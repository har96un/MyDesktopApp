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
        /// <summary>Dosyanın AutoCAD sürümü (ör. "AutoCAD 2010").</summary>
        public string VersionName { get; set; } = "";
        /// <summary>Aynı sürümle kaydetmek için önerilen kayıt biçimi.</summary>
        public string? FormatId { get; set; }

        public void Skip(string type)
        {
            Skipped[type] = Skipped.TryGetValue(type, out var n) ? n + 1 : 1;
        }

        public override string ToString()
        {
            var s = (VersionName.Length > 0 ? $"[{VersionName}] " : "") + $"{Imported} nesne yüklendi" + (Groups > 0 ? $", {Groups} grup." : ".");
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
        try
        {
            var ver = src.Header.Version;
            report.VersionName = VersionName(ver);
            bool dwg = System.IO.Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase);
            report.FormatId = Formats.FirstOrDefault(f => f.Ext == (dwg ? ".dwg" : ".dxf") && !f.Binary && f.Version == ver)?.Id;
        }
        catch { /* sürüm bilgisi yok */ }

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
                    // Kendi (hızlı) NURBS değerlendirmemiz; veri tutarsızsa kütüphaneye düş.
                    // (Kütüphane işlevi büyük çizimlerde açılış süresinin 2/3'ünü alıyordu.)
                    var fast = EvalSpline(sp, 256);
                    List<XYZ>? list = null;
                    if (fast != null || (sp.TryPolygonalVertexes(256, out list) && list.Count >= 2))
                    {
                        var pts = fast ?? list!.Select(V).ToList();
                        bool closed = pts[0].IsClose(pts[^1], 1e-9);
                        // Sabit 256 nokta yerine eğriliğe göre sadeleştir (boyutun ~1/5000'i sapma):
                        // büyük çizimlerde spline'lar milyonlarca gereksiz köşe üretiyordu.
                        var sb = BBox.Empty;
                        foreach (var q in pts) sb.Add(q);
                        double tol = Math.Max(Math.Max(sb.Width, sb.Height) * 2e-4, 1e-9);
                        pts = GeoUtil.Simplify(pts, tol);
                        if (closed && pts.Count > 1 && pts[0].IsClose(pts[^1], 1e-9)) pts.RemoveAt(pts.Count - 1);
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
                    if (TryKeepBlock(ins, doc, rep, layer, color, depth)) goto Attributes;
                    // Blok içeriğini blok koordinatlarında dönüştür, sonra kendi dönüşümümüzle yerleştir.
                    // (Kütüphanenin Explode() işlevi aynalanmış bloklarda yay yönlerini çevirmediği için kullanılmıyor.)
                    var tmp = new Model.CadDocument();
                    var subRep = new ImportReport();
                    foreach (var sub in ins.Block.Entities.ToList())
                        Convert(sub, tmp, subRep, ins, depth + 1);
                    foreach (var kv in subRep.Skipped)
                        rep.Skipped[kv.Key] = rep.Skipped.TryGetValue(kv.Key, out var n0) ? n0 + kv.Value : kv.Value;

                    int rows = Math.Max(1, (int)ins.RowCount), cols = Math.Max(1, (int)ins.ColumnCount);
                    for (int r = 0; r < rows; r++)
                        for (int cI = 0; cI < cols; cI++)
                        {
                            var m = InsertMatrix(ins, cI * ins.ColumnSpacing, r * ins.RowSpacing);
                            bool conformal = IsConformal(m);
                            foreach (var te in tmp.Entities)
                            {
                                var ce = conformal ? te.Clone() : Linearize(te);
                                ce.Transform(m);
                                if (ce.Layer == "0") ce.Layer = layer;
                                doc.EnsureLayer(ce.Layer);
                                if (tmp.Layers.TryGetValue(ce.Layer, out var tl) && !doc.Layers.ContainsKey(ce.Layer)) doc.Layers[ce.Layer] = tl.Clone();
                                doc.Add(ce);
                                rep.Imported++;
                            }
                        }
                    Attributes:
                    // Öznitelik (attribute) yazıları dünya koordinatlarındadır
                    try
                    {
                        foreach (var att in ins.Attributes)
                            if (!att.IsInvisible && !string.IsNullOrEmpty(att.Value))
                            {
                                var p = Ocs(att.InsertPoint, att.Normal);
                                var te = new Model.TextEntity(p, att.Height, att.Rotation, att.Value) { Layer = layer, Color = color };
                                doc.Add(te);
                                rep.Imported++;
                            }
                    }
                    catch { /* öznitelik okunamadı */ }
                    break;
                }

            default:
                rep.Skip(e.GetType().Name);
                break;
        }
    }

    private static Vec2 V(XYZ p) => new(p.X, p.Y);

    /// <summary>NURBS eğrisini de Boor algoritmasıyla eşit parametre aralıklarında değerlendirir (XY düzlemi).</summary>
    internal static List<Vec2>? EvalSpline(AE.Spline sp, int samples)
    {
        try
        {
            int p = sp.Degree;
            var cps = sp.ControlPoints;
            var U = sp.Knots;
            int n = cps.Count;
            if (p < 1 || n < p + 1 || U.Count != n + p + 1) return null;
            var W = sp.Weights;
            bool rational = W != null && W.Count == n;
            double t0 = U[p], t1 = U[n];
            if (!(t1 > t0)) return null;
            var dx = new double[p + 1];
            var dy = new double[p + 1];
            var dw = new double[p + 1];
            var res = new List<Vec2>(samples + 1);
            for (int s = 0; s <= samples; s++)
            {
                double t = s == samples ? t1 : t0 + (t1 - t0) * s / samples;
                // Düğüm aralığı: U[k] <= t < U[k+1], k ∈ [p, n-1]
                int k;
                if (t >= U[n]) k = n - 1;
                else
                {
                    int lo = p, hi = n;
                    while (hi - lo > 1)
                    {
                        int mid = (lo + hi) / 2;
                        if (t < U[mid]) hi = mid; else lo = mid;
                    }
                    k = lo;
                }
                for (int j = 0; j <= p; j++)
                {
                    int i = k - p + j;
                    double w = rational ? W![i] : 1.0;
                    dx[j] = cps[i].X * w;
                    dy[j] = cps[i].Y * w;
                    dw[j] = w;
                }
                for (int r = 1; r <= p; r++)
                {
                    for (int j = p; j >= r; j--)
                    {
                        int i = k - p + j;
                        double den = U[i + p - r + 1] - U[i];
                        double a = den == 0 ? 0 : (t - U[i]) / den;
                        dx[j] = (1 - a) * dx[j - 1] + a * dx[j];
                        dy[j] = (1 - a) * dy[j - 1] + a * dy[j];
                        dw[j] = (1 - a) * dw[j - 1] + a * dw[j];
                    }
                }
                if (Math.Abs(dw[p]) < 1e-300) return null;
                var q = new Vec2(dx[p] / dw[p], dy[p] / dw[p]);
                if (double.IsNaN(q.X) || double.IsNaN(q.Y)) return null;
                res.Add(q);
            }
            return res.Count >= 2 ? res : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Adlı, dizisiz ve eşit ölçekli üst düzey blokları blok referansı olarak korur (tanım bir kez oluşturulur).
    /// Uygun değilse false döner ve blok patlatılarak alınır.
    /// </summary>
    private static bool TryKeepBlock(AE.Insert ins, Model.CadDocument doc, ImportReport rep, string layer, EntColor? color, int depth)
    {
        try
        {
            if (depth != 0 || ins.Block == null) return false;
            if (ins.RowCount > 1 || ins.ColumnCount > 1) return false;
            string name = ins.Block.Name ?? "";
            if (name.Length == 0 || name.StartsWith("*")) return false;
            var m0 = InsertMatrix(ins, 0, 0);
            if (!IsConformal(m0) || Math.Abs(m0.Determinant) < 1e-18) return false;

            if (!doc.Blocks.TryGetValue(name, out var bd))
            {
                var tmp = new Model.CadDocument();
                var subRep = new ImportReport();
                foreach (var sub in ins.Block.Entities.ToList())
                    Convert(sub, tmp, subRep, null, depth + 1);
                if (tmp.Entities.Count == 0) return false;
                foreach (var kv in subRep.Skipped)
                    rep.Skipped[kv.Key] = rep.Skipped.TryGetValue(kv.Key, out var n0) ? n0 + kv.Value : kv.Value;
                var bp = ins.Block.BlockEntity?.BasePoint ?? XYZ.Zero;
                var toLocal = Mat2D.Translation(new Vec2(-bp.X, -bp.Y));
                bd = new BlockDef(name);
                foreach (var te in tmp.Entities)
                {
                    te.Transform(toLocal);
                    te.GroupId = null;
                    bd.Entities.Add(te);
                }
                foreach (var tl in tmp.Layers.Values)
                    if (!doc.Layers.ContainsKey(tl.Name)) doc.Layers[tl.Name] = tl.Clone();
                doc.Blocks[name] = bd;
            }
            var bp2 = ins.Block.BlockEntity?.BasePoint ?? XYZ.Zero;
            var m = Mat2D.Then(Mat2D.Translation(new Vec2(bp2.X, bp2.Y)), m0);
            var bref = new BlockRefEntity(bd, m) { Layer = layer, Color = color };
            doc.Add(bref);
            rep.Imported++;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Blok yerleştirme dönüşümü: taban noktası → ölçek → dizi ofseti → dönme → konum → OCS.</summary>
    private static Mat2D InsertMatrix(AE.Insert ins, double offX, double offY)
    {
        var bp = ins.Block?.BlockEntity?.BasePoint ?? XYZ.Zero;
        double sx = ins.XScale == 0 ? 1 : ins.XScale, sy = ins.YScale == 0 ? 1 : ins.YScale;
        var m = Mat2D.Translation(new Vec2(-bp.X, -bp.Y));
        m = Mat2D.Then(m, new Mat2D(sx, 0, 0, sy, 0, 0));
        m = Mat2D.Then(m, Mat2D.Translation(new Vec2(offX, offY)));
        m = Mat2D.Then(m, Mat2D.Rotation(ins.Rotation, Vec2.Zero));
        m = Mat2D.Then(m, Mat2D.Translation(new Vec2(ins.InsertPoint.X, ins.InsertPoint.Y)));
        OcsAxes(ins.Normal, out double ax, out double ay, out double bx, out double by);
        return Mat2D.Then(m, new Mat2D(ax, bx, ay, by, 0, 0));
    }

    /// <summary>Dönüşüm açıları ve oranları koruyor mu (daire daire olarak kalır mı)?</summary>
    private static bool IsConformal(Mat2D m)
    {
        double c1 = m.A * m.A + m.C * m.C, c2 = m.B * m.B + m.D * m.D, dot = m.A * m.B + m.C * m.D;
        double s = Math.Max(Math.Max(c1, c2), 1e-30);
        return Math.Abs(c1 - c2) <= 1e-9 * s && Math.Abs(dot) <= 1e-9 * s;
    }

    /// <summary>Eşit olmayan ölçekli bloklarda daire/yayları çoklu çizgiye çevirir (elips olarak doğru görünsün).</summary>
    private static Entity Linearize(Entity e)
    {
        Entity res;
        switch (e)
        {
            case CircleEntity:
            case ArcEntity:
            case PolylineEntity when ((PolylineEntity)e).VertexView.Any(v => Math.Abs(v.Bulge) > 1e-12):
                {
                    var pts = e.ToPoints();
                    bool closed = e.IsClosed;
                    if (closed && pts.Count > 2 && pts[0].IsClose(pts[^1], 1e-9)) pts.RemoveAt(pts.Count - 1);
                    res = new PolylineEntity(pts, closed);
                    break;
                }
            case HatchEntity h:
                {
                    var nh = (HatchEntity)h.Clone();
                    nh.Loops.Clear();
                    foreach (var poly in h.LoopPolygons()) nh.Loops.Add(poly.Select(p => new PolyVertex(p)).ToList());
                    res = nh;
                    break;
                }
            default:
                return e.Clone();
        }
        res.Layer = e.Layer;
        res.Color = e.Color;
        res.GroupId = e.GroupId;
        return res;
    }

    /// <summary>OCS eksenlerinin dünya XY bileşenleri (AutoCAD "arbitrary axis").</summary>
    private static void OcsAxes(XYZ n, out double ax, out double ay, out double bx, out double by)
    {
        var x = Ocs(new XYZ(1, 0, 0), n);
        var y = Ocs(new XYZ(0, 1, 0), n);
        ax = x.X; ay = x.Y; bx = y.X; by = y.Y;
    }

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

    // ------------------------------------------------------------------ KAYIT BİÇİMLERİ

    /// <summary>Kayıt biçimi: uzantı, AutoCAD sürümü, ikili/ASCII.</summary>
    public sealed record SaveFormat(string Id, string Label, string Ext, A.ACadVersion Version, bool Binary = false, bool R12 = false)
    {
        public string Filter => $"{Label} (*{Ext})|*{Ext}";
    }

    public static readonly SaveFormat[] Formats =
    {
        new("dxf2018", "AutoCAD 2018 DXF", ".dxf", A.ACadVersion.AC1032),
        new("dxf2013", "AutoCAD 2013 DXF", ".dxf", A.ACadVersion.AC1027),
        new("dxf2010", "AutoCAD 2010 DXF", ".dxf", A.ACadVersion.AC1024),
        new("dxf2007", "AutoCAD 2007 DXF", ".dxf", A.ACadVersion.AC1021),
        new("dxf2004", "AutoCAD 2004 DXF", ".dxf", A.ACadVersion.AC1018),
        new("dxf2000", "AutoCAD 2000 DXF", ".dxf", A.ACadVersion.AC1015),
        new("dxfr14", "AutoCAD R14 DXF", ".dxf", A.ACadVersion.AC1014),
        new("dxfr12", "AutoCAD R12/LT2 DXF - CNC, lazer, abkant uyumlu", ".dxf", A.ACadVersion.AC1009, R12: true),
        new("dxfb2018", "AutoCAD 2018 İkili (binary) DXF", ".dxf", A.ACadVersion.AC1032, Binary: true),
        new("dxfb2010", "AutoCAD 2010 İkili (binary) DXF", ".dxf", A.ACadVersion.AC1024, Binary: true),
        new("dwg2018", "AutoCAD 2018 DWG", ".dwg", A.ACadVersion.AC1032),
        new("dwg2013", "AutoCAD 2013 DWG", ".dwg", A.ACadVersion.AC1027),
        new("dwg2010", "AutoCAD 2010 DWG", ".dwg", A.ACadVersion.AC1024),
        new("dwg2004", "AutoCAD 2004 DWG", ".dwg", A.ACadVersion.AC1018),
        new("dwg2000", "AutoCAD 2000 DWG", ".dwg", A.ACadVersion.AC1015),
        new("dwgr14", "AutoCAD R14 DWG", ".dwg", A.ACadVersion.AC1014),
    };

    public static string SaveFilter => string.Join("|", Formats.Select(f => f.Filter));

    public static SaveFormat? FindFormat(string? id) => Formats.FirstOrDefault(f => f.Id == id);

    /// <summary>Uzantıya göre varsayılan biçim (DWG 2007 yazılamadığı için en yakın sürüme iner).</summary>
    public static SaveFormat DefaultFor(string path, string? preferredId)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var pref = FindFormat(preferredId);
        if (pref != null && pref.Ext == ext) return pref;
        if (pref != null && ext == ".dwg")
        {
            // Tercih edilen DXF sürümünün DWG karşılığı (2007 → 2004)
            var dw = Formats.Where(f => f.Ext == ".dwg" && f.Version <= pref.Version).OrderByDescending(f => f.Version).FirstOrDefault();
            if (dw != null) return dw;
        }
        if (pref != null && ext == ".dxf" && pref.Ext == ".dwg")
            return Formats.First(f => f.Ext == ".dxf" && !f.Binary && f.Version == pref.Version);
        return ext == ".dwg" ? Formats.First(f => f.Id == "dwg2018") : Formats.First(f => f.Id == "dxf2018");
    }

    public static string VersionName(A.ACadVersion v) => v switch
    {
        A.ACadVersion.AC1009 => "AutoCAD R11/R12",
        A.ACadVersion.AC1012 => "AutoCAD R13",
        A.ACadVersion.AC1014 => "AutoCAD R14",
        A.ACadVersion.AC1015 => "AutoCAD 2000",
        A.ACadVersion.AC1018 => "AutoCAD 2004",
        A.ACadVersion.AC1021 => "AutoCAD 2007",
        A.ACadVersion.AC1024 => "AutoCAD 2010",
        A.ACadVersion.AC1027 => "AutoCAD 2013",
        A.ACadVersion.AC1032 => "AutoCAD 2018",
        _ => v.ToString()
    };

    public static void Save(string path, Model.CadDocument source, IEnumerable<Entity>? only = null, SaveFormat? format = null)
    {
        format ??= DefaultFor(path, null);
        if (format.R12)
        {
            DxfR12Writer.Write(path, source, only);
            return;
        }
        var items = (only ?? source.Entities).ToList();
        bool hasExtras = items.Any(e => e.GroupId != null || e is BlockRefEntity);
        try
        {
            SaveCore(path, source, items, withGroups: true, withBlocks: true, format);
        }
        catch when (hasExtras)
        {
            // Grup / blok yazımı desteklenmezse gruplar olmadan ve bloklar patlatılmış olarak kaydet
            SaveCore(path, source, items, withGroups: false, withBlocks: false, format);
        }
    }

    private static void SaveCore(string path, Model.CadDocument source, IEnumerable<Entity>? only, bool withGroups, bool withBlocks, SaveFormat format)
    {
        var doc = new A.CadDocument(format.Version);
        // 2007 öncesi sürümler kod sayfası kullanır: Türkçe karakterler için Windows-1254
        if (format.Version < A.ACadVersion.AC1021) doc.Header.CodePage = "ANSI_1254";
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

        var records = new Dictionary<BlockDef, AT.BlockRecord>();
        foreach (var e0 in only ?? source.Entities)
        {
            if (e0 is BlockRefEntity bref0)
            {
                if (withBlocks)
                {
                    var ins = MakeInsert(doc, bref0, records, 0);
                    if (doc.Layers.TryGetValue(e0.Layer, out var il)) ins.Layer = il;
                    ins.Color = e0.Color is { } ic ? ToAcadColor(ic) : A.Color.ByLayer;
                    doc.Entities.Add(ins);
                    AddGroupMember(groupMap, e0, ins);
                    continue;
                }
            }
            foreach (var e in e0 is BlockRefEntity ? BlockRefEntity.Flatten(new[] { e0 }) : new[] { e0 })
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
        }

        if (withGroups && doc.Groups != null)
            foreach (var kv in groupMap)
                doc.Groups.CreateGroup(kv.Key, kv.Value);

        if (format.Ext == ".dwg") DwgWriter.Write(path, doc);
        else DxfWriter.Write(path, doc, format.Binary);
    }

    /// <summary>Blok referansını gerçek BLOCK + INSERT olarak yazar (tanım bir kez oluşturulur).</summary>
    private static AE.Insert MakeInsert(A.CadDocument doc, BlockRefEntity br, Dictionary<BlockDef, AT.BlockRecord> records, int depth)
    {
        if (depth > 16) throw new InvalidOperationException("Blok iç içe geçme derinliği çok fazla.");
        if (!records.TryGetValue(br.Def, out var rec))
        {
            string name = br.Def.Name.TrimStart('*');
            if (name.Length == 0) name = "Blok";
            string baseName = name;
            for (int i = 2; doc.BlockRecords.TryGetValue(name, out _); i++) name = $"{baseName}_{i}";
            rec = new AT.BlockRecord(name);
            foreach (var ce in br.Def.Entities)
            {
                IEnumerable<AE.Entity> parts = ce switch
                {
                    BlockRefEntity inner => new AE.Entity[] { MakeInsert(doc, inner, records, depth + 1) },
                    DimensionEntity dm => dm.Explode().SelectMany(ToAcad),
                    _ => ToAcad(ce)
                };
                foreach (var ae in parts)
                {
                    if (doc.Layers.TryGetValue(string.IsNullOrEmpty(ce.Layer) ? "0" : ce.Layer, out var cl)) ae.Layer = cl;
                    ae.Color = ce.Color is { } c ? ToAcadColor(c) : A.Color.ByLayer;
                    rec.Entities.Add(ae);
                }
            }
            doc.BlockRecords.Add(rec);
            records[br.Def] = rec;
        }
        var m = br.M;
        double sx = Math.Sqrt(m.A * m.A + m.C * m.C);
        if (sx < 1e-12) sx = 1;
        double sy = m.Determinant / sx;
        return new AE.Insert(rec)
        {
            InsertPoint = new XYZ(m.E, m.F, 0),
            XScale = sx,
            YScale = sy,
            ZScale = 1,
            Rotation = GeoUtil.NormalizeAngle(Math.Atan2(m.C, m.A))
        };
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
                    foreach (var v in pl.VertexView)
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
