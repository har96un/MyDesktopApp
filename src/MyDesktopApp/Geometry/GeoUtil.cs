namespace MyDesktopApp.Geometry;

public static class GeoUtil
{
    public const double TwoPi = Math.PI * 2;

    /// <summary>Açıyı [0, 2π) aralığına getirir.</summary>
    public static double NormalizeAngle(double a)
    {
        a %= TwoPi;
        if (a < 0) a += TwoPi;
        return a;
    }

    /// <summary>start'tan end'e saat yönü tersine (CCW) süpürme açısı (0, 2π].</summary>
    public static double Sweep(double start, double end)
    {
        double s = NormalizeAngle(end - start);
        return s <= 1e-12 ? TwoPi : s;
    }

    /// <summary>Açı, start'tan başlayan CCW yay aralığında mı?</summary>
    public static bool AngleInArc(double angle, double start, double end)
    {
        double sweep = Sweep(start, end);
        double d = NormalizeAngle(angle - start);
        return d <= sweep + 1e-12;
    }

    public static double DistPointSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        return (p - ClosestOnSegment(p, a, b)).Length;
    }

    public static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double l2 = ab.LengthSquared;
        if (l2 < 1e-24) return a;
        double t = Math.Clamp(Vec2.Dot(p - a, ab) / l2, 0, 1);
        return a + ab * t;
    }

    /// <summary>İki doğru parçasının kesişimi.</summary>
    public static bool SegmentIntersect(Vec2 a1, Vec2 a2, Vec2 b1, Vec2 b2, out Vec2 p, bool infinite = false)
    {
        p = default;
        var r = a2 - a1;
        var s = b2 - b1;
        double denom = Vec2.Cross(r, s);
        if (Math.Abs(denom) < 1e-15) return false;
        double t = Vec2.Cross(b1 - a1, s) / denom;
        double u = Vec2.Cross(b1 - a1, r) / denom;
        const double eps = 1e-9;
        if (!infinite && (t < -eps || t > 1 + eps || u < -eps || u > 1 + eps)) return false;
        p = a1 + r * t;
        return true;
    }

    /// <summary>Doğru parçası ile çemberin kesişimleri.</summary>
    public static IEnumerable<Vec2> SegmentCircleIntersect(Vec2 a, Vec2 b, Vec2 c, double r)
    {
        var d = b - a;
        var f = a - c;
        double A = Vec2.Dot(d, d);
        if (A < 1e-24) yield break;
        double B = 2 * Vec2.Dot(f, d);
        double C = Vec2.Dot(f, f) - r * r;
        double disc = B * B - 4 * A * C;
        if (disc < 0) yield break;
        disc = Math.Sqrt(disc);
        double t1 = (-B - disc) / (2 * A);
        double t2 = (-B + disc) / (2 * A);
        const double eps = 1e-9;
        if (t1 >= -eps && t1 <= 1 + eps) yield return a + d * t1;
        if (disc > 1e-12 && t2 >= -eps && t2 <= 1 + eps) yield return a + d * t2;
    }

    /// <summary>Üç noktadan geçen çemberin merkezi.</summary>
    public static bool CircleFrom3Points(Vec2 p1, Vec2 p2, Vec2 p3, out Vec2 center, out double radius)
    {
        center = default;
        radius = 0;
        double d = 2 * (p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y));
        if (Math.Abs(d) < 1e-14) return false;
        double s1 = p1.LengthSquared, s2 = p2.LengthSquared, s3 = p3.LengthSquared;
        double ux = (s1 * (p2.Y - p3.Y) + s2 * (p3.Y - p1.Y) + s3 * (p1.Y - p2.Y)) / d;
        double uy = (s1 * (p3.X - p2.X) + s2 * (p1.X - p3.X) + s3 * (p2.X - p1.X)) / d;
        center = new Vec2(ux, uy);
        radius = Vec2.Distance(center, p1);
        return true;
    }

    /// <summary>Bulge ile tanımlı yay parçası için merkez, yarıçap, açılar.</summary>
    public static void BulgeArc(Vec2 p1, Vec2 p2, double bulge, out Vec2 center, out double radius, out double startAngle, out double endAngle)
    {
        var d = p2 - p1;
        var mid = (p1 + p2) / 2;
        center = mid + d.PerpLeft * ((1 - bulge * bulge) / (4 * bulge));
        radius = d.Length * (1 + bulge * bulge) / (4 * Math.Abs(bulge));
        double a1 = (p1 - center).Angle;
        double a2 = (p2 - center).Angle;
        if (bulge > 0) { startAngle = a1; endAngle = a2; }
        else { startAngle = a2; endAngle = a1; }
    }

    /// <summary>Yayı nokta dizisine çevirir (start→end, CCW).</summary>
    public static List<Vec2> TessellateArc(Vec2 center, double r, double start, double end, bool includeStart = true)
    {
        double sweep = Sweep(start, end);
        int n = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 180.0)));   // ~1° adım
        var list = new List<Vec2>(n + 1);
        for (int i = includeStart ? 0 : 1; i <= n; i++)
        {
            double a = start + sweep * i / n;
            list.Add(center + Vec2.Polar(r, a));
        }
        return list;
    }

    public static bool PointInPolygon(Vec2 p, IReadOnlyList<Vec2> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Douglas–Peucker sadeleştirme: <paramref name="tol"/> sapmasını aşmadan gereksiz ara noktaları atar.
    /// İlk ve son nokta korunur.
    /// </summary>
    public static List<Vec2> Simplify(IReadOnlyList<Vec2> pts, double tol)
    {
        int n = pts.Count;
        if (n <= 2 || tol <= 0) return new List<Vec2>(pts);
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, n - 1));
        while (stack.Count > 0)
        {
            var (i0, i1) = stack.Pop();
            double best = -1;
            int bi = -1;
            for (int i = i0 + 1; i < i1; i++)
            {
                double d = DistPointSegment(pts[i], pts[i0], pts[i1]);
                if (d > best) { best = d; bi = i; }
            }
            if (bi >= 0 && best > tol)
            {
                keep[bi] = true;
                stack.Push((i0, bi));
                stack.Push((bi, i1));
            }
        }
        var res = new List<Vec2>();
        for (int i = 0; i < n; i++) if (keep[i]) res.Add(pts[i]);
        return res;
    }

    public static double DegToRad(double d) => d * Math.PI / 180.0;
    public static double RadToDeg(double r) => r * 180.0 / Math.PI;
}
