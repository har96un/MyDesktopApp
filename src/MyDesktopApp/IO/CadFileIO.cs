using ACadSharp.IO;
using CSMath;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;
using A = ACadSharp;
using AE = ACadSharp.Entities;
using AT = ACadSharp.Tables;

namespace MyDesktopApp.IO;

/// <summary>DWG / DXF okuma ve yazma (ACadSharp kütüphanesi ile).</summary>
public static class CadFileIO
{
    public sealed class ImportReport
    {
        public int Imported { get; set; }
        public Dictionary<string, int> Skipped { get; } = new();
        public List<string> Notes { get; } = new();

        public void Skip(string type)
        {
            Skipped[type] = Skipped.TryGetValue(type, out var n) ? n + 1 : 1;
        }

        public override string ToString()
        {
            var s = $"{Imported} nesne yüklendi.";
            if (Skipped.Count > 0)
                s += " Desteklenmeyen/atlanan: " + string.Join(", ", Skipped.Select(k => $"{k.Key}×{k.Value}"));
            return s;
        }
    }

    // ------------------------------------------------------------------ OKUMA

    public static ImportReport Load(string path, Model.CadDocument target)
    {
        A.CadDocument src = ReadAny(path);
        var report = new ImportReport();

        foreach (var layer in src.Layers)
        {
            var li = target.EnsureLayer(layer.Name);
            li.Color = ToEntColor(layer.Color) ?? new EntColor(255, 255, 255, 7);
            li.Visible = layer.IsOn;
        }

        foreach (var e in src.Entities)
            Convert(e, target, report, null, 0);

        return report;
    }

    private static A.CadDocument ReadAny(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".dwg") return DwgReader.Read(path);
        if (ext == ".dxf") return DxfReader.Read(path);
        throw new NotSupportedException("Yalnızca .dwg ve .dxf dosyaları desteklenir.");
    }

    private static void Convert(AE.Entity e, Model.CadDocument doc, ImportReport rep, AE.Insert? parent, int depth)
    {
        string layer = e.Layer?.Name ?? "0";
        if (parent != null && layer == "0") layer = parent.Layer?.Name ?? "0";

        EntColor? color = null;
        if (e.Color.IsByBlock && parent != null) color = ToEntColor(parent.Color);
        else if (!e.Color.IsByBlock) color = ToEntColor(e.Color);

        void Add(Entity ent)
        {
            ent.Layer = layer;
            ent.Color = color;
            doc.Add(ent);
            rep.Imported++;
        }

        switch (e)
        {
            case AE.Line l:
                Add(new LineEntity(V(l.StartPoint), V(l.EndPoint)));
                break;

            case AE.Arc arc:
                {
                    var c = Ocs(arc.Center, arc.Normal);
                    var sOcs = new XYZ(arc.Center.X + arc.Radius * Math.Cos(arc.StartAngle), arc.Center.Y + arc.Radius * Math.Sin(arc.StartAngle), arc.Center.Z);
                    var eOcs = new XYZ(arc.Center.X + arc.Radius * Math.Cos(arc.EndAngle), arc.Center.Y + arc.Radius * Math.Sin(arc.EndAngle), arc.Center.Z);
                    var s = Ocs(sOcs, arc.Normal);
                    var en = Ocs(eOcs, arc.Normal);
                    if (arc.Normal.Z < 0) (s, en) = (en, s);
                    Add(new ArcEntity(c, arc.Radius, (s - c).Angle, (en - c).Angle));
                    break;
                }

            case AE.Circle circle:
                Add(new CircleEntity(Ocs(circle.Center, circle.Normal), circle.Radius));
                break;

            case AE.LwPolyline lw:
                {
                    var pl = new PolylineEntity { Closed = lw.IsClosed };
                    bool flip = lw.Normal.Z < 0;
                    foreach (var v in lw.Vertices)
                    {
                        var p = Ocs(new XYZ(v.Location.X, v.Location.Y, lw.Elevation), lw.Normal);
                        pl.Vertices.Add(new PolyVertex(p, flip ? -v.Bulge : v.Bulge));
                    }
                    if (pl.Vertices.Count >= 2) Add(pl); else rep.Skip("Boş polyline");
                    break;
                }

            case AE.Polyline2D p2:
                {
                    var pl = new PolylineEntity { Closed = p2.IsClosed };
                    bool flip = p2.Normal.Z < 0;
                    foreach (var v in p2.Vertices)
                    {
                        var p = Ocs(v.Location, p2.Normal);
                        pl.Vertices.Add(new PolyVertex(p, flip ? -v.Bulge : v.Bulge));
                    }
                    if (pl.Vertices.Count >= 2) Add(pl); else rep.Skip("Boş polyline");
                    break;
                }

            case AE.Polyline3D p3:
                {
                    var pts = p3.Vertices.Select(v => V(v.Location)).ToList();
                    if (pts.Count >= 2) Add(new PolylineEntity(pts, p3.IsClosed));
                    break;
                }

            case AE.Ellipse el:
                {
                    double major = Math.Sqrt(el.MajorAxisEndPoint.X * el.MajorAxisEndPoint.X + el.MajorAxisEndPoint.Y * el.MajorAxisEndPoint.Y);
                    if (Math.Abs(el.RadiusRatio - 1) < 1e-9 && Math.Abs(el.Normal.Z) > 0.999999)
                    {
                        var c = V(el.Center);
                        if (el.IsFullEllipse) { Add(new CircleEntity(c, major)); break; }
                        var ptsA = el.PolygonalVertexes(64).Select(V).ToList();
                        if (ptsA.Count >= 3 && GeoUtil.CircleFrom3Points(ptsA[0], ptsA[ptsA.Count / 2], ptsA[^1], out var cc, out var rr))
                        {
                            var a0 = (ptsA[0] - cc).Angle;
                            var a1 = (ptsA[^1] - cc).Angle;
                            bool ccw = Vec2.Cross(ptsA[ptsA.Count / 2] - ptsA[0], ptsA[^1] - ptsA[0]) < 0;
                            Add(ccw ? new ArcEntity(cc, rr, a0, a1) : new ArcEntity(cc, rr, a1, a0));
                            break;
                        }
                    }
                    var pts = el.PolygonalVertexes(128).Select(V).ToList();
                    bool closed = el.IsFullEllipse;
                    if (closed && pts.Count > 2 && pts[0].IsClose(pts[^1], 1e-9)) pts.RemoveAt(pts.Count - 1);
                    if (pts.Count >= 2) Add(new PolylineEntity(pts, closed));
                    break;
                }

            case AE.Spline sp:
                {
                    if (sp.TryPolygonalVertexes(256, out var list) && list.Count >= 2)
                    {
                        var pts = list.Select(V).ToList();
                        bool closed = pts[0].IsClose(pts[^1], 1e-9);
                        if (closed) pts.RemoveAt(pts.Count - 1);
                        Add(new PolylineEntity(pts, closed));
                    }
                    else if (sp.FitPoints.Count >= 2)
                    {
                        Add(new PolylineEntity(sp.FitPoints.Select(V), false));
                    }
                    else if (sp.ControlPoints.Count >= 2)
                    {
                        Add(new PolylineEntity(sp.ControlPoints.Select(V), false));
                    }
                    else rep.Skip("Spline");
                    break;
                }

            case AE.AttributeDefinition:
                break;   // Blok tanımındaki öznitelik şablonları çizilmez

            case AE.TextEntity t:
                {
                    var p = Ocs(t.InsertPoint, t.Normal);
                    Add(new Model.TextEntity(p, t.Height, t.Rotation, t.Value ?? ""));
                    break;
                }

            case AE.MText mt:
                {
                    string val = StripMText(mt.Value ?? "");
                    Add(new Model.TextEntity(V(mt.InsertPoint) - new Vec2(0, mt.Height), mt.Height, mt.Rotation, val));
                    break;
                }

            case AE.Point:
                rep.Skip("Nokta");
                break;

            case AE.Solid solid:
                {
                    var pts = new List<Vec2> { V(solid.FirstCorner), V(solid.SecondCorner), V(solid.FourthCorner), V(solid.ThirdCorner) };
                    Add(new PolylineEntity(pts.Distinct(), true));
                    break;
                }

            case AE.Insert ins:
                {
                    if (depth > 16 || ins.Block == null) { rep.Skip("Blok"); break; }
                    IEnumerable<AE.Entity> parts;
                    try { parts = ins.Explode().ToList(); }
                    catch { rep.Skip("Blok"); break; }
                    foreach (var sub in parts)
                        Convert(sub, doc, rep, ins, depth + 1);
                    break;
                }

            default:
                rep.Skip(e.GetType().Name);
                break;
        }
    }

    private static Vec2 V(XYZ p) => new(p.X, p.Y);

    /// <summary>OCS (nesne koordinat sistemi) → WCS dönüşümü (AutoCAD "arbitrary axis" algoritması).</summary>
    private static Vec2 Ocs(XYZ p, XYZ n)
    {
        if (Math.Abs(n.X) < 1e-12 && Math.Abs(n.Y) < 1e-12 && n.Z > 0) return new Vec2(p.X, p.Y);
        double nl = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
        if (nl < 1e-12) return new Vec2(p.X, p.Y);
        double nx = n.X / nl, ny = n.Y / nl, nz = n.Z / nl;
        double ax, ay, az;
        if (Math.Abs(nx) < 1.0 / 64 && Math.Abs(ny) < 1.0 / 64)
        {   // Wy × N
            ax = 1 * nz - 0 * ny; ay = 0 * nx - 0 * nz; az = 0 * ny - 1 * nx;
        }
        else
        {   // Wz × N
            ax = 0 * nz - 1 * ny; ay = 1 * nx - 0 * nz; az = 0;
        }
        double al = Math.Sqrt(ax * ax + ay * ay + az * az);
        ax /= al; ay /= al; az /= al;
        // Ay = N × Ax
        double bx = ny * az - nz * ay;
        double by = nz * ax - nx * az;
        double bl = Math.Sqrt(bx * bx + by * by + (nx * ay - ny * ax) * (nx * ay - ny * ax));
        bx /= bl; by /= bl;
        return new Vec2(p.X * ax + p.Y * bx + p.Z * nx, p.X * ay + p.Y * by + p.Z * ny);
    }

    private static EntColor? ToEntColor(A.Color c)
    {
        if (c.IsByLayer || c.IsByBlock) return null;
        try
        {
            if (c.IsTrueColor) return new EntColor(c.R, c.G, c.B, -1);
            short idx = c.Index;
            if (idx == 7 || idx == 0) return new EntColor(255, 255, 255, 7);
            return new EntColor(c.R, c.G, c.B, idx);
        }
        catch
        {
            return null;
        }
    }

    private static string StripMText(string s)
    {
        // Basit MText biçim kodu temizleme
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '{' || ch == '}') continue;
            if (ch == '\\' && i + 1 < s.Length)
            {
                char k = s[i + 1];
                if (k == 'P') { sb.Append(' '); i++; continue; }
                if ("fFHWQTACcpL".IndexOf(k) >= 0 || k == 'l' || k == 'O' || k == 'o' || k == 'K' || k == 'k')
                {
                    int semi = s.IndexOf(';', i);
                    if ("LlOoKk".IndexOf(k) >= 0) { i++; continue; }
                    if (semi > 0) { i = semi; continue; }
                }
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ YAZMA

    public static void Save(string path, Model.CadDocument source, IEnumerable<Entity>? only = null)
    {
        var doc = new A.CadDocument();

        foreach (var li in source.Layers.Values)
        {
            if (!doc.Layers.TryGetValue(li.Name, out var layer))
            {
                layer = new AT.Layer(li.Name);
                doc.Layers.Add(layer);
            }
            layer.Color = ToAcadColor(li.Color);
            layer.IsOn = li.Visible;
        }

        foreach (var e in only ?? source.Entities)
        {
            var list = ToAcad(e).ToList();
            foreach (var ae in list)
            {
                if (doc.Layers.TryGetValue(e.Layer, out var layer)) ae.Layer = layer;
                ae.Color = e.Color is { } c ? ToAcadColor(c) : A.Color.ByLayer;
                doc.Entities.Add(ae);
            }
        }

        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".dwg") DwgWriter.Write(path, doc);
        else DxfWriter.Write(path, doc, false);
    }

    private static A.Color ToAcadColor(EntColor c)
    {
        if (c.Aci > 0 && c.Aci < 256) return new A.Color(c.Aci);
        return new A.Color(c.R, c.G, c.B);
    }

    private static XYZ P(Vec2 v) => new(v.X, v.Y, 0);

    private static IEnumerable<AE.Entity> ToAcad(Entity e)
    {
        switch (e)
        {
            case LineEntity l:
                yield return new AE.Line { StartPoint = P(l.Start), EndPoint = P(l.End) };
                break;
            case CircleEntity c:
                yield return new AE.Circle { Center = P(c.Center), Radius = c.Radius };
                break;
            case ArcEntity a:
                yield return new AE.Arc
                {
                    Center = P(a.Center),
                    Radius = a.Radius,
                    StartAngle = GeoUtil.NormalizeAngle(a.StartAngle),
                    EndAngle = GeoUtil.NormalizeAngle(a.EndAngle)
                };
                break;
            case PolylineEntity pl:
                {
                    var lw = new AE.LwPolyline();
                    foreach (var v in pl.Vertices)
                        lw.Vertices.Add(new AE.LwPolyline.Vertex(new XY(v.P.X, v.P.Y)) { Bulge = v.Bulge });
                    lw.IsClosed = pl.Closed;
                    yield return lw;
                    break;
                }
            case Model.TextEntity t:
                yield return new AE.TextEntity
                {
                    InsertPoint = P(t.Position),
                    Height = t.Height,
                    Rotation = t.Rotation,
                    Value = t.Value
                };
                break;
        }
    }
}
