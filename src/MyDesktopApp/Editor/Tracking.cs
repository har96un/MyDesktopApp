using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>Yakalama izi için bir hizalama doğrusu (başlangıç noktası + yön).</summary>
public readonly record struct TrackLine(Vec2 Origin, Vec2 Dir, string Label);

/// <summary>İmleç bekletilerek alınmış (acquired) iz noktası.</summary>
public sealed class TrackPoint
{
    public Vec2 P { get; init; }
    /// <summary>Noktadan geçen iz yönleri (yatay, dikey, nesne doğrultusu ve diki).</summary>
    public List<(Vec2 Dir, string Label)> Dirs { get; } = new();
    /// <summary>Noktanın ait olduğu doğru parçalarının yönleri (paralel/dik izi için).</summary>
    public List<Vec2> EdgeDirs { get; } = new();
}

/// <summary>
/// Nesne yakalama izi (AutoCAD OTRACK benzeri): bir yakalama noktasının üzerinde imleç kısa süre bekletilince
/// nokta "alınır"; imleç o noktanın yatay/dikey hizasına, bağlı olduğu çizginin uzantısına veya dikine
/// yaklaştığında bu hizaya oturur. İki noktanın izlerinin kesiştiği yere de yakalanır.
/// </summary>
public sealed partial class CadEditor
{
    public bool TrackingEnabled { get; set; } = true;
    public List<TrackPoint> TrackPoints { get; } = new();
    public List<TrackLine> ActiveTrackLines { get; } = new();
    public string? TrackLabel { get; private set; }

    private const int MaxTrackPoints = 4;
    private static readonly TimeSpan AcquireDelay = TimeSpan.FromMilliseconds(450);

    private SnapPoint? _hoverSnap;
    private DateTime _hoverSince;
    private bool _hoverHandled;

    /// <summary>Canvas zamanlayıcısı tarafından düzenli çağrılır (bekletme ile nokta alma).</summary>
    public void Tick()
    {
        if (!TrackingEnabled || Mode != InputMode.Point || _hoverSnap is not { } hs || _hoverHandled) return;
        if (DateTime.UtcNow - _hoverSince < AcquireDelay) return;
        _hoverHandled = true;

        double tol = PixelSize * 3;
        int idx = TrackPoints.FindIndex(t => t.P.IsClose(hs.Point, tol));
        if (idx >= 0)
        {
            TrackPoints.RemoveAt(idx);   // Aynı noktada tekrar bekletmek izi kaldırır
        }
        else
        {
            TrackPoints.Add(BuildTrackPoint(hs.Point));
            if (TrackPoints.Count > MaxTrackPoints) TrackPoints.RemoveAt(0);
        }
        OverlayChanged?.Invoke();
    }

    public void ClearTracking()
    {
        TrackPoints.Clear();
        ActiveTrackLines.Clear();
        TrackLabel = null;
        _hoverSnap = null;
    }

    private void UpdateHover(SnapPoint? snap)
    {
        if (snap is { } s && s.Kind != SnapKind.Tracking)
        {
            if (_hoverSnap is { } h && h.Point.IsClose(s.Point, PixelSize * 2)) return;
            _hoverSnap = s;
            _hoverSince = DateTime.UtcNow;
            _hoverHandled = false;
        }
        else
        {
            _hoverSnap = null;
        }
    }

    private TrackPoint BuildTrackPoint(Vec2 p)
    {
        var tp = new TrackPoint { P = p };
        tp.Dirs.Add((Vec2.UnitX, "Yatay"));
        tp.Dirs.Add((Vec2.UnitY, "Dikey"));

        // Noktadan geçen doğru parçaları: uzantı (paralel) ve dik doğrultular
        double tol = PixelSize * 3;
        foreach (var e in Doc.VisibleEntities)
        {
            var b = Doc.BoundsOf(e);
            if (p.X < b.MinX - tol || p.X > b.MaxX + tol || p.Y < b.MinY - tol || p.Y > b.MaxY + tol) continue;
            foreach (var pr in e.Primitives())
            {
                if (pr is not LinePrim l || pr.Distance(p) > tol) continue;
                var d = (l.B - l.A).Normalized();
                if (d.LengthSquared < 1e-20) continue;
                if (Math.Abs(d.X) < 1e-9 || Math.Abs(d.Y) < 1e-9) { AddEdgeDir(tp, d); continue; }  // yatay/dikey zaten var
                AddDir(tp, d, "Uzantı");
                AddDir(tp, d.PerpLeft, "Dik");
                AddEdgeDir(tp, d);
            }
        }
        return tp;
    }

    private static void AddDir(TrackPoint tp, Vec2 d, string label)
    {
        foreach (var (dir, _) in tp.Dirs)
            if (Math.Abs(Vec2.Cross(dir, d)) < 1e-9) return;
        tp.Dirs.Add((d, label));
    }

    private static void AddEdgeDir(TrackPoint tp, Vec2 d)
    {
        foreach (var x in tp.EdgeDirs)
            if (Math.Abs(Vec2.Cross(x, d)) < 1e-9) return;
        tp.EdgeDirs.Add(d);
    }

    /// <summary>İz doğrularına göre imleci hizalar. Hizalandıysa true.</summary>
    private bool ApplyTracking(Vec2 world, out Vec2 result)
    {
        result = world;
        ActiveTrackLines.Clear();
        TrackLabel = null;
        if (!TrackingEnabled || Mode != InputMode.Point) return false;

        var lines = new List<TrackLine>();
        foreach (var tp in TrackPoints)
            foreach (var (d, label) in tp.Dirs)
                lines.Add(new TrackLine(tp.P, d, label));

        if (_request?.Base is { } bp)
        {
            // Kutupsal iz: temel noktadan yatay / dikey
            lines.Add(new TrackLine(bp, Vec2.UnitX, "Yatay"));
            lines.Add(new TrackLine(bp, Vec2.UnitY, "Dikey"));
            // Alınmış noktaların kenarlarına paralel ve dik
            foreach (var tp in TrackPoints)
                foreach (var d in tp.EdgeDirs)
                {
                    lines.Add(new TrackLine(bp, d, "Paralel"));
                    lines.Add(new TrackLine(bp, d.PerpLeft, "Dik"));
                }
        }
        if (lines.Count == 0) return false;

        double tol = PixelSize * 10;
        var near = new List<(TrackLine L, Vec2 Proj, double Dist)>();
        foreach (var l in lines)
        {
            var proj = l.Origin + l.Dir * Vec2.Dot(world - l.Origin, l.Dir);
            double dist = Vec2.Distance(world, proj);
            if (dist < tol) near.Add((l, proj, dist));
        }
        if (near.Count == 0) return false;

        // İki izin kesişimi
        (Vec2 P, TrackLine A, TrackLine B)? bestX = null;
        double bestXd = tol * 1.5;
        for (int i = 0; i < near.Count; i++)
            for (int j = i + 1; j < near.Count; j++)
            {
                var a = near[i].L;
                var b = near[j].L;
                if (Math.Abs(Vec2.Cross(a.Dir, b.Dir)) < 1e-9) continue;
                if (a.Origin.IsClose(b.Origin, 1e-9)) continue;
                if (!GeoUtil.SegmentIntersect(a.Origin, a.Origin + a.Dir, b.Origin, b.Origin + b.Dir, out var x, infinite: true)) continue;
                double d = Vec2.Distance(x, world);
                if (d < bestXd) { bestXd = d; bestX = (x, a, b); }
            }

        if (bestX is { } bx)
        {
            result = bx.P;
            ActiveTrackLines.Add(bx.A);
            ActiveTrackLines.Add(bx.B);
            TrackLabel = $"Kesişim: {bx.A.Label} ∩ {bx.B.Label}";
            return true;
        }

        var best = near.OrderBy(n => n.Dist).First();
        result = best.Proj;
        ActiveTrackLines.Add(best.L);
        double dist0 = Vec2.Distance(best.L.Origin, best.Proj);
        double ang = GeoUtil.RadToDeg((best.Proj - best.L.Origin).Angle);
        TrackLabel = $"{best.L.Label}: {Units.FormatLength(dist0)} < {Vec2.Format(ang)}°";
        return true;
    }
}
