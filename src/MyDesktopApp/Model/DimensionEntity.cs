using System.Globalization;
using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

public enum DimKind
{
    /// <summary>Yatay/dikey (döndürülmüş) doğrusal ölçü.</summary>
    Linear,
    /// <summary>İki noktaya paralel ölçü.</summary>
    Aligned,
    Radius,
    Diameter,
    Angular
}

/// <summary>Ölçünün çizilecek parçaları.</summary>
public sealed class DimGeometry
{
    public List<(Vec2 A, Vec2 B)> Lines { get; } = new();
    public List<ArcPrim> Arcs { get; } = new();
    /// <summary>Dolu ok üçgenleri.</summary>
    public List<Vec2[]> Arrows { get; } = new();
    /// <summary>Yazı: alt-orta konum, açı (radyan), yükseklik, metin.</summary>
    public List<(Vec2 Anchor, double Rotation, double Height, string Text)> Texts { get; } = new();
}

/// <summary>Ölçülendirme nesnesi.</summary>
public sealed class DimensionEntity : Entity
{
    public DimKind Kind { get; set; }
    /// <summary>Doğrusal/paralel: 1. ölçü noktası. Yarıçap/çap: yay üzerindeki nokta. Açısal: 1. doğru üzerindeki nokta.</summary>
    public Vec2 P1 { get; set; }
    /// <summary>Doğrusal/paralel: 2. ölçü noktası. Açısal: 2. doğru üzerindeki nokta.</summary>
    public Vec2 P2 { get; set; }
    /// <summary>Yarıçap/çap: merkez. Açısal: köşe (kesişim) noktası.</summary>
    public Vec2 Center { get; set; }
    /// <summary>Ölçü çizgisi / yazının konumu.</summary>
    public Vec2 Location { get; set; }
    /// <summary>Doğrusal ölçüde ölçü çizgisi doğrultusu (radyan): 0 yatay, π/2 dikey.</summary>
    public double Rotation { get; set; }

    public double TextHeight { get; set; } = 2.5;
    public double ArrowSize { get; set; } = 2.5;
    public int Decimals { get; set; } = 2;
    /// <summary>Boş/null: ölçülen değer. "&lt;&gt;" ölçülen değerle değiştirilir.</summary>
    public string? TextOverride { get; set; }

    public override string TypeName => Kind switch
    {
        DimKind.Linear => "Ölçü (doğrusal)",
        DimKind.Aligned => "Ölçü (paralel)",
        DimKind.Radius => "Ölçü (yarıçap)",
        DimKind.Diameter => "Ölçü (çap)",
        _ => "Ölçü (açı)"
    };

    /// <summary>Ölçülen değer (açıda derece).</summary>
    public double Measurement
    {
        get
        {
            switch (Kind)
            {
                case DimKind.Linear:
                    return Math.Abs(Vec2.Dot(P2 - P1, Vec2.Polar(1, Rotation)));
                case DimKind.Aligned:
                    return Vec2.Distance(P1, P2);
                case DimKind.Radius:
                    return Vec2.Distance(Center, P1);
                case DimKind.Diameter:
                    return 2 * Vec2.Distance(Center, P1);
                default:
                    AngularArc(out _, out double s, out double e);
                    return GeoUtil.RadToDeg(GeoUtil.Sweep(s, e));
            }
        }
    }

    public string FormatValue(double v)
    {
        string fmt = Decimals <= 0 ? "0" : "0." + new string('#', Math.Min(8, Decimals));
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }

    public string DisplayText
    {
        get
        {
            // Uzunluk ölçüleri gösterim biriminde (inç modunda inç); dosyaya ölçü nesnesi olarak yazıldığı için etkilenmez
            string val = Kind == DimKind.Angular ? FormatValue(Measurement) : Units.FormatLength(Measurement, Decimals);
            val = Kind switch
            {
                DimKind.Radius => "R" + val,
                DimKind.Diameter => "Ø" + val,
                DimKind.Angular => val + "°",
                _ => val
            };
            if (string.IsNullOrEmpty(TextOverride)) return val;
            return TextOverride.Replace("<>", val);
        }
    }

    private void AngularArc(out double radius, out double start, out double end) => AngularArc(out radius, out start, out end, out _);

    /// <param name="swapped">Yay P2'den başlıyorsa true.</param>
    private void AngularArc(out double radius, out double start, out double end, out bool swapped)
    {
        radius = Math.Max(1e-9, Vec2.Distance(Location, Center));
        double a1 = (P1 - Center).Angle;
        double a2 = (P2 - Center).Angle;
        double aL = (Location - Center).Angle;
        if (GeoUtil.AngleInArc(aL, a1, a2)) { start = a1; end = a2; swapped = false; }
        else { start = a2; end = a1; swapped = true; }
    }

    /// <summary>Ölçünün çizim parçalarını üretir.</summary>
    public DimGeometry Build()
    {
        var g = new DimGeometry();
        double h = TextHeight, ar = ArrowSize;
        double gap = h * 0.4, extExt = h * 0.5, extOff = h * 0.25;

        switch (Kind)
        {
            case DimKind.Linear:
            case DimKind.Aligned:
                {
                    var d = Kind == DimKind.Linear ? Vec2.Polar(1, Rotation) : (P2 - P1).Normalized();
                    if (d.LengthSquared < 1e-20) d = Vec2.UnitX;
                    // Ölçü çizgisi Location'dan geçer, d doğrultusunda
                    var a = Location + d * Vec2.Dot(P1 - Location, d);
                    var b = Location + d * Vec2.Dot(P2 - Location, d);
                    AddExtLine(g, P1, a, extOff, extExt);
                    AddExtLine(g, P2, b, extOff, extExt);
                    g.Lines.Add((a, b));
                    double len = Vec2.Distance(a, b);
                    var u = len > 1e-12 ? (b - a) / len : d;
                    bool outside = len < ar * 2.5;
                    if (outside)
                    {
                        AddArrow(g, a, -u, ar);
                        AddArrow(g, b, u, ar);
                        g.Lines.Add((a - u * ar * 2, a));
                        g.Lines.Add((b, b + u * ar * 2));
                    }
                    else
                    {
                        AddArrow(g, a, u, ar);
                        AddArrow(g, b, -u, ar);
                    }
                    AddAlignedText(g, (a + b) / 2, u, gap, h);
                    break;
                }
            case DimKind.Radius:
            case DimKind.Diameter:
                {
                    double r = Vec2.Distance(Center, P1);
                    var dir = (Location - Center).Normalized();
                    if (dir.LengthSquared < 1e-20) dir = (P1 - Center).Normalized();
                    if (dir.LengthSquared < 1e-20) dir = Vec2.UnitX;
                    var q = Center + dir * r;
                    double dl = Vec2.Distance(Location, Center);
                    bool outside = dl > r;
                    if (Kind == DimKind.Radius)
                    {
                        if (outside)
                        {
                            g.Lines.Add((q, Location));
                            AddArrow(g, q, -dir, ar);
                        }
                        else
                        {
                            g.Lines.Add((Center, q));
                            AddArrow(g, q, -dir, ar);
                        }
                    }
                    else
                    {
                        var q2 = Center - dir * r;
                        g.Lines.Add((q2, q));
                        AddArrow(g, q, -dir, ar);
                        AddArrow(g, q2, dir, ar);
                        if (outside) g.Lines.Add((q, Location));
                    }
                    // Yazı: konumda yatay (okunur)
                    var anchor = outside ? Location + new Vec2(0, gap) : Location + new Vec2(0, gap);
                    g.Texts.Add((anchor, 0, h, DisplayText));
                    break;
                }
            default:
                {
                    AngularArc(out double r, out double s, out double e, out bool swapped);
                    var arc = new ArcPrim(Center, r, s, e);
                    g.Arcs.Add(arc);
                    var ps = Center + Vec2.Polar(r, s);
                    var pe = Center + Vec2.Polar(r, e);
                    // Uzatma çizgileri (ölçü yayı doğrunun ötesindeyse)
                    if (Vec2.Distance(swapped ? P2 : P1, Center) < r) AddExtLine(g, swapped ? P2 : P1, ps, extOff, extExt);
                    if (Vec2.Distance(swapped ? P1 : P2, Center) < r) AddExtLine(g, swapped ? P1 : P2, pe, extOff, extExt);
                    // Oklar teğet yönde
                    var ts = Vec2.Polar(1, s + Math.PI / 2);
                    var te = Vec2.Polar(1, e + Math.PI / 2);
                    AddArrow(g, ps, ts, Math.Min(ar, r * GeoUtil.Sweep(s, e) / 3));
                    AddArrow(g, pe, -te, Math.Min(ar, r * GeoUtil.Sweep(s, e) / 3));
                    double mid = s + GeoUtil.Sweep(s, e) / 2;
                    var mp = Center + Vec2.Polar(r, mid);
                    var tangent = Vec2.Polar(1, mid - Math.PI / 2);
                    AddAlignedText(g, mp, tangent, gap, h);
                    break;
                }
        }
        return g;
    }

    private static void AddExtLine(DimGeometry g, Vec2 from, Vec2 to, double off, double ext)
    {
        var v = to - from;
        double l = v.Length;
        if (l < 1e-9) return;
        var n = v / l;
        if (l <= off) return;
        g.Lines.Add((from + n * off, to + n * ext));
    }

    private static void AddArrow(DimGeometry g, Vec2 tip, Vec2 dirIntoLine, double size)
    {
        if (size <= 1e-12) return;
        var d = dirIntoLine.Normalized();
        var n = d.PerpLeft;
        var b = tip + d * size;
        g.Arrows.Add(new[] { tip, b + n * size / 6, b - n * size / 6 });
    }

    private void AddAlignedText(DimGeometry g, Vec2 mid, Vec2 dir, double gap, double h)
    {
        var td = dir;
        if (td.X < -1e-9 || (Math.Abs(td.X) <= 1e-9 && td.Y < 0)) td = -td;
        var up = td.PerpLeft;
        g.Texts.Add((mid + up * gap, td.Angle, h, DisplayText));
    }

    public override IEnumerable<Prim> Primitives()
    {
        var g = Build();
        foreach (var (a, b) in g.Lines) yield return new LinePrim(a, b);
        foreach (var arc in g.Arcs) yield return arc;
        foreach (var (anchor, rot, h, text) in g.Texts)
        {
            // Yazı kutusu
            double w = Math.Max(1, text.Length) * h * 0.7;
            var dx = Vec2.Polar(w / 2, rot);
            var dy = Vec2.Polar(h, rot + Math.PI / 2);
            var p0 = anchor - dx;
            var p1 = anchor + dx;
            yield return new LinePrim(p0, p1);
            yield return new LinePrim(p1, p1 + dy);
            yield return new LinePrim(p1 + dy, p0 + dy);
            yield return new LinePrim(p0 + dy, p0);
        }
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        yield return new(Location, SnapKind.Insertion);
    }

    public override void Transform(Mat2D m)
    {
        var dir = m.ApplyVector(Vec2.Polar(1, Rotation));
        P1 = m.Apply(P1);
        P2 = m.Apply(P2);
        Center = m.Apply(Center);
        Location = m.Apply(Location);
        Rotation = dir.Angle;
        double s = m.UniformScale;
        TextHeight *= s;
        ArrowSize *= s;
        if (m.IsMirroring && Kind == DimKind.Angular)
        {
            (P1, P2) = (P2, P1);
        }
    }

    /// <summary>Ölçüyü çizgi, yay ve yazılara dönüştürür (patlatma / dışa aktarma).</summary>
    public IEnumerable<Entity> Explode()
    {
        var g = Build();
        foreach (var (a, b) in g.Lines) yield return new LineEntity(a, b) { Layer = Layer, Color = Color, GroupId = GroupId };
        foreach (var arc in g.Arcs) yield return new ArcEntity(arc.Center, arc.Radius, arc.Start, arc.End) { Layer = Layer, Color = Color, GroupId = GroupId };
        foreach (var tri in g.Arrows)
            yield return new HatchEntity { Layer = Layer, Color = Color, GroupId = GroupId, Pattern = "SOLID", Loops = { tri.Select(p => new PolyVertex(p)).ToList() } };
        foreach (var (anchor, rot, h, text) in g.Texts)
        {
            double w = Math.Max(1, text.Length) * h * 0.7;
            yield return new TextEntity(anchor - Vec2.Polar(w / 2, rot), h, rot, text) { Layer = Layer, Color = Color, GroupId = GroupId };
        }
    }
}
