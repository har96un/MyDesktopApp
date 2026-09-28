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
        public int Groups { get; set; }
        public Dictionary<string, int> Skipped { get; } = new();
        public List<string> Notes { get; } = new();

        public void Skip(string type)
        {
            Skipped[type] = Skipped.TryGetValue(type, out var n) ? n + 1 : 1;
        }

        public override string ToString()
        {
            var s = $"{Imported} nesne yüklendi" + (Groups > 0 ? $", {Groups} grup." : ".");
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

        // Her kaynak nesneden üretilen nesneleri izle (gruplar için)
        var produced = new Dictionary<AE.Entity, List<Entity>>();
        foreach (var e in src.Entities)
        {
            int before = target.Entities.Count;
            Convert(e, target, report, null, 0);
            if (target.Entities.Count > before)
                produced[e] = target.Entities.GetRange(before, target.Entities.Count - before);
        }

        // Gruplar
        try
        {
            if (src.Groups != null)
            {
                foreach (var g in src.Groups)
                {
                    string name = string.IsNullOrWhiteSpace(g.Name) || g.Name.StartsWith("*") ? target.NewGroupName() : g.Name;
                    int n = 0;
                    foreach (var ge in g.Entities)
                        if (produced.TryGetValue(ge, out var list))
                            foreach (var me in list) { me.GroupId = name; n++; }
                    if (n > 0) report.Groups++;
                }
            }
        }
        catch (Exception ex)
        {
            report.Notes.Add("Gruplar okunamadı: " + ex.Message);
        }

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

            case AE.DimensionLinear dl:
                AddDimension(dl, DimKind.Linear, doc, rep, layer, color);
                break;
            case AE.DimensionAligned da:
                AddDimension(da, DimKind.Aligned, doc, rep, layer, color);
                break;
            case AE.DimensionRadius dr:
                AddDimension(dr, DimKind.Radius, doc, rep, layer, color);
                break;
            case AE.DimensionDiameter dd:
                AddDimension(dd, DimKind.Diameter, doc, rep, layer, color);
                break;
            case AE.DimensionAngular2Line dang:
                AddDimension(dang, DimKind.Angular, doc, rep, layer, color);
                break;
            case AE.Dimension other:
                {
                    // Diğer ölçü türleri: blok geometrisini al
                    if (other.Block != null && depth < 16)
                        foreach (var sub in other.Block.Entities.ToList())
                            Convert(sub, doc, rep, null, depth + 1);
                    else rep.Skip("Ölçü");
                    break;
                }

            case AE.Hatch ht:
                {
                    var h = new HatchEntity
                    {
                        Pattern = ht.IsSolid ? "SOLID" : NormalizePattern(ht.Pattern?.Name),
                        Scale = ht.PatternScale > 0 ? ht.PatternScale : 1,
                        Angle = ht.PatternAngle
                    };
                    bool flip = ht.Normal.Z < 0;
                    foreach (var path in ht.Paths)
                    {
                        var loop = new List<PolyVertex>();
                        var poly = path.Edges.OfType<AE.Hatch.BoundaryPath.Polyline>().FirstOrDefault();
                        if (poly != null)
                        {
                            foreach (var v in poly.Vertices)
                                loop.Add(new PolyVertex(Ocs(new XYZ(v.X, v.Y, ht.Elevation), ht.Normal), flip ? -v.Z : v.Z));
                        }
                        else
                        {
                            foreach (var v in path.GetPoints(32))
                            {
                                var p = Ocs(new XYZ(v.X, v.Y, ht.Elevation), ht.Normal);
                                if (loop.Count == 0 || !loop[^1].P.IsClose(p, 1e-9)) loop.Add(new PolyVertex(p));
                            }
                            if (loop.Count > 1 && loop[0].P.IsClose(loop[^1].P, 1e-9)) loop.RemoveAt(loop.Count - 1);
                        }
                        if (loop.Count >= 3) h.Loops.Add(loop);
                    }
                    if (h.Loops.Count > 0) Add(h); else rep.Skip("Tarama");
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

    private static string NormalizePattern(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "ANSI31";
        var up = name.ToUpperInvariant();
        return HatchEntity.Patterns.Contains(up) ? up : "ANSI31";
    }

    private static void AddDimension(AE.Dimension src, DimKind kind, Model.CadDocument doc, ImportReport rep, string layer, EntColor? color)
    {
        var d = new DimensionEntity { Kind = kind, Layer = layer, Color = color };
        try
        {
            var st = src.GetActiveDimensionStyle();
            double sf = st.ScaleFactor > 0 ? st.ScaleFactor : 1;
            d.TextHeight = st.TextHeight * sf;
            d.ArrowSize = st.ArrowSize * sf;
            d.Decimals = st.DecimalPlaces;
        }
        catch
        {
            d.TextHeight = doc.DimTextHeight;
            d.ArrowSize = doc.DimArrowSize;
            d.Decimals = doc.DimDecimals;
        }
        if (!string.IsNullOrEmpty(src.Text) && src.Text != "<>") d.TextOverride = src.Text;

        switch (src)
        {
            case AE.DimensionLinear l:
                d.P1 = V(l.FirstPoint); d.P2 = V(l.SecondPoint); d.Location = V(l.DefinitionPoint); d.Rotation = l.Rotation;
                break;
            case AE.DimensionAligned a:
                d.P1 = V(a.FirstPoint); d.P2 = V(a.SecondPoint); d.Location = V(a.DefinitionPoint);
                break;
            case AE.DimensionRadius r:
                d.Center = V(r.DefinitionPoint); d.P1 = V(r.AngleVertex); d.Location = V(r.TextMiddlePoint);
                break;
            case AE.DimensionDiameter dm:
                d.P1 = V(dm.AngleVertex); d.Center = (V(dm.AngleVertex) + V(dm.DefinitionPoint)) / 2; d.Location = V(dm.TextMiddlePoint);
                break;
            case AE.DimensionAngular2Line ang:
                {
                    var a1 = V(ang.FirstPoint); var a2 = V(ang.SecondPoint);
                    var b1 = V(ang.AngleVertex); var b2 = V(ang.DefinitionPoint);
                    if (!GeoUtil.SegmentIntersect(a1, a2, b1, b2, out var vx, infinite: true)) { rep.Skip("Açı ölçüsü"); return; }
                    d.Center = vx;
                    d.P1 = Vec2.Distance(a1, vx) > Vec2.Distance(a2, vx) ? a1 : a2;
                    d.P2 = Vec2.Distance(b1, vx) > Vec2.Distance(b2, vx) ? b1 : b2;
                    d.Location = V(ang.DimensionArc);
                    break;
                }
        }
        doc.Add(d);
        rep.Imported++;
    }

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
        try
        {
            SaveCore(path, source, only, withGroups: true);
        }
        catch when (source.Entities.Any(e => e.GroupId != null))
        {
            // Grup yazımı desteklenmezse gruplar olmadan kaydet
            SaveCore(path, source, only, withGroups: false);
        }
    }

    private static void SaveCore(string path, Model.CadDocument source, IEnumerable<Entity>? only, bool withGroups)
    {
        var doc = new A.CadDocument();
        var groupMap = new Dictionary<string, List<AE.Entity>>();

        // Ölçü stili
        try
        {
            if (doc.DimensionStyles.TryGetValue(AT.DimensionStyle.DefaultName, out var ds))
            {
                ds.TextHeight = source.DimTextHeight;
                ds.ArrowSize = source.DimArrowSize;
                ds.DecimalPlaces = (short)source.DimDecimals;
            }
        }
        catch { /* stil ayarlanamazsa varsayılan kalır */ }

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
            if (e is DimensionEntity && list.Count == 1 && list[0] is AE.Dimension adim)
            {
                // Gerçek ölçü nesnesi; blok üretilemezse parçalara ayrılmış olarak yaz
                if (!TryAddDimension(doc, adim, e))
                    list = ((DimensionEntity)e).Explode().SelectMany(ToAcad).ToList();
                else
                {
                    AddGroupMember(groupMap, e, adim);
                    continue;
                }
            }
            foreach (var ae in list)
            {
                if (doc.Layers.TryGetValue(e.Layer, out var layer)) ae.Layer = layer;
                ae.Color = e.Color is { } c ? ToAcadColor(c) : A.Color.ByLayer;
                doc.Entities.Add(ae);
                AddGroupMember(groupMap, e, ae);
            }
        }

        if (withGroups && doc.Groups != null)
            foreach (var kv in groupMap)
                doc.Groups.CreateGroup(kv.Key, kv.Value);

        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".dwg") DwgWriter.Write(path, doc);
        else DxfWriter.Write(path, doc, false);
    }

    private static void AddGroupMember(Dictionary<string, List<AE.Entity>> map, Entity e, AE.Entity ae)
    {
        if (e.GroupId == null) return;
        if (!map.TryGetValue(e.GroupId, out var gl)) map[e.GroupId] = gl = new List<AE.Entity>();
        gl.Add(ae);
    }

    private static bool TryAddDimension(A.CadDocument doc, AE.Dimension adim, Entity e)
    {
        try
        {
            if (doc.Layers.TryGetValue(e.Layer, out var layer)) adim.Layer = layer;
            adim.Color = e.Color is { } c ? ToAcadColor(c) : A.Color.ByLayer;
            doc.Entities.Add(adim);
            try
            {
                adim.UpdateBlock();
                return true;
            }
            catch
            {
                doc.Entities.Remove(adim);
                return false;
            }
        }
        catch
        {
            return false;
        }
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
            case DimensionEntity d:
                {
                    AE.Dimension? ad = null;
                    var g = d.Build();
                    var textPt = g.Texts.Count > 0 ? P(g.Texts[0].Anchor) : P(d.Location);
                    switch (d.Kind)
                    {
                        case DimKind.Linear:
                        case DimKind.Aligned:
                            {
                                var dir = d.Kind == DimKind.Linear ? Vec2.Polar(1, d.Rotation) : (d.P2 - d.P1).Normalized();
                                var b = d.Location + dir * Vec2.Dot(d.P2 - d.Location, dir);
                                if (d.Kind == DimKind.Linear)
                                    ad = new AE.DimensionLinear { FirstPoint = P(d.P1), SecondPoint = P(d.P2), DefinitionPoint = P(b), Rotation = d.Rotation };
                                else
                                    ad = new AE.DimensionAligned(P(d.P1), P(d.P2)) { DefinitionPoint = P(b) };
                                break;
                            }
                        case DimKind.Radius:
                            {
                                double r = Vec2.Distance(d.Center, d.P1);
                                var dir = (d.Location - d.Center).Normalized();
                                if (dir.LengthSquared < 1e-20) dir = Vec2.UnitX;
                                ad = new AE.DimensionRadius { DefinitionPoint = P(d.Center), AngleVertex = P(d.Center + dir * r) };
                                break;
                            }
                        case DimKind.Diameter:
                            {
                                double r = Vec2.Distance(d.Center, d.P1);
                                var dir = (d.Location - d.Center).Normalized();
                                if (dir.LengthSquared < 1e-20) dir = Vec2.UnitX;
                                ad = new AE.DimensionDiameter { DefinitionPoint = P(d.Center - dir * r), AngleVertex = P(d.Center + dir * r) };
                                break;
                            }
                        case DimKind.Angular:
                            ad = new AE.DimensionAngular2Line
                            {
                                FirstPoint = P(d.Center),
                                SecondPoint = P(d.P1),
                                AngleVertex = P(d.Center),
                                DefinitionPoint = P(d.P2),
                                DimensionArc = P(d.Location)
                            };
                            break;
                    }
                    if (ad != null)
                    {
                        ad.TextMiddlePoint = textPt;
                        if (!string.IsNullOrEmpty(d.TextOverride)) ad.Text = d.TextOverride;
                        yield return ad;
                    }
                    break;
                }
            case HatchEntity h:
                {
                    var hatch = new AE.Hatch();
                    if (h.IsSolid)
                    {
                        hatch.IsSolid = true;
                        hatch.PatternType = AE.HatchPatternType.SolidFill;
                        hatch.Pattern = AE.HatchPattern.Solid;
                    }
                    else
                    {
                        hatch.IsSolid = false;
                        hatch.PatternType = AE.HatchPatternType.Custom;
                        // Ölçek/açı önce, desen çizgileri sonra (ayarlayıcılar deseni yeniden dönüştürür)
                        hatch.PatternScale = h.Scale;
                        hatch.PatternAngle = h.Angle;
                        var pat = new AE.HatchPattern(h.Pattern.ToUpperInvariant());
                        foreach (var (ang, sp) in h.Families())
                        {
                            var off = Vec2.Polar(sp, ang + Math.PI / 2);
                            pat.Lines.Add(new AE.HatchPattern.Line { Angle = ang, BasePoint = new XY(0, 0), Offset = new XY(off.X, off.Y) });
                        }
                        hatch.Pattern = pat;
                    }
                    bool first = true;
                    foreach (var loop in h.Loops)
                    {
                        var path = new AE.Hatch.BoundaryPath();
                        path.Edges.Add(new AE.Hatch.BoundaryPath.Polyline(loop.Select(v => new XYZ(v.P.X, v.P.Y, v.Bulge)), true));
                        path.Flags = first ? AE.BoundaryPathFlags.External : AE.BoundaryPathFlags.Default;
                        hatch.Paths.Add(path);
                        first = false;
                    }
                    yield return hatch;
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
