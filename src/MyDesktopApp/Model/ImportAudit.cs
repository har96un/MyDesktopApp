using System.Globalization;
using System.Text;
using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

public enum AuditReason
{
    /// <summary>Çizimin ana gövdesinden çok uzakta.</summary>
    FarAway,
    /// <summary>Çizime göre aşırı büyük daire / yay.</summary>
    Oversized,
    /// <summary>Neredeyse tam daire olan yay (ters yönlü yay belirtisi).</summary>
    NearFullArc,
    /// <summary>Sıfır boylu / sıfır yarıçaplı.</summary>
    Degenerate,
    /// <summary>Aynı yerde birebir aynı nesne.</summary>
    Duplicate
}

public sealed class AuditResult
{
    public Dictionary<AuditReason, List<Entity>> Items { get; } = new();
    public BBox MainExtents { get; set; } = BBox.Empty;
    public int Total => Items.Values.Sum(l => l.Count);

    public static string Describe(AuditReason r) => r switch
    {
        AuditReason.FarAway => "Çizimin ana gövdesinden çok uzakta kalan nesneler",
        AuditReason.Oversized => "Çizime göre aşırı büyük daire / yaylar",
        AuditReason.NearFullArc => "Neredeyse tam daire olan yaylar (yönü ters çevrilmiş yay olabilir)",
        AuditReason.Degenerate => "Sıfır boylu / sıfır yarıçaplı nesneler",
        AuditReason.Duplicate => "Üst üste binen birebir kopyalar",
        _ => r.ToString()
    };
}

/// <summary>
/// Açılan çizimdeki şüpheli nesneleri bulur. Nesneleri değiştirmez; yalnızca listeler.
/// Ana gövde, nesne merkezlerinin çeyrekler arası aralığı (IQR) ile sağlam biçimde bulunur;
/// böylece birkaç uçuk nesne ölçütü bozmaz.
/// </summary>
public static class ImportAudit
{
    public static AuditResult Analyze(IReadOnlyList<Entity> entities)
    {
        var res = new AuditResult();
        var ents = entities.Where(e => e is not (TextEntity or DimensionEntity)).ToList();
        if (ents.Count == 0) return res;

        var boxes = ents.Select(e => e.Bounds()).ToList();
        var assigned = new HashSet<Entity>();
        void Flag(AuditReason r, Entity e)
        {
            if (!assigned.Add(e)) return;
            if (!res.Items.TryGetValue(r, out var l)) res.Items[r] = l = new List<Entity>();
            l.Add(e);
        }

        // --- Ana gövde (sağlam sınır kutusu)
        var main = BBox.Empty;
        if (ents.Count >= 4)
        {
            var xs = boxes.Where(b => !b.IsEmpty).Select(b => b.Center.X).OrderBy(v => v).ToList();
            var ys = boxes.Where(b => !b.IsEmpty).Select(b => b.Center.Y).OrderBy(v => v).ToList();
            double q1x = Q(xs, 0.25), q3x = Q(xs, 0.75), q1y = Q(ys, 0.25), q3y = Q(ys, 0.75);
            double ix = Math.Max(q3x - q1x, 1e-9), iy = Math.Max(q3y - q1y, 1e-9);
            double span = Math.Max(ix, iy);
            ix = Math.Max(ix, span * 0.25);
            iy = Math.Max(iy, span * 0.25);
            var core = new BBox { MinX = q1x - 3 * ix, MaxX = q3x + 3 * ix, MinY = q1y - 3 * iy, MaxY = q3y + 3 * iy, IsEmpty = false };
            for (int i = 0; i < ents.Count; i++)
            {
                var b = boxes[i];
                if (b.IsEmpty || !core.Contains(b.Center)) continue;
                // Aşırı büyük daireleri ana gövdeye katma
                if (ents[i] is CircleEntity or ArcEntity && Math.Max(b.Width, b.Height) > 3 * Math.Max(q3x - q1x + (q3y - q1y), 1e-9) && ents.Count > 10) continue;
                main.Add(b);
            }
        }
        if (main.IsEmpty) foreach (var b in boxes) main.Add(b);
        res.MainExtents = main;
        double size = Math.Max(Math.Max(main.Width, main.Height), 1e-9);
        double tol = size * 1e-7;

        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            var b = boxes[i];

            // Sıfır boylu
            bool degenerate = e switch
            {
                LineEntity l => l.Length <= tol,
                CircleEntity c => c.Radius <= tol,
                ArcEntity a => a.Radius <= tol,
                PolylineEntity p => p.VertexView.Count < 2 || (b.Width <= tol && b.Height <= tol),
                _ => false
            };
            if (degenerate) { Flag(AuditReason.Degenerate, e); continue; }

            // Neredeyse tam daire yay (> 350°)
            if (e is ArcEntity arc && GeoUtil.RadToDeg(arc.Sweep) > 350) { Flag(AuditReason.NearFullArc, e); continue; }

            // Aşırı büyük daire / yay
            if (e is CircleEntity or ArcEntity && ents.Count >= 4 && Math.Max(b.Width, b.Height) > 2 * size) { Flag(AuditReason.Oversized, e); continue; }

            // Ana gövdeden uzak
            if (ents.Count >= 4 && !b.IsEmpty)
            {
                double gap = Math.Max(Math.Max(main.MinX - b.MaxX, b.MinX - main.MaxX), Math.Max(main.MinY - b.MaxY, b.MinY - main.MaxY));
                if (gap > 3 * size) { Flag(AuditReason.FarAway, e); continue; }
            }
        }

        // Kopyalar
        var seen = new HashSet<string>();
        double q = Math.Max(size * 1e-6, 1e-9);
        foreach (var e in ents)
        {
            if (assigned.Contains(e)) continue;
            string? key = Key(e, q);
            if (key != null && !seen.Add(key)) Flag(AuditReason.Duplicate, e);
        }
        return res;
    }

    private static double Q(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double idx = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    private static string R(double v, double q) => Math.Round(v / q).ToString("0", CultureInfo.InvariantCulture);

    private static string P(Vec2 p, double q) => R(p.X, q) + "," + R(p.Y, q);

    /// <summary>Geometrik imza (katmandan bağımsız).</summary>
    private static string? Key(Entity e, double q)
    {
        switch (e)
        {
            case LineEntity l:
                {
                    string a = P(l.Start, q), b = P(l.End, q);
                    return "L" + (string.CompareOrdinal(a, b) < 0 ? a + ";" + b : b + ";" + a);
                }
            case CircleEntity c:
                return "C" + P(c.Center, q) + ";" + R(c.Radius, q);
            case ArcEntity a:
                return "A" + P(a.StartPoint, q) + ";" + P(a.EndPoint, q) + ";" + P(a.Center, q);
            case PolylineEntity p:
                {
                    var sb = new StringBuilder(p.Closed ? "PC" : "P");
                    foreach (var v in p.VertexView) sb.Append(P(v.P, q)).Append(':').Append(Math.Round(v.Bulge, 6).ToString(CultureInfo.InvariantCulture)).Append(';');
                    return sb.ToString();
                }
        }
        return null;
    }
}
