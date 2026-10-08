using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

public sealed record CommandInfo(string Name, string[] Aliases, string Description, Func<Task> Handler, bool Repeatable = true);

public sealed partial class CadEditor
{
    private readonly Dictionary<string, CommandInfo> _commands = new();
    private readonly Dictionary<string, string> _aliases = new();

    private void Reg(string name, string desc, Func<Task> handler, params string[] aliases)
    {
        var info = new CommandInfo(name, aliases, desc, handler);
        _commands[name] = info;
        _aliases[Fold(name)] = name;
        foreach (var a in aliases) _aliases[Fold(a)] = name;
    }

    private void RegisterCommands()
    {
        // Çizim
        Reg("LINE", "Çizgi çizer", CmdLine, "L", "CIZGI");
        Reg("PLINE", "Polyline (çoklu çizgi) çizer", CmdPline, "PL", "POLYLINE");
        Reg("RECTANG", "Dikdörtgen çizer", CmdRect, "REC", "DIKDORTGEN");
        Reg("CIRCLE", "Daire çizer (merkez, yarıçap)", CmdCircle, "C", "DAIRE");
        Reg("ARC", "Üç noktadan yay çizer", CmdArc, "A", "YAY");
        Reg("TEXT", "Tek satırlı yazı ekler (komut satırından)", CmdText, "DT", "T", "YAZI");

        // Değiştirme
        Reg("MOVE", "Taşır", CmdMove, "M", "TASI");
        Reg("COPY", "Kopyalar", CmdCopy, "CO", "CP", "KOPYALA");
        Reg("ROTATE", "Döndürür", CmdRotate, "RO", "DONDUR");
        Reg("MIRROR", "Aynalar", CmdMirror, "MI", "AYNALA");
        Reg("SCALE", "Ölçekler", CmdScale, "SC", "OLCEKLE");
        Reg("ERASE", "Siler", CmdErase, "E", "SIL", "DEL");
        Reg("EXPLODE", "Polyline'ları parçalar", CmdExplode, "X", "PATLAT");
        Reg("OFFSET", "Paralel kopya (ötele)", CmdOffset, "O", "OTELE");
        Reg("JOIN", "Uç uca çizgi/yay/polyline'ları tek polyline yapar", CmdJoin, "J", "BIRLESTIR");
        Reg("GROUP", "Seçili nesneleri gruplar", CmdGroup, "G", "GRUP", "GRUPLA");
        Reg("UNGROUP", "Grubu çözer", CmdUngroup, "UG", "GRUPCOZ");
        Reg("CHANGELAYER", "Seçili nesneleri geçerli katmana taşır", CmdToCurrentLayer, "LAYCUR", "KATMANATASI");

        // Profil / koordinat
        Reg("ORIGIN", "Seçimin referans noktasını 0,0'a taşır", CmdOrigin, "OR", "SIFIR", "00");
        Reg("ORIGINLOWERLEFT", "Sol alt köşeyi 0,0'a taşır", () => OriginPreset("SolAlt"), "OLL");
        Reg("ORIGINCENTER", "Sınır kutusu merkezini 0,0'a taşır", () => OriginPreset("Merkez"), "OBC");
        Reg("ROT90", "0,0 etrafında +90° döndürür", () => QuickTransform(Mat2D.Rotation(Math.PI / 2, Vec2.Zero), "+90°"), "R90");
        Reg("ROTM90", "0,0 etrafında -90° döndürür", () => QuickTransform(Mat2D.Rotation(-Math.PI / 2, Vec2.Zero), "-90°"), "R-90", "RM90");
        Reg("ROT180", "0,0 etrafında 180° döndürür", () => QuickTransform(Mat2D.Rotation(Math.PI, Vec2.Zero), "180°"), "R180");
        Reg("MIRRORX", "X eksenine göre aynalar (y → -y)", () => QuickTransform(Mat2D.Mirror(Vec2.Zero, Vec2.UnitX), "X ekseni aynası"), "MX");
        Reg("MIRRORY", "Y eksenine göre aynalar (x → -x)", () => QuickTransform(Mat2D.Mirror(Vec2.Zero, Vec2.UnitY), "Y ekseni aynası"), "MY");
        Reg("ALIGN", "İki nokta: 1. nokta 0,0'a, 2. nokta +X yönüne", CmdAlign, "AL", "HIZALA");

        // Sorgu / görünüm
        Reg("DIST", "İki nokta arası mesafe", CmdDist, "DI", "OLC");
        Reg("ID", "Nokta koordinatı", CmdId);
        Reg("LIST", "Seçili nesne bilgileri", CmdList, "LI", "LS");
        Reg("ZOOMEXTENTS", "Tümünü göster", () => { ZoomExtents(); return Task.CompletedTask; }, "ZE", "Z");
        Reg("UNDO", "Geri al", () => { DoUndo(); return Task.CompletedTask; }, "U", "GERIAL");
        Reg("REDO", "Yinele", () => { DoRedo(); return Task.CompletedTask; }, "YINELE");
        Reg("SELECTALL", "Tümünü seç", () => { Doc.SetSelection(Doc.VisibleEntities); return Task.CompletedTask; }, "TUMUNUSEC");
        Reg("HELP", "Komut listesi", CmdHelp, "YARDIM", "?");
        RegisterDimCommands();
        RegisterModifyCommands();
        RegisterBlockCommands();
        RegisterTextCommands();
        Reg("AUDIT", "Çizimi denetler: şüpheli nesneleri (çok uzak, çok büyük, sıfır boylu, kopya) bulur", () => { AuditRequested?.Invoke(); return Task.CompletedTask; }, "DENETLE");
    }

    // ================================================================ Yardımcılar

    public void DoUndo()
    {
        if (Doc.Undo()) Log("Geri alındı."); else Log("Geri alınacak işlem yok.");
        StateChanged?.Invoke();
    }

    public void DoRedo()
    {
        if (Doc.Redo()) Log("Yinelendi."); else Log("Yinelenecek işlem yok.");
        StateChanged?.Invoke();
    }

    private void AddEntity(Entity e, bool saveUndo = true)
    {
        if (saveUndo) Doc.SaveUndo();
        e.Layer = Doc.CurrentLayer;
        Doc.Add(e);
        NotifyDocumentChanged();
    }

    private static IEnumerable<Entity> Transformed(IEnumerable<Entity> items, Mat2D m)
    {
        foreach (var e in items.Take(3000))
        {
            var c = e.Clone();
            c.Transform(m);
            yield return c;
        }
    }

    private void ApplyTransform(IEnumerable<Entity> items, Mat2D m)
    {
        Doc.SaveUndo();
        foreach (var e in items) e.Transform(m);
        NotifyDocumentChanged();
    }

    private static string F(double v) => Vec2.Format(v);
    private static string F(Vec2 v) => Vec2.Format(v);
    /// <summary>Uzunluk / nokta: geçerli gösterim biriminde (mm veya inç).</summary>
    private static string L(double v) => Units.FormatLength(v);
    private static string L(Vec2 v) => Units.FormatPoint(v);

    private async Task<double?> GetNumberOrDistance(string prompt, Vec2? basePt, double? def = null)
    {
        while (true)
        {
            var r = await GetInput(new PointRequest
            {
                Prompt = prompt + (def is { } d ? $" <{L(d)}>" : ""),
                Base = basePt,
                AllowNumber = true,
                NumberIsLength = true,
                AllowEnter = def != null
            });
            switch (r.Type)
            {
                case InputType.Number: return r.Number;
                case InputType.Enter: return def;
                case InputType.Point:
                    if (basePt is { } b) return Vec2.Distance(b, r.Point);
                    var p1 = r.Point;
                    var p2 = await GetPoint("İkinci nokta", p1, c => new Entity[] { new LineEntity(p1, c) });
                    if (p2 is { } q) return Vec2.Distance(p1, q);
                    return null;
            }
        }
    }

    // ================================================================ Çizim komutları

    private async Task CmdLine()
    {
        var p1 = await GetPoint("İlk nokta");
        if (p1 is not { } start) return;
        var prev = start;
        var first = start;
        int count = 0;
        bool any = false;
        while (true)
        {
            var from = prev;
            var r = await GetInput(new PointRequest
            {
                Prompt = "Sonraki nokta",
                Base = from,
                Preview = c => new Entity[] { new LineEntity(from, c) },
                Keywords = count >= 2 ? new[] { "Kapat", "GeriAl" } : new[] { "GeriAl" },
                AllowEnter = true
            });
            if (r.Type == InputType.Enter) break;
            if (r.Type == InputType.Keyword && r.Text == "Kapat")
            {
                var l = new LineEntity(prev, first) { Layer = Doc.CurrentLayer };
                if (!any) Doc.SaveUndo();
                Doc.Add(l);
                any = true;
                NotifyDocumentChanged();
                break;
            }
            if (r.Type == InputType.Keyword && r.Text == "GeriAl")
            {
                var last = Doc.Entities.LastOrDefault() as LineEntity;
                if (count > 0 && last != null)
                {
                    Doc.Entities.Remove(last);
                    prev = last.Start;
                    count--;
                    NotifyDocumentChanged();
                }
                continue;
            }
            if (r.Type != InputType.Point) continue;
            var line = new LineEntity(prev, r.Point) { Layer = Doc.CurrentLayer };
            if (!any) Doc.SaveUndo();
            Doc.Add(line);
            any = true;
            count++;
            prev = r.Point;
            NotifyDocumentChanged();
        }
    }

    private async Task CmdPline()
    {
        var p1 = await GetPoint("Başlangıç noktası");
        if (p1 is not { } start) return;
        var pts = new List<Vec2> { start };
        bool closed = false;
        while (true)
        {
            var last = pts[^1];
            var snapshot = pts.ToList();
            var r = await GetInput(new PointRequest
            {
                Prompt = "Sonraki nokta",
                Base = last,
                Preview = c => new Entity[] { new PolylineEntity(snapshot.Append(c), false) },
                Keywords = pts.Count >= 3 ? new[] { "Kapat", "GeriAl" } : new[] { "GeriAl" },
                AllowEnter = true
            });
            if (r.Type == InputType.Enter) break;
            if (r.Type == InputType.Keyword && r.Text == "Kapat") { closed = true; break; }
            if (r.Type == InputType.Keyword && r.Text == "GeriAl") { if (pts.Count > 1) pts.RemoveAt(pts.Count - 1); continue; }
            if (r.Type == InputType.Point) pts.Add(r.Point);
        }
        if (pts.Count < 2) return;
        AddEntity(new PolylineEntity(pts, closed));
        Log($"  Polyline: {pts.Count} köşe{(closed ? ", kapalı" : "")}.");
    }

    private static PolylineEntity RectFrom(Vec2 a, Vec2 b) =>
        new(new[] { a, new Vec2(b.X, a.Y), b, new Vec2(a.X, b.Y) }, true);

    private async Task CmdRect()
    {
        var p1 = await GetPoint("İlk köşe");
        if (p1 is not { } a) return;
        var r = await GetInput(new PointRequest
        {
            Prompt = "Karşı köşe (veya @genişlik,yükseklik)",
            Base = a,
            Preview = c => new Entity[] { RectFrom(a, c) }
        });
        if (r.Type != InputType.Point) return;
        AddEntity(RectFrom(a, r.Point));
        Log($"  Dikdörtgen: {L(Math.Abs(r.Point.X - a.X))} × {L(Math.Abs(r.Point.Y - a.Y))}");
    }

    private async Task CmdCircle()
    {
        var pc = await GetPoint("Merkez noktası");
        if (pc is not { } c) return;
        while (true)
        {
            var r = await GetInput(new PointRequest
            {
                Prompt = "Yarıçap (veya nokta)",
                Base = c,
                AllowNumber = true,
                NumberIsLength = true,
                Keywords = new[] { "Cap" },
                Preview = p => new Entity[] { new CircleEntity(c, Math.Max(1e-9, Vec2.Distance(c, p))) }
            });
            double rad;
            if (r.Type == InputType.Number) rad = r.Number;
            else if (r.Type == InputType.Point) rad = Vec2.Distance(c, r.Point);
            else if (r.Type == InputType.Keyword)
            {
                var d = await GetNumberOrDistance("Çap", c);
                if (d is not { } dd) return;
                rad = dd / 2;
            }
            else continue;
            if (rad <= 0) { Log("Yarıçap sıfırdan büyük olmalı."); continue; }
            AddEntity(new CircleEntity(c, rad));
            return;
        }
    }

    private async Task CmdArc()
    {
        var a = await GetPoint("Yayın başlangıç noktası");
        if (a is not { } p1) return;
        var b = await GetPoint("Yayın ikinci noktası", p1, c => new Entity[] { new LineEntity(p1, c) });
        if (b is not { } p2) return;
        var e = await GetPoint("Yayın bitiş noktası", p2, c => ArcFrom3(p1, p2, c) is { } arc ? new Entity[] { arc } : Array.Empty<Entity>());
        if (e is not { } p3) return;
        var res = ArcFrom3(p1, p2, p3);
        if (res == null) { Log("Noktalar doğrusal; yay oluşturulamadı."); return; }
        AddEntity(res);
    }

    private static ArcEntity? ArcFrom3(Vec2 p1, Vec2 p2, Vec2 p3)
    {
        if (!GeoUtil.CircleFrom3Points(p1, p2, p3, out var c, out var r)) return null;
        double a1 = (p1 - c).Angle, a3 = (p3 - c).Angle;
        bool ccw = Vec2.Cross(p2 - p1, p3 - p1) > 0;
        return ccw ? new ArcEntity(c, r, a1, a3) : new ArcEntity(c, r, a3, a1);
    }

    private async Task CmdText()
    {
        var p = await GetPoint("Yazı başlangıç noktası");
        if (p is not { } pos) return;
        var h = await GetNumberOrDistance("Yazı yüksekliği", pos, _lastTextHeight);
        if (h is not { } height || height <= 0) return;
        _lastTextHeight = height;
        var r = await GetInput(new PointRequest { Prompt = "Döndürme açısı (derece) <0>", Base = pos, AllowNumber = true, AllowEnter = true });
        double rot = r.Type switch
        {
            InputType.Number => GeoUtil.DegToRad(r.Number),
            InputType.Point => (r.Point - pos).Angle,
            _ => 0
        };
        var text = await GetText("Metin");
        if (string.IsNullOrWhiteSpace(text)) return;
        AddEntity(new TextEntity(pos, height, rot, text));
    }

    private double _lastTextHeight = 2.5;

    // ================================================================ Değiştirme komutları

    private async Task CmdMove() => await MoveOrCopy(copy: false);
    private async Task CmdCopy() => await MoveOrCopy(copy: true);

    private async Task MoveOrCopy(bool copy)
    {
        var sel = await GetSelection();
        var bp = await GetPoint("Temel nokta");
        if (bp is not { } basePt) return;
        bool first = true;
        while (true)
        {
            var p2 = await GetPoint(copy ? "İkinci nokta (bitirmek için Enter)" : "İkinci nokta", basePt,
                c => Transformed(sel, Mat2D.Translation(c - basePt)), allowEnter: copy);
            if (p2 is not { } target) return;
            var m = Mat2D.Translation(target - basePt);
            if (copy)
            {
                if (first) Doc.SaveUndo();
                first = false;
                var clones = new List<Entity>();
                foreach (var e in sel)
                {
                    var c = e.Clone();
                    c.Transform(m);
                    clones.Add(c);
                    Doc.Add(c);
                }
                Doc.AssignNewGroupIds(clones);
                NotifyDocumentChanged();
            }
            else
            {
                ApplyTransform(sel, m);
                Log($"  Taşındı: Δ = {L(target - basePt)}");
                return;
            }
        }
    }

    private async Task CmdRotate()
    {
        var sel = await GetSelection();
        var bp = await GetPoint("Temel nokta <0,0>", allowEnter: true);
        var basePt = bp ?? Vec2.Zero;
        while (true)
        {
            var r = await GetInput(new PointRequest
            {
                Prompt = "Döndürme açısı (derece, + saat yönü tersi)",
                Base = basePt,
                AllowNumber = true,
                Keywords = new[] { "Referans" },
                Preview = c => Transformed(sel, Mat2D.Rotation((c - basePt).Angle, basePt))
            });
            double ang;
            if (r.Type == InputType.Number) ang = GeoUtil.DegToRad(r.Number);
            else if (r.Type == InputType.Point) ang = (r.Point - basePt).Angle;
            else if (r.Type == InputType.Keyword)
            {
                var r1 = await GetInput(new PointRequest { Prompt = "Referans açı (derece) veya ilk nokta", AllowNumber = true });
                double refAng;
                if (r1.Type == InputType.Number) refAng = GeoUtil.DegToRad(r1.Number);
                else if (r1.Type == InputType.Point)
                {
                    var q1 = r1.Point;
                    var q2 = await GetPoint("İkinci nokta", q1, c => new Entity[] { new LineEntity(q1, c) });
                    if (q2 is not { } qq2) return;
                    refAng = (qq2 - q1).Angle;
                }
                else continue;
                var r2 = await GetInput(new PointRequest
                {
                    Prompt = "Yeni açı (derece)",
                    Base = basePt,
                    AllowNumber = true,
                    Preview = c => Transformed(sel, Mat2D.Rotation((c - basePt).Angle - refAng, basePt))
                });
                double newAng;
                if (r2.Type == InputType.Number) newAng = GeoUtil.DegToRad(r2.Number);
                else if (r2.Type == InputType.Point) newAng = (r2.Point - basePt).Angle;
                else continue;
                ang = newAng - refAng;
            }
            else continue;
            ApplyTransform(sel, Mat2D.Rotation(ang, basePt));
            Log($"  {F(GeoUtil.RadToDeg(ang))}° döndürüldü, merkez {L(basePt)}");
            return;
        }
    }

    private async Task CmdScale()
    {
        var sel = await GetSelection();
        var bp = await GetPoint("Temel nokta <0,0>", allowEnter: true);
        var basePt = bp ?? Vec2.Zero;
        while (true)
        {
            var r = await GetInput(new PointRequest
            {
                Prompt = "Ölçek katsayısı",
                Base = basePt,
                AllowNumber = true,
                Keywords = new[] { "Referans" },
                Preview = c => Transformed(sel, Mat2D.Scaling(Math.Max(1e-6, Vec2.Distance(c, basePt)), basePt))
            });
            double f;
            if (r.Type == InputType.Number) f = r.Number;
            else if (r.Type == InputType.Point) f = Vec2.Distance(r.Point, basePt);
            else if (r.Type == InputType.Keyword)
            {
                var r1 = await GetInput(new PointRequest { Prompt = "Referans uzunluk veya ilk nokta", AllowNumber = true, NumberIsLength = true });
                double refLen;
                if (r1.Type == InputType.Number) refLen = r1.Number;
                else if (r1.Type == InputType.Point)
                {
                    var q1 = r1.Point;
                    var q2 = await GetPoint("İkinci nokta", q1, c => new Entity[] { new LineEntity(q1, c) });
                    if (q2 is not { } qq2) return;
                    refLen = Vec2.Distance(q1, qq2);
                }
                else continue;
                if (refLen <= 0) { Log("Referans uzunluk sıfır olamaz."); continue; }
                var newLen = await GetNumberOrDistance("Yeni uzunluk", basePt);
                if (newLen is not { } nl) return;
                f = nl / refLen;
            }
            else continue;
            if (f <= 0) { Log("Ölçek sıfırdan büyük olmalı."); continue; }
            ApplyTransform(sel, Mat2D.Scaling(f, basePt));
            Log($"  Ölçek: {F(f)}");
            return;
        }
    }

    private async Task CmdMirror()
    {
        var sel = await GetSelection();
        var a = await GetPoint("Ayna çizgisinin ilk noktası");
        if (a is not { } p1) return;
        var b = await GetPoint("Ayna çizgisinin ikinci noktası", p1, c => Transformed(sel, Mat2D.Mirror(p1, c)).Append(new LineEntity(p1, c)));
        if (b is not { } p2) return;
        if (p1.IsClose(p2, 1e-12)) { Log("Noktalar aynı olamaz."); return; }
        var r = await GetInput(new PointRequest { Prompt = "Kaynak nesneler silinsin mi? <Hayir>", Keywords = new[] { "Evet", "Hayir" }, AllowEnter = true });
        bool erase = r.Type == InputType.Keyword && r.Text == "Evet";
        var m = Mat2D.Mirror(p1, p2);
        if (erase)
        {
            ApplyTransform(sel, m);
        }
        else
        {
            Doc.SaveUndo();
            var clones = new List<Entity>();
            foreach (var e in sel)
            {
                var c = e.Clone();
                c.Transform(m);
                clones.Add(c);
                Doc.Add(c);
            }
            Doc.AssignNewGroupIds(clones);
            NotifyDocumentChanged();
        }
    }

    private async Task CmdErase()
    {
        var sel = await GetSelection();
        Doc.SaveUndo();
        Doc.Remove(sel);
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log($"  {sel.Count} nesne silindi.");
    }

    public void EraseSelection()
    {
        if (Doc.Selection.Count == 0 || IsBusy) return;
        var sel = Doc.Selection.ToList();
        Doc.SaveUndo();
        Doc.Remove(sel);
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log($"{sel.Count} nesne silindi.");
    }

    private async Task CmdExplode()
    {
        var sel = await GetSelection();
        var polys = sel.OfType<PolylineEntity>().ToList();
        var dims = sel.OfType<DimensionEntity>().ToList();
        var refs = sel.OfType<BlockRefEntity>().ToList();
        if (polys.Count == 0 && dims.Count == 0 && refs.Count == 0) { Log("Seçimde patlatılabilecek polyline, blok veya ölçü yok."); return; }
        Doc.SaveUndo();
        int made = 0;
        foreach (var br in refs)
        {
            foreach (var ce in br.Explode()) { Doc.Add(ce); made++; }
        }
        Doc.Remove(refs);
        foreach (var dm in dims)
        {
            foreach (var pe in dm.Explode()) { Doc.Add(pe); made++; }
        }
        Doc.Remove(dims);
        foreach (var pl in polys)
        {
            foreach (var pr in pl.Primitives())
            {
                Entity ne = pr switch
                {
                    LinePrim l => new LineEntity(l.A, l.B),
                    ArcPrim a => new ArcEntity(a.Center, a.Radius, a.Start, a.End),
                    _ => throw new InvalidOperationException()
                };
                ne.Layer = pl.Layer;
                ne.Color = pl.Color;
                ne.GroupId = pl.GroupId;
                Doc.Add(ne);
                made++;
            }
        }
        Doc.Remove(polys);
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log($"  {polys.Count + dims.Count + refs.Count} nesne → {made} parça.");
    }

    private double _lastOffset = 1;

    private async Task CmdOffset()
    {
        var d = await GetNumberOrDistance("Öteleme mesafesi", null, _lastOffset);
        if (d is not { } dist || dist <= 0) return;
        _lastOffset = dist;
        while (true)
        {
            Doc.ClearSelection();
            var r = await GetInput(new PointRequest { Prompt = "Ötelenecek nesneyi seçin (bitirmek için Enter)", AllowEnter = true });
            if (r.Type != InputType.Point) return;
            var ent = HitTest(r.Point);
            if (ent == null) { Log("Nesne bulunamadı."); continue; }
            Doc.SetSelection(new[] { ent });
            var side = await GetPoint("Hangi tarafa öteleneceğini gösterin");
            if (side is not { } s) return;
            var res = OffsetEntity(ent, dist, s);
            if (res == null) { Log("Bu nesne ötelenemiyor."); continue; }
            Doc.SaveUndo();
            res.Layer = ent.Layer;
            res.Color = ent.Color;
            Doc.Add(res);
            NotifyDocumentChanged();
        }
    }

    private static Entity? OffsetEntity(Entity e, double d, Vec2 side)
    {
        switch (e)
        {
            case LineEntity l:
                {
                    var n = (l.End - l.Start).Normalized().PerpLeft;
                    double sgn = Vec2.Dot(side - l.Start, n) >= 0 ? 1 : -1;
                    return new LineEntity(l.Start + n * d * sgn, l.End + n * d * sgn);
                }
            case CircleEntity c:
                {
                    double nr = Vec2.Distance(side, c.Center) > c.Radius ? c.Radius + d : c.Radius - d;
                    return nr > 0 ? new CircleEntity(c.Center, nr) : null;
                }
            case ArcEntity a:
                {
                    double nr = Vec2.Distance(side, a.Center) > a.Radius ? a.Radius + d : a.Radius - d;
                    return nr > 0 ? new ArcEntity(a.Center, nr, a.StartAngle, a.EndAngle) : null;
                }
            case PolylineEntity pl:
                return OffsetPolyline(pl, d, side);
        }
        return null;
    }

    private static PolylineEntity? OffsetPolyline(PolylineEntity pl, double d, Vec2 side)
    {
        int n = pl.SegmentCount;
        if (n == 0) return null;

        // En yakın parçaya göre taraf (sol = +)
        int nearest = 0;
        double best = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double dd = pl.Segment(i).Distance(side);
            if (dd < best) { best = dd; nearest = i; }
        }
        double sgn = SideOf(pl.Segment(nearest), side) ? 1 : -1;
        double off = d * sgn; // sola doğru öteleme miktarı

        var segs = new List<(Vec2 a, Vec2 b, bool line)>();
        for (int i = 0; i < n; i++)
        {
            var s = pl.Segment(i);
            if (s is LinePrim l)
            {
                var nn = (l.B - l.A).Normalized().PerpLeft * off;
                segs.Add((l.A + nn, l.B + nn, true));
            }
            else if (s is ArcPrim ar)
            {
                // CCW gidişte sol = merkeze doğru
                double nr = ar.Reversed ? ar.Radius + off : ar.Radius - off;
                if (nr <= 1e-9) return null;
                var sp = ar.StartPoint;
                var ep = ar.EndPoint;
                segs.Add((ar.Center + (sp - ar.Center).Normalized() * nr, ar.Center + (ep - ar.Center).Normalized() * nr, false));
            }
        }

        var res = new PolylineEntity { Closed = pl.Closed };
        int vc = pl.Vertices.Count;
        for (int i = 0; i < vc; i++)
        {
            double bulge = pl.Vertices[i].Bulge;
            Vec2 pt;
            int segIn = i - 1;           // köşeye gelen parça
            int segOut = i;              // köşeden çıkan parça
            if (!pl.Closed)
            {
                if (i == 0) { res.Vertices.Add(new PolyVertex(segs[0].a, bulge)); continue; }
                if (i == vc - 1) { res.Vertices.Add(new PolyVertex(segs[n - 1].b, bulge)); continue; }
            }
            else
            {
                if (segIn < 0) segIn = n - 1;
            }
            var sIn = segs[segIn];
            var sOut = segs[segOut % n];
            if (sIn.line && sOut.line && GeoUtil.SegmentIntersect(sIn.a, sIn.b, sOut.a, sOut.b, out var x, infinite: true))
                pt = x;
            else
                pt = sOut.a;
            res.Vertices.Add(new PolyVertex(pt, bulge));
        }
        return res;
    }

    private static bool SideOf(Prim p, Vec2 pt)
    {
        if (p is LinePrim l) return Vec2.Cross(l.B - l.A, pt - l.A) >= 0;
        if (p is ArcPrim a)
        {
            bool inside = Vec2.Distance(pt, a.Center) < a.Radius;
            // CCW (Reversed=false) gidişte sol taraf = içerisi
            return a.Reversed ? !inside : inside;
        }
        return true;
    }

    private async Task CmdJoin()
    {
        var sel = await GetSelection();
        var parts = sel.Where(e => e is LineEntity or ArcEntity or PolylineEntity { Closed: false }).ToList();
        if (parts.Count < 2) { Log("Birleştirmek için en az iki açık nesne seçin."); return; }

        var ext = Doc.Extents(parts);
        double tol = Math.Max(1e-6, Math.Max(ext.Width, ext.Height) * 1e-7);

        // Her parçayı (köşeler + bulge) dizisine çevir
        var chains = new List<List<PolyVertex>>();
        foreach (var e in parts)
        {
            switch (e)
            {
                case LineEntity l:
                    chains.Add(new() { new PolyVertex(l.Start), new PolyVertex(l.End) });
                    break;
                case ArcEntity a:
                    chains.Add(new() { new PolyVertex(a.StartPoint, Math.Tan(a.Sweep / 4)), new PolyVertex(a.EndPoint) });
                    break;
                case PolylineEntity p:
                    chains.Add(new List<PolyVertex>(p.Vertices));
                    break;
            }
        }

        static List<PolyVertex> Reverse(List<PolyVertex> c)
        {
            var r = new List<PolyVertex>();
            for (int i = c.Count - 1; i >= 0; i--)
            {
                double bulge = i > 0 ? -c[i - 1].Bulge : 0;
                r.Add(new PolyVertex(c[i].P, bulge));
            }
            return r;
        }

        var result = new List<PolylineEntity>();
        var used = new bool[chains.Count];
        for (int i = 0; i < chains.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var cur = new List<PolyVertex>(chains[i]);
            bool grown = true;
            while (grown)
            {
                grown = false;
                for (int j = 0; j < chains.Count; j++)
                {
                    if (used[j]) continue;
                    var c = chains[j];
                    List<PolyVertex>? add = null;
                    bool atEnd = true;
                    if (cur[^1].P.IsClose(c[0].P, tol)) add = c;
                    else if (cur[^1].P.IsClose(c[^1].P, tol)) add = Reverse(c);
                    else if (cur[0].P.IsClose(c[^1].P, tol)) { add = c; atEnd = false; }
                    else if (cur[0].P.IsClose(c[0].P, tol)) { add = Reverse(c); atEnd = false; }
                    if (add == null) continue;
                    used[j] = true;
                    grown = true;
                    if (atEnd)
                    {
                        cur[^1] = new PolyVertex(cur[^1].P, add[0].Bulge);
                        cur.AddRange(add.Skip(1));
                    }
                    else
                    {
                        var head = add.Take(add.Count - 1).ToList();
                        head.Add(new PolyVertex(add[^1].P, cur[0].Bulge));
                        cur.RemoveAt(0);
                        head.AddRange(cur);
                        cur = head;
                    }
                }
            }
            var pl = new PolylineEntity();
            bool closed = cur.Count > 2 && cur[0].P.IsClose(cur[^1].P, tol);
            if (closed) cur.RemoveAt(cur.Count - 1);
            pl.Vertices.AddRange(cur);
            pl.Closed = closed;
            result.Add(pl);
        }

        Doc.SaveUndo();
        var first = parts[0];
        Doc.Remove(parts);
        foreach (var pl in result)
        {
            pl.Layer = first.Layer;
            pl.Color = first.Color;
            pl.GroupId = first.GroupId;
            Doc.Add(pl);
        }
        Doc.SetSelection(result);
        NotifyDocumentChanged();
        Log($"  {parts.Count} nesne → {result.Count} polyline ({result.Count(p => p.Closed)} kapalı).");
    }

    // ================================================================ Grup / katman komutları

    private async Task CmdGroup()
    {
        var sel = await GetSelection();
        if (sel.Count < 2) { Log("Grup için en az iki nesne seçin."); return; }
        Doc.SaveUndo();
        string name = Doc.NewGroupName();
        foreach (var e in sel) e.GroupId = name;
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log($"  {sel.Count} nesne \"{name}\" olarak gruplandı. (Ctrl+tık: gruptan tek nesne seçer)");
    }

    private async Task CmdUngroup()
    {
        var sel = await GetSelection();
        var groups = sel.Where(e => e.GroupId != null).Select(e => e.GroupId!).Distinct().ToList();
        if (groups.Count == 0) { Log("Seçimde grup yok."); return; }
        Doc.SaveUndo();
        foreach (var e in Doc.Entities)
            if (e.GroupId != null && groups.Contains(e.GroupId)) e.GroupId = null;
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log($"  Çözülen grup: {string.Join(", ", groups)}");
    }

    private async Task CmdToCurrentLayer()
    {
        var sel = await GetSelection();
        MoveToLayer(sel, Doc.CurrentLayer);
    }

    public void MoveToLayer(IReadOnlyCollection<Entity> items, string layer)
    {
        if (items.Count == 0) return;
        Doc.SaveUndo();
        Doc.EnsureLayer(layer);
        foreach (var e in items) e.Layer = layer;
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log($"  {items.Count} nesne \"{layer}\" katmanına taşındı.");
    }

    // ================================================================ Profil komutları

    private Vec2? ReferencePoint(List<Entity> sel, string kw)
    {
        var b = Doc.Extents(sel);
        if (b.IsEmpty) return null;
        switch (kw)
        {
            case "SolAlt": return new Vec2(b.MinX, b.MinY);
            case "SagAlt": return new Vec2(b.MaxX, b.MinY);
            case "SolUst": return new Vec2(b.MinX, b.MaxY);
            case "SagUst": return new Vec2(b.MaxX, b.MaxY);
            case "Merkez": return b.Center;
        }
        return null;
    }

    private static readonly string[] OriginKeywords = { "SolAlt", "SagAlt", "SolUst", "SagUst", "Merkez" };

    private async Task CmdOrigin()
    {
        var sel = await GetSelection();
        var r = await GetInput(new PointRequest
        {
            Prompt = "0,0'a gelecek nokta (tıklayın) veya seçenek <SolAlt>",
            Keywords = OriginKeywords,
            AllowEnter = true,
            Preview = c => Transformed(sel, Mat2D.Translation(-c))
        });
        Vec2? refPt = r.Type switch
        {
            InputType.Point => r.Point,
            InputType.Keyword => ReferencePoint(sel, r.Text),
            InputType.Enter => ReferencePoint(sel, "SolAlt"),
            _ => null
        };
        if (refPt is not { } p) return;
        MoveToOrigin(sel, p);
    }

    private void MoveToOrigin(List<Entity> sel, Vec2 p)
    {
        ApplyTransform(sel, Mat2D.Translation(-p));
        Log($"  {L(p)} noktası 0,0'a taşındı.");
        ZoomExtents();
    }

    private async Task OriginPreset(string kw)
    {
        var sel = await GetSelection();
        var p = ReferencePoint(sel, kw);
        if (p != null) MoveToOrigin(sel, p.Value);
    }

    private async Task QuickTransform(Mat2D m, string label)
    {
        var sel = await GetSelection();
        ApplyTransform(sel, m);
        Log($"  {label} uygulandı ({sel.Count} nesne).");
    }

    private async Task CmdAlign()
    {
        var sel = await GetSelection();
        var a = await GetPoint("Yeni orijin (0,0) olacak nokta");
        if (a is not { } p1) return;
        var b = await GetPoint("+X ekseni yönündeki nokta", p1, c =>
        {
            var m = Mat2D.Then(Mat2D.Translation(-p1), Mat2D.Rotation(-(c - p1).Angle, Vec2.Zero));
            return Transformed(sel, m).Append(new LineEntity(p1, c));
        });
        if (b is not { } p2) return;
        if (p1.IsClose(p2, 1e-12)) { Log("Noktalar aynı olamaz."); return; }
        double ang = (p2 - p1).Angle;
        var mm = Mat2D.Then(Mat2D.Translation(-p1), Mat2D.Rotation(-ang, Vec2.Zero));
        ApplyTransform(sel, mm);
        Log($"  Hizalandı: {L(p1)} → 0,0; {F(GeoUtil.RadToDeg(-ang))}° döndürüldü.");
        ZoomExtents();
    }

    // ================================================================ Sorgu

    private async Task CmdDist()
    {
        var a = await GetPoint("İlk nokta");
        if (a is not { } p1) return;
        var b = await GetPoint("İkinci nokta", p1, c => new Entity[] { new LineEntity(p1, c) });
        if (b is not { } p2) return;
        var d = p2 - p1;
        Log($"  Mesafe = {L(d.Length)},  ΔX = {L(d.X)},  ΔY = {L(d.Y)},  Açı = {F(GeoUtil.RadToDeg(d.Angle))}°");
    }

    private async Task CmdId()
    {
        var a = await GetPoint("Nokta");
        if (a is { } p) Log($"  X = {L(p.X)}   Y = {L(p.Y)}");
    }

    private async Task CmdList()
    {
        var sel = await GetSelection();
        foreach (var e in sel.Take(50))
        {
            string s = e switch
            {
                LineEntity l => $"Çizgi: {L(l.Start)} → {L(l.End)}, uzunluk {L(l.Length)}",
                CircleEntity c => $"Daire: merkez {L(c.Center)}, R {L(c.Radius)}",
                ArcEntity a => $"Yay: merkez {L(a.Center)}, R {L(a.Radius)}, {F(GeoUtil.RadToDeg(a.StartAngle))}° → {F(GeoUtil.RadToDeg(a.EndAngle))}°",
                PolylineEntity p => $"Polyline: {p.VertexView.Count} köşe, {(p.Closed ? "kapalı" : "açık")}",
                TextEntity t => $"Yazı: \"{t.Value}\" @ {L(t.Position)}, h {L(t.Height)}",
                BlockRefEntity br => $"Blok: \"{br.Name}\" @ {L(br.InsertPoint)}, {F(GeoUtil.RadToDeg(br.RotationRad))}°, ölçek {F(br.ScaleFactor)}{(br.Mirrored ? ", aynalı" : "")}",
                _ => e.TypeName
            };
            Log($"  [{e.Layer}] {s}");
        }
        if (sel.Count > 50) Log($"  ... ve {sel.Count - 50} nesne daha");
    }

    private Task CmdHelp()
    {
        Log("Komutlar (kısayollar):");
        foreach (var c in _commands.Values)
            Log($"  {c.Name,-16} {string.Join(", ", c.Aliases),-18} {c.Description}");
        Log("Koordinat girişi: x,y  (mutlak) | @dx,dy (göreli) | @uzunluk<açı (kutupsal) | sayı (imleç yönünde mesafe)");
        Log("F3: Nesne yakalama  F7: Izgara  F8: Orto  F11: Yakalama izi  Esc: İptal  Enter/Boşluk/Sağ tık: Onay/Tekrar");
        Log("Yakalama izi: bir uç/orta/merkez noktasının üzerinde imleci ~0,5 sn bekletin (yeşil +). İmleç o noktanın yatay/dikey hizasına,");
        Log("  bağlı çizginin uzantısına veya dikine gelince hizaya oturur; iki noktanın izlerinin kesişimine de yakalanır. Temel noktadan");
        Log("  alınan çizgiye paralel/dik yönler de izlenir. Aynı noktada tekrar bekletmek izi kaldırır.");
        Log("Gruplar: G ile grupla, UG ile çöz. Gruptaki bir nesneye tıklamak tüm grubu seçer; Ctrl+tık tek nesneyi seçer.");
        Log("Buda (TR) / Uzat (EX): önceden seçim yapılırsa kenar olarak seçili nesneler, yoksa tüm nesneler kullanılır; parçaya tıklayın.");
        Log("Yuvarla (F) / Pah (CHA): iki çizgi ya da aynı polyline'ın komşu iki parçası. Y/M: yarıçap/mesafe, P: polyline'ın tüm köşeleri.");
        Log("Ölçü: DLI (doğrusal), DAL (paralel), DRA (yarıçap), DDI (çap), DAN (açı). Tarama: H, kapalı alanın içine tıklayın.");
        Log("Metin: MT ile pencereden çok satırlı, hizalamalı metin ekleyin; yazıya çift tıklayınca düzenlenir (ED). DT: tek satır.");
        Log("Blok: B ile seçimden blok oluşturun, I ile ekleyin, X ile patlatın. Referans: BR → sınır kutusunun 9 noktasından birini 0,0'a taşır.");
        Log("Sağ tık (komut yokken): bağlam menüsü. Ctrl+L: profil kütüphanesi, Ctrl+P: PDF raporu. Kısa ad/kısayol: Araçlar → Ayarlar.");
        return Task.CompletedTask;
    }
}
