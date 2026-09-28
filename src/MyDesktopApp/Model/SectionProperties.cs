using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

/// <summary>Kapalı çevrelerden oluşan bir profil kesitinin alan özellikleri.</summary>
public sealed class SectionResult
{
    public int LoopCount { get; init; }
    public int HoleCount { get; init; }
    public double Area { get; init; }
    public double Perimeter { get; init; }
    public Vec2 Centroid { get; init; }
    /// <summary>Ağırlık merkezinden geçen X eksenine göre atalet (∫y² dA).</summary>
    public double Ix { get; init; }
    /// <summary>Ağırlık merkezinden geçen Y eksenine göre atalet (∫x² dA).</summary>
    public double Iy { get; init; }
    public double Ixy { get; init; }
    /// <summary>Asal atalet momentleri ve asal eksen açısı (radyan).</summary>
    public double I1 { get; init; }
    public double I2 { get; init; }
    public double PrincipalAngle { get; init; }
    /// <summary>Kapanmayan (açık kalan) eğri sayısı.</summary>
    public int OpenChains { get; init; }
    public bool IsValid => LoopCount > 0 && Area > 1e-12;
}

public static class SectionProperties
{
    /// <summary>
    /// Nesnelerden kapalı çevreleri çıkarır. Kapalı polyline/daireler doğrudan,
    /// çizgi/yay/açık polyline parçaları uç uca eklenerek çevre oluşturulur.
    /// </summary>
    public static List<List<Vec2>> BuildLoops(IEnumerable<Entity> entities, double tol, out int openChains)
    {
        var loops = new List<List<Vec2>>();
        var chains = new List<List<Vec2>>();

        foreach (var e in entities)
        {
            if (e is TextEntity or DimensionEntity or HatchEntity) continue;
            var pts = e.ToPoints();
            if (pts.Count < 2) continue;
            if (e.IsClosed)
            {
                if (pts[0].IsClose(pts[^1], tol)) pts.RemoveAt(pts.Count - 1);
                if (pts.Count >= 3) loops.Add(pts);
            }
            else
            {
                chains.Add(pts);
            }
        }

        // Açık parçaları birleştir
        openChains = 0;
        var used = new bool[chains.Count];
        for (int i = 0; i < chains.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var cur = new List<Vec2>(chains[i]);
            bool grown = true;
            while (grown && !cur[0].IsClose(cur[^1], tol))
            {
                grown = false;
                for (int j = 0; j < chains.Count; j++)
                {
                    if (used[j]) continue;
                    var c = chains[j];
                    if (cur[^1].IsClose(c[0], tol)) { cur.AddRange(c.Skip(1)); }
                    else if (cur[^1].IsClose(c[^1], tol)) { cur.AddRange(Enumerable.Reverse(c).Skip(1)); }
                    else if (cur[0].IsClose(c[^1], tol)) { cur.InsertRange(0, c.Take(c.Count - 1)); }
                    else if (cur[0].IsClose(c[0], tol)) { cur.InsertRange(0, Enumerable.Reverse(c).Take(c.Count - 1)); }
                    else continue;
                    used[j] = true;
                    grown = true;
                    if (cur[0].IsClose(cur[^1], tol)) break;
                }
            }
            if (cur.Count >= 4 && cur[0].IsClose(cur[^1], tol))
            {
                cur.RemoveAt(cur.Count - 1);
                loops.Add(cur);
            }
            else openChains++;
        }
        return loops;
    }

    public static SectionResult Compute(IEnumerable<Entity> entities)
    {
        var list = entities.ToList();
        var ext = BBox.Empty;
        foreach (var e in list) ext.Add(e.Bounds());
        double size = ext.IsEmpty ? 1 : Math.Max(ext.Width, ext.Height);
        double tol = Math.Max(1e-6, size * 1e-6);

        var loops = BuildLoops(list, tol, out int open);
        if (loops.Count == 0) return new SectionResult { OpenChains = open };

        // Her çevre için içerildiği çevre sayısı → çift: dolu, tek: boşluk
        var signedAreas = loops.Select(SignedArea).ToList();
        double A = 0, Sx = 0, Sy = 0, Ixx = 0, Iyy = 0, Ixy = 0, per = 0;
        int holes = 0;
        for (int i = 0; i < loops.Count; i++)
        {
            int depth = 0;
            var probe = loops[i][0];
            for (int j = 0; j < loops.Count; j++)
            {
                if (i == j) continue;
                if (Math.Abs(signedAreas[j]) <= Math.Abs(signedAreas[i])) continue;
                if (GeoUtil.PointInPolygon(probe, loops[j])) depth++;
            }
            bool hole = depth % 2 == 1;
            if (hole) holes++;
            double orient = signedAreas[i] < 0 ? -1 : 1;
            double sign = orient * (hole ? -1 : 1);

            var pts = loops[i];
            double a = 0, sx = 0, sy = 0, ixx = 0, iyy = 0, ixy = 0;
            for (int k = 0; k < pts.Count; k++)
            {
                var p = pts[k];
                var q = pts[(k + 1) % pts.Count];
                double cr = p.X * q.Y - q.X * p.Y;
                a += cr;
                sx += (p.X + q.X) * cr;
                sy += (p.Y + q.Y) * cr;
                ixx += (p.Y * p.Y + p.Y * q.Y + q.Y * q.Y) * cr;
                iyy += (p.X * p.X + p.X * q.X + q.X * q.X) * cr;
                ixy += (p.X * q.Y + 2 * p.X * p.Y + 2 * q.X * q.Y + q.X * p.Y) * cr;
                per += Vec2.Distance(p, q);
            }
            A += sign * a / 2;
            Sx += sign * sx / 6;       // ∫x dA
            Sy += sign * sy / 6;       // ∫y dA
            Ixx += sign * ixx / 12;    // ∫y² dA
            Iyy += sign * iyy / 12;    // ∫x² dA
            Ixy += sign * ixy / 24;    // ∫xy dA
        }

        if (Math.Abs(A) < 1e-15)
            return new SectionResult { LoopCount = loops.Count, HoleCount = holes, OpenChains = open, Perimeter = per };

        var c = new Vec2(Sx / A, Sy / A);
        double ix = Ixx - A * c.Y * c.Y;
        double iy = Iyy - A * c.X * c.X;
        double ixyC = Ixy - A * c.X * c.Y;
        double avg = (ix + iy) / 2;
        double rad = Math.Sqrt(Math.Pow((ix - iy) / 2, 2) + ixyC * ixyC);
        double theta = 0.5 * Math.Atan2(-2 * ixyC, ix - iy);

        return new SectionResult
        {
            LoopCount = loops.Count,
            HoleCount = holes,
            OpenChains = open,
            Area = A,
            Perimeter = per,
            Centroid = c,
            Ix = ix,
            Iy = iy,
            Ixy = ixyC,
            I1 = avg + rad,
            I2 = avg - rad,
            PrincipalAngle = theta
        };
    }

    private static double SignedArea(List<Vec2> pts)
    {
        double a = 0;
        for (int k = 0; k < pts.Count; k++)
        {
            var p = pts[k];
            var q = pts[(k + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }
}
