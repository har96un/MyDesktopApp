using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

/// <summary>Tarama (hatch): kapalı çevrelerle sınırlı dolgu veya çizgi deseni. Çevreler çift-tek (even-odd) kuralıyla doldurulur.</summary>
public sealed class HatchEntity : Entity
{
    public static readonly string[] Patterns = { "SOLID", "ANSI31", "ANSI37", "LINE", "NET" };

    /// <summary>Kapalı çevreler (bulge destekli).</summary>
    public List<List<PolyVertex>> Loops { get; private set; } = new();
    public string Pattern { get; set; } = "ANSI31";
    /// <summary>Desen ölçeği (1 = 3,175 birim aralık).</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Ek desen açısı (radyan).</summary>
    public double Angle { get; set; }

    public const double BaseSpacing = 3.175;

    public bool IsSolid => string.Equals(Pattern, "SOLID", StringComparison.OrdinalIgnoreCase);

    public override string TypeName => "Tarama";

    public override Entity Clone()
    {
        var c = (HatchEntity)MemberwiseClone();
        c.Loops = Loops.Select(l => new List<PolyVertex>(l)).ToList();
        // Önbellekler aynı geometriye ait olduğu için paylaşılabilir (değişmez listeler)
        return c;
    }

    // ---------------------------------------------------------------- Önbellek
    // Desen çizgileri ve çevre çokgenleri her ekran çiziminde ve her tıklamada yeniden
    // hesaplanıyordu (büyük çizimlerde kare başına yüzlerce ms). Geometri değişince yenilenir.
    private int _version;
    private (int Ver, int Loops, int Verts, string Pat, double Scale, double Angle, int Max) _patKey;
    private List<(Vec2 A, Vec2 B)>? _patCache;
    private (int Ver, int Loops, int Verts) _polyKey;
    private List<List<Vec2>>? _polyCache;

    private int VertexCount() { int n = 0; foreach (var l in Loops) n += l.Count; return n; }

    /// <summary>Çevreler dışarıdan değiştirildiyse önbelleği geçersiz kılar.</summary>
    public void Invalidate() => _version++;

    public static IEnumerable<Prim> LoopPrims(List<PolyVertex> loop)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            var v1 = loop[i];
            var v2 = loop[(i + 1) % loop.Count];
            if (Math.Abs(v1.Bulge) < 1e-12 || v1.P.IsClose(v2.P, 1e-12))
            {
                if (!v1.P.IsClose(v2.P, 1e-12)) yield return new LinePrim(v1.P, v2.P);
                continue;
            }
            GeoUtil.BulgeArc(v1.P, v2.P, v1.Bulge, out var c, out var r, out var sa, out var ea);
            yield return new ArcPrim(c, r, sa, ea, reversed: v1.Bulge < 0);
        }
    }

    public override IEnumerable<Prim> Primitives() => Loops.SelectMany(LoopPrims);

    /// <summary>Çevreleri çokgen olarak döndürür (yaylar parçalanmış). Sonuç önbelleklidir; değiştirmeyin.</summary>
    public List<List<Vec2>> LoopPolygons()
    {
        var key = (_version, Loops.Count, VertexCount());
        if (_polyCache != null && _polyKey == key) return _polyCache;
        var res = BuildLoopPolygons();
        _polyKey = key;
        _polyCache = res;
        return res;
    }

    private List<List<Vec2>> BuildLoopPolygons()
    {
        var res = new List<List<Vec2>>();
        foreach (var loop in Loops)
        {
            var pts = new List<Vec2>();
            foreach (var pr in LoopPrims(loop))
            {
                var t = pr.Tessellate();
                if (pts.Count > 0 && t.Count > 0 && pts[^1].IsClose(t[0], 1e-9)) t.RemoveAt(0);
                pts.AddRange(t);
            }
            if (pts.Count > 1 && pts[0].IsClose(pts[^1], 1e-9)) pts.RemoveAt(pts.Count - 1);
            if (pts.Count >= 3) res.Add(pts);
        }
        return res;
    }

    public bool Contains(Vec2 p)
    {
        int n = 0;
        foreach (var poly in LoopPolygons())
            if (GeoUtil.PointInPolygon(p, poly)) n++;
        return n % 2 == 1;
    }

    public override double Distance(Vec2 p) => Contains(p) ? 0 : base.Distance(p);

    public override IEnumerable<SnapPoint> SnapPoints() { yield break; }

    public override void Transform(Mat2D m)
    {
        bool mir = m.IsMirroring;
        _version++;
        foreach (var loop in Loops)
            for (int i = 0; i < loop.Count; i++)
                loop[i] = new PolyVertex(m.Apply(loop[i].P), mir ? -loop[i].Bulge : loop[i].Bulge);
        var d = m.ApplyVector(Vec2.UnitX);
        Angle = GeoUtil.NormalizeAngle(Angle + d.Angle);
        Scale *= m.UniformScale;
    }

    /// <summary>Desen çizgi aileleri: (açı, aralık).</summary>
    public IEnumerable<(double Angle, double Spacing)> Families()
    {
        double sp = BaseSpacing * Math.Max(1e-6, Scale);
        switch (Pattern.ToUpperInvariant())
        {
            case "ANSI31":
                yield return (Angle + Math.PI / 4, sp);
                break;
            case "ANSI37":
                yield return (Angle + Math.PI / 4, sp);
                yield return (Angle + 3 * Math.PI / 4, sp);
                break;
            case "LINE":
                yield return (Angle, sp);
                break;
            case "NET":
                yield return (Angle, sp);
                yield return (Angle + Math.PI / 2, sp);
                break;
        }
    }

    /// <summary>Desen çizgilerini çevrelere kırpılmış doğru parçaları olarak üretir (önbellekli; değiştirmeyin).</summary>
    public List<(Vec2 A, Vec2 B)> PatternSegments(int maxLines = 3000)
    {
        var key = (_version, Loops.Count, VertexCount(), Pattern, Scale, Angle, maxLines);
        if (_patCache != null && _patKey == key) return _patCache;
        var res = BuildPatternSegments(maxLines);
        _patKey = key;
        _patCache = res;
        return res;
    }

    private List<(Vec2 A, Vec2 B)> BuildPatternSegments(int maxLines)
    {
        var result = new List<(Vec2, Vec2)>();
        if (IsSolid) return result;
        var polys = LoopPolygons();
        if (polys.Count == 0) return result;
        var box = BBox.Empty;
        foreach (var poly in polys) foreach (var p in poly) box.Add(p);
        var center = box.Center;
        double diag = Math.Sqrt(box.Width * box.Width + box.Height * box.Height) / 2 + 1e-9;

        foreach (var (ang, sp0) in Families())
        {
            double sp = sp0;
            if (2 * diag / sp > maxLines) sp = 2 * diag / maxLines;
            var d = Vec2.Polar(1, ang);
            var n = d.PerpLeft;
            // Desen dünya orijinine bağlı olsun (taşımada kaymasın diye merkez yerine 0'a göre)
            double c0 = Vec2.Dot(center, n);
            int k0 = (int)Math.Floor((c0 - diag) / sp), k1 = (int)Math.Ceiling((c0 + diag) / sp);
            for (int k = k0; k <= k1; k++)
            {
                double off = k * sp;
                var basePt = n * off + d * Vec2.Dot(center, d);
                var a = basePt - d * diag * 1.5;
                var b = basePt + d * diag * 1.5;
                var ts = new List<double>();
                foreach (var poly in polys)
                    for (int i = 0; i < poly.Count; i++)
                    {
                        var p = poly[i];
                        var q = poly[(i + 1) % poly.Count];
                        // Kenar doğrunun iki tarafında mı?
                        double sp1 = Vec2.Dot(p, n) - off, sq = Vec2.Dot(q, n) - off;
                        if ((sp1 > 0) == (sq > 0)) continue;
                        double t = sp1 / (sp1 - sq);
                        var x = p + (q - p) * t;
                        ts.Add(Vec2.Dot(x - a, d));
                    }
                ts.Sort();
                for (int i = 0; i + 1 < ts.Count; i += 2)
                    result.Add((a + d * ts[i], a + d * ts[i + 1]));
            }
        }
        return result;
    }
}
