using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>Bulge'lı tek bir yol parçası (doğru veya yay).</summary>
internal readonly record struct PSeg(Vec2 A, Vec2 B, double Bulge)
{
    public bool IsArc => Math.Abs(Bulge) > 1e-12 && !A.IsClose(B, 1e-12);
    /// <summary>İşaretli merkez açısı (CCW +).</summary>
    public double Theta => 4 * Math.Atan(Bulge);

    public Vec2 Center
    {
        get
        {
            GeoUtil.BulgeArc(A, B, Bulge, out var c, out _, out _, out _);
            return c;
        }
    }

    public Vec2 PointAt(double t)
    {
        if (!IsArc) return Vec2.Lerp(A, B, t);
        var c = Center;
        return c + (A - c).Rotate(Theta * t);
    }

    /// <summary>Noktanın parça üzerindeki parametresi (doğru: sınırsız, yay: [0, 2π/|θ|) ).</summary>
    public double ParamOf(Vec2 p)
    {
        if (!IsArc)
        {
            var d = B - A;
            double l2 = d.LengthSquared;
            return l2 < 1e-24 ? 0 : Vec2.Dot(p - A, d) / l2;
        }
        var c = Center;
        var v1 = A - c;
        var v = p - c;
        double a = Math.Atan2(Vec2.Cross(v1, v), Vec2.Dot(v1, v));
        double th = Theta;
        if (th > 0 && a < -1e-9) a += GeoUtil.TwoPi;
        else if (th < 0 && a > 1e-9) a -= GeoUtil.TwoPi;
        return a / th;
    }

    /// <summary>En yakın noktanın [0,1] içindeki parametresi.</summary>
    public double ClosestParam(Vec2 p)
    {
        double t = ParamOf(p);
        if (t >= 0 && t <= 1) return t;
        return Vec2.Distance(p, A) <= Vec2.Distance(p, B) ? 0 : 1;
    }

    public PSeg Sub(double t1, double t2) =>
        new(PointAt(t1), PointAt(t2), IsArc ? Math.Tan(Theta * (t2 - t1) / 4) : 0);

    public Prim ToPrim()
    {
        if (!IsArc) return new LinePrim(A, B);
        GeoUtil.BulgeArc(A, B, Bulge, out var c, out var r, out var sa, out var ea);
        return new ArcPrim(c, r, sa, ea, reversed: Bulge < 0);
    }
}

/// <summary>Nesnelerin ortak yol (parça dizisi) gösterimi; budama / uzatma için.</summary>
internal sealed class CurvePath
{
    public List<PSeg> Segs { get; } = new();
    public bool Closed { get; init; }
    public int N => Segs.Count;

    public static CurvePath? From(Entity e)
    {
        switch (e)
        {
            case LineEntity l:
                if (l.Start.IsClose(l.End, 1e-12)) return null;
                return new CurvePath { Segs = { new PSeg(l.Start, l.End, 0) } };
            case ArcEntity a:
                return new CurvePath { Segs = { new PSeg(a.StartPoint, a.EndPoint, Math.Tan(a.Sweep / 4)) } };
            case CircleEntity c:
                {
                    var p1 = c.Center + new Vec2(c.Radius, 0);
                    var p2 = c.Center - new Vec2(c.Radius, 0);
                    return new CurvePath { Closed = true, Segs = { new PSeg(p1, p2, 1), new PSeg(p2, p1, 1) } };
                }
            case PolylineEntity pl:
                {
                    if (pl.SegmentCount == 0) return null;
                    var cp = new CurvePath { Closed = pl.Closed };
                    for (int i = 0; i < pl.SegmentCount; i++)
                        cp.Segs.Add(new PSeg(pl.Vertices[i].P, pl.Vertices[(i + 1) % pl.Vertices.Count].P, pl.Vertices[i].Bulge));
                    return cp;
                }
        }
        return null;
    }

    public Vec2 PointAt(double u)
    {
        int i = Math.Clamp((int)Math.Floor(u), 0, N - 1);
        return Segs[i].PointAt(u - i);
    }

    public double ClosestParam(Vec2 p)
    {
        double best = double.MaxValue, bu = 0;
        for (int i = 0; i < N; i++)
        {
            double t = Segs[i].ClosestParam(p);
            double d = Vec2.Distance(Segs[i].PointAt(t), p);
            if (d < best) { best = d; bu = i + t; }
        }
        return bu;
    }

    /// <summary>[u1, u2] aralığındaki parçalar (u1 &lt; u2).</summary>
    public List<PSeg> Extract(double u1, double u2)
    {
        var res = new List<PSeg>();
        int i0 = Math.Max(0, (int)Math.Floor(u1));
        int i1 = Math.Min(N - 1, (int)Math.Ceiling(u2) - 1);
        for (int i = i0; i <= i1; i++)
        {
            double t1 = Math.Max(u1 - i, 0), t2 = Math.Min(u2 - i, 1);
            if (t2 - t1 > 1e-10) res.Add(Segs[i].Sub(t1, t2));
        }
        return res;
    }

    /// <summary>Kesme kenarlarıyla kesişim parametreleri (sıralı, tekil).</summary>
    public List<double> Cuts(IEnumerable<Prim> edges)
    {
        var edgeList = edges as IList<Prim> ?? edges.ToList();
        var res = new List<double>();
        for (int i = 0; i < N; i++)
        {
            var sp = Segs[i].ToPrim();
            var sb = sp.Bounds();
            foreach (var ed in edgeList)
            {
                var eb = ed.Bounds();
                if (eb.MaxX < sb.MinX - 1e-9 || eb.MinX > sb.MaxX + 1e-9 || eb.MaxY < sb.MinY - 1e-9 || eb.MinY > sb.MaxY + 1e-9) continue;
                foreach (var q in Prim.Intersect(sp, ed))
                {
                    double t = Math.Clamp(Segs[i].ParamOf(q), 0, 1);
                    res.Add(i + t);
                }
            }
        }
        res.Sort();
        var uniq = new List<double>();
        foreach (var u in res)
            if (uniq.Count == 0 || u - uniq[^1] > 1e-9) uniq.Add(u);
        if (Closed && uniq.Count > 1 && N - uniq[^1] + uniq[0] < 1e-9) uniq.RemoveAt(uniq.Count - 1);
        return uniq;
    }
}

/// <summary>Budama, uzatma, pah ve yuvarlatma komutları.</summary>
public sealed partial class CadEditor
{
    private double _filletRadius;
    private double _chamferD1 = 1, _chamferD2 = 1;

    private void RegisterModifyCommands()
    {
        Reg("TRIM", "Budar: kesme kenarları arasında kalan parçayı siler", CmdTrim, "TR", "BUDA");
        Reg("EXTEND", "Uzatır: nesneyi sınır kenarına kadar uzatır", CmdExtend, "EX", "UZAT");
        Reg("FILLET", "İki çizgiyi yayla birleştirir (yuvarlatma)", () => CmdFilletChamfer(false), "F", "YUVARLA");
        Reg("CHAMFER", "İki çizgi arasına pah kırar", () => CmdFilletChamfer(true), "CHA", "PAH");
    }

    private static bool IsCurve(Entity e) => e is LineEntity or ArcEntity or CircleEntity or PolylineEntity;

    private List<Prim> EdgePrims(IEnumerable<Entity> edges, Entity exclude) =>
        edges.Where(e => e != exclude && IsCurve(e) && Doc.IsVisible(e)).SelectMany(e => e.Primitives()).ToList();

    /// <summary>Seçimde yakalanmış noktayı değil ham imleci kullan (kesişime yakalanan tıklama belirsiz olmasın).</summary>
    private Vec2 PickPoint(Vec2 p) => Vec2.Distance(p, RawCursor) < PixelSize * 20 ? RawCursor : p;

    private void ReplaceEntity(Entity old, IEnumerable<Entity> news)
    {
        int idx = Doc.Entities.IndexOf(old);
        if (idx < 0) return;
        Doc.Entities.RemoveAt(idx);
        Doc.Selection.Remove(old);
        foreach (var n in news)
        {
            n.Layer = old.Layer;
            n.Color = old.Color;
            n.GroupId = old.GroupId;
            Doc.EnsureLayer(n.Layer);
            Doc.Entities.Insert(idx++, n);
        }
    }

    private static Entity? PiecesToEntity(Entity src, List<PSeg> segs)
    {
        if (segs.Count == 0) return null;
        switch (src)
        {
            case LineEntity:
                return new LineEntity(segs[0].A, segs[^1].B);
            case ArcEntity a:
                return new ArcEntity(a.Center, a.Radius, (segs[0].A - a.Center).Angle, (segs[^1].B - a.Center).Angle);
            case CircleEntity c:
                return new ArcEntity(c.Center, c.Radius, (segs[0].A - c.Center).Angle, (segs[^1].B - c.Center).Angle);
            default:
                var pl = new PolylineEntity();
                foreach (var s in segs) pl.Vertices.Add(new PolyVertex(s.A, s.Bulge));
                pl.Vertices.Add(new PolyVertex(segs[^1].B));
                return pl;
        }
    }

    /// <summary>Budama sonucu: kalan parçalar ve silinen parça. null = kesişim yok.</summary>
    private static (List<Entity> Keep, List<PSeg> Removed)? ComputeTrim(Entity e, Vec2 pick, List<Prim> edges)
    {
        var path = CurvePath.From(e);
        if (path == null) return null;
        var cuts = path.Cuts(edges);
        double up = path.ClosestParam(pick);
        int n = path.N;
        var keep = new List<Entity>();

        if (!path.Closed)
        {
            cuts = cuts.Where(c => c > 1e-9 && c < n - 1e-9).ToList();
            double lo = 0, hi = n;
            foreach (var c in cuts)
            {
                if (c < up) lo = c;
                else if (c > up) { hi = c; break; }
            }
            if (lo <= 0 && hi >= n) return null;
            if (lo > 0 && PiecesToEntity(e, path.Extract(0, lo)) is { } k1) keep.Add(k1);
            if (hi < n && PiecesToEntity(e, path.Extract(hi, n)) is { } k2) keep.Add(k2);
            return (keep, path.Extract(lo, hi));
        }
        else
        {
            if (cuts.Count < 2) return null;
            double lo = cuts[^1], hi = cuts[0];
            foreach (var c in cuts)
            {
                if (c < up) lo = c;
                else if (c > up) { hi = c; break; }
            }
            if (up > cuts[^1]) hi = cuts[0];
            if (up < cuts[0]) lo = cuts[^1];
            // Kalan: hi → lo (ileri yönde, sarmalı)
            var rest = hi < lo ? path.Extract(hi, lo) : path.Extract(hi, n).Concat(path.Extract(0, lo)).ToList();
            var removed = lo < hi ? path.Extract(lo, hi) : path.Extract(lo, n).Concat(path.Extract(0, hi)).ToList();
            if (PiecesToEntity(e, rest) is { } k) keep.Add(k);
            return (keep, removed);
        }
    }

    private static IEnumerable<Entity> SegsAsEntities(List<PSeg> segs)
    {
        foreach (var s in segs)
        {
            if (!s.IsArc) { yield return new LineEntity(s.A, s.B); continue; }
            var pl = new PolylineEntity();
            pl.Vertices.Add(new PolyVertex(s.A, s.Bulge));
            pl.Vertices.Add(new PolyVertex(s.B));
            yield return pl;
        }
    }

    private async Task CmdTrim()
    {
        List<Entity>? edgeSet = Doc.Selection.Count > 0 ? Doc.Selection.Where(IsCurve).ToList() : null;
        Log(edgeSet == null ? "  Kesme kenarları: tüm nesneler." : $"  Kesme kenarları: {edgeSet.Count} seçili nesne.");
        Doc.ClearSelection();

        while (true)
        {
            var edges = edgeSet ?? Doc.VisibleEntities.ToList();
            var r = await GetInput(new PointRequest
            {
                Prompt = "Budanacak parçayı tıklayın (bitirmek için Enter)",
                AllowEnter = true,
                Preview = c =>
                {
                    var e = HitTest(c);
                    if (e == null || !IsCurve(e)) return Array.Empty<Entity>();
                    var pv = ComputeTrim(e, RawCursor, EdgePrims(edges, e));
                    return pv is { } rr ? SegsAsEntities(rr.Removed) : Array.Empty<Entity>();
                }
            });
            if (r.Type != InputType.Point) return;
            var pick = PickPoint(r.Point);
            var ent = HitTest(pick);
            if (ent == null || !IsCurve(ent)) { Log("  Budanabilir nesne bulunamadı."); continue; }
            var result = ComputeTrim(ent, pick, EdgePrims(edges, ent));
            if (result == null) { Log("  Kesişim yok; nesne budanamadı."); continue; }
            Doc.SaveUndo();
            ReplaceEntity(ent, result.Value.Keep);
            if (edgeSet != null && edgeSet.Remove(ent)) edgeSet.AddRange(result.Value.Keep);
            NotifyDocumentChanged();
        }
    }

    // ================================================================ EXTEND

    /// <summary>Parçanın bir ucunu sınır kenarlarına kadar uzatır. Yeni parça ya da null.</summary>
    private static PSeg? ExtendSeg(PSeg s, bool atEnd, List<Prim> edges, double big)
    {
        const double eps = 1e-7;
        if (!s.IsArc)
        {
            var from = atEnd ? s.B : s.A;
            var dir = (atEnd ? s.B - s.A : s.A - s.B).Normalized();
            var ray = new LinePrim(from, from + dir * big);
            double best = double.MaxValue;
            foreach (var ed in edges)
                foreach (var q in Prim.Intersect(ray, ed))
                {
                    double d = Vec2.Dot(q - from, dir);
                    if (d > eps && d < best) best = d;
                }
            if (best == double.MaxValue) return null;
            var np = from + dir * best;
            return atEnd ? s with { B = np } : s with { A = np };
        }
        else
        {
            GeoUtil.BulgeArc(s.A, s.B, s.Bulge, out var c, out var r, out _, out _);
            var circle = new ArcPrim(c, r, 0, GeoUtil.TwoPi, isFull: true);
            double th = s.Theta;
            double sign = Math.Sign(th) * (atEnd ? 1 : -1);   // uzatma dönüş yönü
            var from = atEnd ? s.B : s.A;
            var v0 = from - c;
            double best = double.MaxValue;
            foreach (var ed in edges)
                foreach (var q in Prim.Intersect(circle, ed))
                {
                    var v = q - c;
                    double a = Math.Atan2(Vec2.Cross(v0, v), Vec2.Dot(v0, v)) * sign;
                    if (a < 0) a += GeoUtil.TwoPi;
                    if (a > 1e-9 && Math.Abs(th) + a < GeoUtil.TwoPi - 1e-6 && a < best) best = a;
                }
            if (best == double.MaxValue) return null;
            var np = c + v0.Rotate(best * sign);
            double nth = Math.Sign(th) * (Math.Abs(th) + best);
            double nb = Math.Tan(nth / 4);
            return atEnd ? new PSeg(s.A, np, nb) : new PSeg(np, s.B, nb);
        }
    }

    /// <summary>Uzatılmış nesneyi (kopya) döndürür.</summary>
    private Entity? ComputeExtend(Entity e, Vec2 pick, List<Prim> edges)
    {
        var path = CurvePath.From(e);
        if (path == null || path.Closed) return null;
        var ext = Doc.Extents();
        double big = (ext.IsEmpty ? 1000 : Math.Sqrt(ext.Width * ext.Width + ext.Height * ext.Height)) * 10 + 1000;
        double up = path.ClosestParam(pick);
        bool atEnd = up > path.N / 2.0;
        int si = atEnd ? path.N - 1 : 0;
        if (ExtendSeg(path.Segs[si], atEnd, edges, big) is not { } ns) return null;

        switch (e)
        {
            case LineEntity:
                return new LineEntity(ns.A, ns.B);
            case ArcEntity a:
                return new ArcEntity(a.Center, a.Radius, (ns.A - a.Center).Angle, (ns.B - a.Center).Angle);
            case PolylineEntity pl:
                {
                    var c = (PolylineEntity)pl.Clone();
                    if (atEnd)
                    {
                        c.Vertices[si] = new PolyVertex(ns.A, ns.Bulge);
                        c.Vertices[si + 1] = new PolyVertex(ns.B, c.Vertices[si + 1].Bulge);
                    }
                    else
                    {
                        c.Vertices[0] = new PolyVertex(ns.A, ns.Bulge);
                    }
                    return c;
                }
        }
        return null;
    }

    private async Task CmdExtend()
    {
        List<Entity>? edgeSet = Doc.Selection.Count > 0 ? Doc.Selection.Where(IsCurve).ToList() : null;
        Log(edgeSet == null ? "  Sınır kenarları: tüm nesneler." : $"  Sınır kenarları: {edgeSet.Count} seçili nesne.");
        Doc.ClearSelection();

        while (true)
        {
            var edges = edgeSet ?? Doc.VisibleEntities.ToList();
            var r = await GetInput(new PointRequest
            {
                Prompt = "Uzatılacak nesneyi ucuna yakın tıklayın (bitirmek için Enter)",
                AllowEnter = true,
                Preview = c =>
                {
                    var e = HitTest(c);
                    if (e == null || !IsCurve(e)) return Array.Empty<Entity>();
                    var pv = ComputeExtend(e, RawCursor, EdgePrims(edges, e));
                    return pv != null ? new[] { pv } : Array.Empty<Entity>();
                }
            });
            if (r.Type != InputType.Point) return;
            var pick = PickPoint(r.Point);
            var ent = HitTest(pick);
            if (ent == null || !IsCurve(ent)) { Log("  Uzatılabilir nesne bulunamadı."); continue; }
            var res = ComputeExtend(ent, pick, EdgePrims(edges, ent));
            if (res == null) { Log("  Nesne hiçbir sınıra ulaşmıyor."); continue; }
            Doc.SaveUndo();
            ReplaceEntity(ent, new[] { res });
            if (edgeSet != null && edgeSet.Remove(ent)) edgeSet.Add(res);
            NotifyDocumentChanged();
        }
    }

    // ================================================================ FILLET / CHAMFER

    /// <summary>
    /// prev → k → next köşesini yuvarlatır / pah kırar. k yerine konacak köşeleri döndürür.
    /// null: paralel/doğrusal ya da yarıçap (mesafe) çok büyük.
    /// </summary>
    private List<PolyVertex>? CornerVerts(Vec2 prev, Vec2 k, Vec2 next, bool chamfer, double maxIn, double maxOut, out string? error)
    {
        error = null;
        var u1 = (prev - k).Normalized();
        var u2 = (next - k).Normalized();
        if (u1.LengthSquared < 1e-12 || u2.LengthSquared < 1e-12 || Math.Abs(Vec2.Cross(u1, u2)) < 1e-9)
        {
            error = "Çizgiler paralel/doğrusal.";
            return null;
        }
        double phi = Math.Acos(Math.Clamp(Vec2.Dot(u1, u2), -1, 1));
        double t1, t2;
        if (chamfer) { t1 = _chamferD1; t2 = _chamferD2; }
        else t1 = t2 = _filletRadius / Math.Tan(phi / 2);

        double tol = 1e-9 * Math.Max(1, Math.Max(maxIn, maxOut));
        if (t1 > maxIn + tol || t2 > maxOut + tol)
        {
            error = chamfer ? "Pah mesafesi çok büyük." : "Yarıçap çok büyük.";
            return null;
        }
        if (t1 < 1e-12 && t2 < 1e-12) return new List<PolyVertex> { new(k) };
        var T1 = k + u1 * t1;
        var T2 = k + u2 * t2;
        double bulge = 0;
        if (!chamfer)
        {
            var din = -u1;
            double turn = Math.Atan2(Vec2.Cross(din, u2), Vec2.Dot(din, u2));
            bulge = Math.Tan(turn / 4);
        }
        return new List<PolyVertex> { new(T1, bulge), new(T2) };
    }

    private readonly record struct SegPick(Entity E, int Seg, Vec2 A, Vec2 B, Vec2 Pick);

    /// <summary>Çizgi ya da polyline'ın düz parçasını seçtirir. Anahtar kelime seçilirse Text dolu döner.</summary>
    private async Task<(SegPick? Pick, string? Keyword, bool Enter)> PickLineSeg(string prompt, string[] keywords)
    {
        while (true)
        {
            var r = await GetInput(new PointRequest { Prompt = prompt, Keywords = keywords, AllowEnter = true });
            if (r.Type == InputType.Enter) return (null, null, true);
            if (r.Type == InputType.Keyword) return (null, r.Text, false);
            if (r.Type != InputType.Point) continue;
            var p = PickPoint(r.Point);
            var e = HitTest(p);
            if (e is LineEntity l) return (new SegPick(l, 0, l.Start, l.End, p), null, false);
            if (e is PolylineEntity pl)
            {
                int best = -1;
                double bd = double.MaxValue;
                for (int i = 0; i < pl.SegmentCount; i++)
                {
                    double d = pl.Segment(i).Distance(p);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best >= 0 && pl.Segment(best) is LinePrim lp)
                    return (new SegPick(pl, best, lp.A, lp.B, p), null, false);
            }
            Log("  Bir çizgi ya da polyline'ın düz parçasını seçin.");
        }
    }

    private async Task CmdFilletChamfer(bool chamfer)
    {
        string name = chamfer ? "Pah" : "Yuvarlatma";
        string[] kws = chamfer ? new[] { "Mesafe", "Polyline" } : new[] { "Yaricap", "Polyline" };
        Log(chamfer ? $"  Pah mesafeleri: {F(_chamferD1)}, {F(_chamferD2)}" : $"  Yuvarlatma yarıçapı: {F(_filletRadius)}");
        Doc.ClearSelection();

        SegPick first;
        while (true)
        {
            var (pk, kw, enter) = await PickLineSeg("İlk çizgiyi seçin", kws);
            if (enter) return;
            if (kw == "Yaricap")
            {
                var d = await GetNumberOrDistance("Yuvarlatma yarıçapı", null, _filletRadius);
                if (d is { } v && v >= 0) _filletRadius = v;
                continue;
            }
            if (kw == "Mesafe")
            {
                var d1 = await GetNumberOrDistance("Birinci pah mesafesi", null, _chamferD1);
                if (d1 is not { } v1 || v1 < 0) continue;
                var d2 = await GetNumberOrDistance("İkinci pah mesafesi", null, v1);
                if (d2 is not { } v2 || v2 < 0) continue;
                _chamferD1 = v1; _chamferD2 = v2;
                continue;
            }
            if (kw == "Polyline") { await FilletPolylineAll(chamfer); return; }
            if (pk is { } p) { first = p; break; }
        }
        Doc.SetSelection(new[] { first.E });

        SegPick second;
        while (true)
        {
            var (pk, _, enter) = await PickLineSeg("İkinci çizgiyi seçin", Array.Empty<string>());
            if (enter) { Doc.ClearSelection(); return; }
            if (pk is not { } p) continue;
            if (p.E == first.E && p.Seg == first.Seg) { Log("  Farklı bir çizgi seçin."); continue; }
            second = p;
            break;
        }
        Doc.ClearSelection();

        string? err;
        if (first.E == second.E && first.E is PolylineEntity pl)
        {
            // Aynı polyline'ın komşu iki parçası
            int n = pl.SegmentCount, i = first.Seg, j = second.Seg, k;
            if (j == i + 1) k = j;
            else if (i == j + 1) k = i;
            else if (pl.Closed && ((i == 0 && j == n - 1) || (j == 0 && i == n - 1))) k = 0;
            else { Log("  Polyline'ın komşu olmayan parçaları seçildi."); return; }
            int vc = pl.Vertices.Count;
            var prev = pl.Vertices[(k - 1 + vc) % vc].P;
            var cur = pl.Vertices[k].P;
            var next = pl.Vertices[(k + 1) % vc].P;
            var verts = CornerVerts(prev, cur, next, chamfer, Vec2.Distance(prev, cur), Vec2.Distance(next, cur), out err);
            if (verts == null) { Log("  " + err); return; }
            Doc.SaveUndo();
            var c = (PolylineEntity)pl.Clone();
            c.Vertices.RemoveAt(k);
            c.Vertices.InsertRange(k, verts);
            ReplaceEntity(pl, new[] { c });
            NotifyDocumentChanged();
            Log($"  {name} uygulandı.");
            return;
        }

        if (first.E is not LineEntity l1 || second.E is not LineEntity l2)
        {
            Log("  İki ayrı çizgi ya da aynı polyline'ın komşu parçaları seçilmelidir.");
            return;
        }

        if (!GeoUtil.SegmentIntersect(l1.Start, l1.End, l2.Start, l2.End, out var X, infinite: true))
        {
            Log("  Çizgiler paralel.");
            return;
        }
        Vec2 Far(LineEntity l, Vec2 pick)
        {
            var d = (l.End - l.Start).Normalized();
            double s = Vec2.Dot(pick - X, d);
            var u = s >= 0 ? d : -d;
            return Vec2.Dot(l.Start - X, u) >= Vec2.Dot(l.End - X, u) ? l.Start : l.End;
        }
        var far1 = Far(l1, first.Pick);
        var far2 = Far(l2, second.Pick);
        // Uzak uç kesişimin yanlış tarafındaysa (tıklanan taraf çok kısa) kesişime kadar izin ver
        double m1 = Vec2.Distance(far1, X), m2 = Vec2.Distance(far2, X);
        var cv = CornerVerts(far1, X, far2, chamfer, m1, m2, out err);
        if (cv == null) { Log("  " + err); return; }

        Doc.SaveUndo();
        var newL1 = new LineEntity(far1, cv[0].P);
        var newL2 = new LineEntity(cv[^1].P, far2);
        ReplaceEntity(l1, newL1.Length > 1e-9 ? new Entity[] { newL1 } : Array.Empty<Entity>());
        ReplaceEntity(l2, newL2.Length > 1e-9 ? new Entity[] { newL2 } : Array.Empty<Entity>());
        if (cv.Count == 2)
        {
            Entity join;
            if (chamfer) join = new LineEntity(cv[0].P, cv[1].P);
            else
            {
                GeoUtil.BulgeArc(cv[0].P, cv[1].P, cv[0].Bulge, out var cc, out var rr, out var sa, out var ea);
                join = new ArcEntity(cc, rr, sa, ea);
            }
            join.Layer = l1.Layer;
            join.Color = l1.Color;
            Doc.Add(join);
        }
        NotifyDocumentChanged();
        Log($"  {name} uygulandı.");
    }

    private async Task FilletPolylineAll(bool chamfer)
    {
        var r = await GetInput(new PointRequest { Prompt = "Polyline'ı seçin", AllowEnter = true });
        if (r.Type != InputType.Point) return;
        if (HitTest(PickPoint(r.Point)) is not PolylineEntity pl) { Log("  Polyline seçilmedi."); return; }

        var v = pl.Vertices;
        int vc = v.Count;
        bool closed = pl.Closed;
        var res = new List<PolyVertex>();
        int done = 0, skipped = 0;
        for (int k = 0; k < vc; k++)
        {
            bool interior = closed || (k > 0 && k < vc - 1);
            int ip = (k - 1 + vc) % vc, inx = (k + 1) % vc;
            if (!interior || Math.Abs(v[ip].Bulge) > 1e-12 || Math.Abs(v[k].Bulge) > 1e-12)
            {
                res.Add(v[k]);
                continue;
            }
            double lin = Vec2.Distance(v[ip].P, v[k].P), lout = Vec2.Distance(v[inx].P, v[k].P);
            // Komşu köşe de işlenecekse parçanın yarısı kullanılabilir
            double maxIn = (!closed && ip == 0) ? lin : lin / 2;
            double maxOut = (!closed && inx == vc - 1) ? lout : lout / 2;
            var cv = CornerVerts(v[ip].P, v[k].P, v[inx].P, chamfer, maxIn, maxOut, out var err);
            if (cv == null)
            {
                if (err != null && !err.Contains("paralel")) skipped++;
                res.Add(v[k]);
                continue;
            }
            if (cv.Count == 2) done++;
            res.AddRange(cv);
        }
        if (done == 0) { Log(skipped > 0 ? "  Köşeler için mesafe çok büyük." : "  İşlenecek köşe bulunamadı."); return; }
        Doc.SaveUndo();
        var c = (PolylineEntity)pl.Clone();
        c.Vertices.Clear();
        c.Vertices.AddRange(res);
        ReplaceEntity(pl, new[] { c });
        NotifyDocumentChanged();
        Log($"  {done} köşe işlendi" + (skipped > 0 ? $", {skipped} köşe çok kısa olduğu için atlandı." : "."));
    }
}
