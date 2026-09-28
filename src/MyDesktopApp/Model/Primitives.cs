using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

/// <summary>Varlıkların geometrik temel parçası: doğru parçası veya yay.</summary>
public abstract class Prim
{
    public abstract double Distance(Vec2 p);
    public abstract BBox Bounds();
    public abstract List<Vec2> Tessellate();
    public abstract Vec2 StartPoint { get; }
    public abstract Vec2 EndPoint { get; }

    public static IEnumerable<Vec2> Intersect(Prim a, Prim b)
    {
        switch (a)
        {
            case LinePrim la when b is LinePrim lb:
                if (GeoUtil.SegmentIntersect(la.A, la.B, lb.A, lb.B, out var p)) yield return p;
                break;
            case LinePrim la2 when b is ArcPrim ab:
                foreach (var q in GeoUtil.SegmentCircleIntersect(la2.A, la2.B, ab.Center, ab.Radius))
                    if (ab.ContainsAngle((q - ab.Center).Angle)) yield return q;
                break;
            case ArcPrim aa when b is LinePrim lb2:
                foreach (var q in Intersect(lb2, aa)) yield return q;
                break;
            case ArcPrim a1 when b is ArcPrim a2:
                foreach (var q in CircleCircle(a1.Center, a1.Radius, a2.Center, a2.Radius))
                    if (a1.ContainsAngle((q - a1.Center).Angle) && a2.ContainsAngle((q - a2.Center).Angle))
                        yield return q;
                break;
        }
    }

    private static IEnumerable<Vec2> CircleCircle(Vec2 c1, double r1, Vec2 c2, double r2)
    {
        double d = Vec2.Distance(c1, c2);
        if (d < 1e-12 || d > r1 + r2 + 1e-9 || d < Math.Abs(r1 - r2) - 1e-9) yield break;
        double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        double h2 = r1 * r1 - a * a;
        double h = h2 > 0 ? Math.Sqrt(h2) : 0;
        var dir = (c2 - c1) / d;
        var mid = c1 + dir * a;
        yield return mid + dir.PerpLeft * h;
        if (h > 1e-12) yield return mid - dir.PerpLeft * h;
    }
}

public sealed class LinePrim : Prim
{
    public Vec2 A { get; }
    public Vec2 B { get; }
    public LinePrim(Vec2 a, Vec2 b) { A = a; B = b; }

    public override double Distance(Vec2 p) => GeoUtil.DistPointSegment(p, A, B);
    public override BBox Bounds() => BBox.FromPoints(A, B);
    public override List<Vec2> Tessellate() => new() { A, B };
    public override Vec2 StartPoint => A;
    public override Vec2 EndPoint => B;
}

public sealed class ArcPrim : Prim
{
    public Vec2 Center { get; }
    public double Radius { get; }
    public double Start { get; }
    public double End { get; }
    public bool IsFull { get; }
    /// <summary>Yay orijinal olarak saat yönünde (CW) mi tanımlıydı (polyline için yön bilgisi).</summary>
    public bool Reversed { get; }

    public ArcPrim(Vec2 center, double radius, double start, double end, bool isFull = false, bool reversed = false)
    {
        Center = center;
        Radius = radius;
        Start = start;
        End = end;
        IsFull = isFull;
        Reversed = reversed;
    }

    public bool ContainsAngle(double a) => IsFull || GeoUtil.AngleInArc(a, Start, End);

    public override Vec2 StartPoint
    {
        get
        {
            var s = Center + Vec2.Polar(Radius, Start);
            var e = Center + Vec2.Polar(Radius, End);
            return Reversed ? e : s;
        }
    }

    public override Vec2 EndPoint
    {
        get
        {
            var s = Center + Vec2.Polar(Radius, Start);
            var e = Center + Vec2.Polar(Radius, End);
            return Reversed ? s : e;
        }
    }

    public override double Distance(Vec2 p)
    {
        var v = p - Center;
        if (ContainsAngle(v.Angle)) return Math.Abs(v.Length - Radius);
        var s = Center + Vec2.Polar(Radius, Start);
        var e = Center + Vec2.Polar(Radius, End);
        return Math.Min(Vec2.Distance(p, s), Vec2.Distance(p, e));
    }

    public override BBox Bounds()
    {
        var b = BBox.Empty;
        if (IsFull)
        {
            b.Add(Center - new Vec2(Radius, Radius));
            b.Add(Center + new Vec2(Radius, Radius));
            return b;
        }
        b.Add(Center + Vec2.Polar(Radius, Start));
        b.Add(Center + Vec2.Polar(Radius, End));
        for (int q = 0; q < 4; q++)
        {
            double a = q * Math.PI / 2;
            if (ContainsAngle(a)) b.Add(Center + Vec2.Polar(Radius, a));
        }
        return b;
    }

    /// <summary>Yön bilgisi korunarak (Reversed ise end→start) nokta dizisi.</summary>
    public override List<Vec2> Tessellate()
    {
        var pts = IsFull
            ? GeoUtil.TessellateArc(Center, Radius, 0, GeoUtil.TwoPi)
            : GeoUtil.TessellateArc(Center, Radius, Start, End);
        if (Reversed) pts.Reverse();
        return pts;
    }
}
