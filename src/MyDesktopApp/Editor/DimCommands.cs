using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>Ölçülendirme ve tarama komutları.</summary>
public sealed partial class CadEditor
{
    private void RegisterDimCommands()
    {
        Reg("DIMLINEAR", "Doğrusal (yatay/dikey) ölçü", CmdDimLinear, "DLI", "OLCU", "DL");
        Reg("DIMALIGNED", "Paralel ölçü", CmdDimAligned, "DAL", "PARALELOLCU");
        Reg("DIMRADIUS", "Yarıçap ölçüsü", () => CmdDimRadial(false), "DRA", "YARICAP");
        Reg("DIMDIAMETER", "Çap ölçüsü", () => CmdDimRadial(true), "DDI", "CAP");
        Reg("DIMANGULAR", "Açı ölçüsü", CmdDimAngular, "DAN", "ACIOLCU");
        Reg("HATCH", "Tarama (iç noktayı tıklayın)", CmdHatch, "H", "BH", "TARA");
    }

    private DimensionEntity NewDim(DimKind kind) => new()
    {
        Kind = kind,
        TextHeight = Doc.DimTextHeight,
        ArrowSize = Doc.DimArrowSize,
        Decimals = Doc.DimDecimals
    };

    /// <summary>Tıklanan noktadaki nesnenin en yakın parçasını döndürür.</summary>
    private async Task<(Entity E, Prim P, Vec2 Pick)?> PickPrim(string prompt, Func<Prim, bool> accept, bool allowEnter = false)
    {
        while (true)
        {
            var r = await GetInput(new PointRequest { Prompt = prompt, AllowEnter = allowEnter });
            if (r.Type == InputType.Enter) return null;
            if (r.Type != InputType.Point) continue;
            var e = HitTest(r.Point);
            if (e == null || e is TextEntity or DimensionEntity or HatchEntity) { Log("  Uygun nesne bulunamadı."); continue; }
            Prim? best = null;
            double bd = double.MaxValue;
            foreach (var pr in e.Primitives())
            {
                double d = pr.Distance(r.Point);
                if (d < bd) { bd = d; best = pr; }
            }
            if (best == null || !accept(best)) { Log("  Bu nesne için uygun değil."); continue; }
            return (e, best, r.Point);
        }
    }

    private void AddDim(DimensionEntity d)
    {
        Doc.SaveUndo();
        d.Layer = Doc.CurrentLayer;
        Doc.Add(d);
        NotifyDocumentChanged();
        Log($"  Ölçü: {d.DisplayText}");
    }

    private async Task CmdDimLinear()
    {
        Vec2 p1, p2;
        var a = await GetPoint("İlk ölçü noktası (nesne seçmek için Enter)", allowEnter: true);
        if (a is { } pa)
        {
            var b = await GetPoint("İkinci ölçü noktası", pa);
            if (b is not { } pb) return;
            p1 = pa;
            p2 = pb;
        }
        else
        {
            var pick = await PickPrim("Ölçülecek çizgiyi seçin", pr => pr is LinePrim);
            if (pick is not { } pk) return;
            var l = (LinePrim)pk.P;
            p1 = l.A;
            p2 = l.B;
        }

        double? locked = null;
        while (true)
        {
            var mid = (p1 + p2) / 2;
            var r = await GetInput(new PointRequest
            {
                Prompt = "Ölçü çizgisinin konumu",
                Keywords = new[] { "Yatay", "Dikey" },
                Preview = c =>
                {
                    var d = NewDim(DimKind.Linear);
                    d.P1 = p1; d.P2 = p2; d.Location = c;
                    d.Rotation = locked ?? AutoRotation(p1, p2, c);
                    return new Entity[] { d };
                }
            });
            if (r.Type == InputType.Keyword) { locked = r.Text == "Yatay" ? 0 : Math.PI / 2; continue; }
            if (r.Type != InputType.Point) continue;
            var dim = NewDim(DimKind.Linear);
            dim.P1 = p1; dim.P2 = p2; dim.Location = r.Point;
            dim.Rotation = locked ?? AutoRotation(p1, p2, r.Point);
            AddDim(dim);
            return;
        }
    }

    /// <summary>İmleç ölçü noktalarının sağına/soluna çekildiyse dikey, üstüne/altına çekildiyse yatay ölçü.</summary>
    private static double AutoRotation(Vec2 p1, Vec2 p2, Vec2 loc)
    {
        var box = BBox.FromPoints(p1, p2);
        bool outsideX = loc.X < box.MinX || loc.X > box.MaxX;
        bool outsideY = loc.Y < box.MinY || loc.Y > box.MaxY;
        if (outsideX && !outsideY) return Math.PI / 2;
        if (outsideY && !outsideX) return 0;
        var v = loc - (p1 + p2) / 2;
        return Math.Abs(v.X) > Math.Abs(v.Y) ? Math.PI / 2 : 0;
    }

    private async Task CmdDimAligned()
    {
        Vec2 p1, p2;
        var a = await GetPoint("İlk ölçü noktası (nesne seçmek için Enter)", allowEnter: true);
        if (a is { } pa)
        {
            var b = await GetPoint("İkinci ölçü noktası", pa);
            if (b is not { } pb) return;
            p1 = pa;
            p2 = pb;
        }
        else
        {
            var pick = await PickPrim("Ölçülecek çizgiyi seçin", pr => pr is LinePrim);
            if (pick is not { } pk) return;
            var l = (LinePrim)pk.P;
            p1 = l.A;
            p2 = l.B;
        }
        var loc = await GetPoint("Ölçü çizgisinin konumu", null, c =>
        {
            var d = NewDim(DimKind.Aligned);
            d.P1 = p1; d.P2 = p2; d.Location = c;
            return new Entity[] { d };
        });
        if (loc is not { } lp) return;
        var dim = NewDim(DimKind.Aligned);
        dim.P1 = p1; dim.P2 = p2; dim.Location = lp;
        AddDim(dim);
    }

    private async Task CmdDimRadial(bool diameter)
    {
        var pick = await PickPrim(diameter ? "Daire veya yay seçin" : "Yay veya daire seçin", pr => pr is ArcPrim);
        if (pick is not { } pk) return;
        var arc = (ArcPrim)pk.P;
        var onArc = arc.Center + (pk.Pick - arc.Center).Normalized() * arc.Radius;
        var kind = diameter ? DimKind.Diameter : DimKind.Radius;
        var loc = await GetPoint("Ölçü yazısının konumu", null, c =>
        {
            var d = NewDim(kind);
            d.Center = arc.Center; d.P1 = onArc; d.Location = c;
            return new Entity[] { d };
        });
        if (loc is not { } lp) return;
        var dim = NewDim(kind);
        dim.Center = arc.Center;
        dim.P1 = onArc;
        dim.Location = lp;
        AddDim(dim);
    }

    private async Task CmdDimAngular()
    {
        var k1 = await PickPrim("Birinci çizgiyi seçin", pr => pr is LinePrim);
        if (k1 is not { } a) return;
        var k2 = await PickPrim("İkinci çizgiyi seçin", pr => pr is LinePrim);
        if (k2 is not { } b) return;
        var l1 = (LinePrim)a.P;
        var l2 = (LinePrim)b.P;
        if (!GeoUtil.SegmentIntersect(l1.A, l1.B, l2.A, l2.B, out var v, infinite: true))
        {
            Log("  Çizgiler paralel; açı ölçülemez.");
            return;
        }
        // Tıklanan tarafı kullan
        var p1 = GeoUtil.ClosestOnSegment(a.Pick, l1.A, l1.B);
        var p2 = GeoUtil.ClosestOnSegment(b.Pick, l2.A, l2.B);
        if (p1.IsClose(v, 1e-9)) p1 = Vec2.Distance(l1.A, v) > Vec2.Distance(l1.B, v) ? l1.A : l1.B;
        if (p2.IsClose(v, 1e-9)) p2 = Vec2.Distance(l2.A, v) > Vec2.Distance(l2.B, v) ? l2.A : l2.B;
        var loc = await GetPoint("Ölçü yayının konumu", null, c =>
        {
            var d = NewDim(DimKind.Angular);
            d.Center = v; d.P1 = p1; d.P2 = p2; d.Location = c;
            return new Entity[] { d };
        });
        if (loc is not { } lp) return;
        var dim = NewDim(DimKind.Angular);
        dim.Center = v; dim.P1 = p1; dim.P2 = p2; dim.Location = lp;
        AddDim(dim);
    }

    // ================================================================ Tarama

    private async Task CmdHatch()
    {
        int made = 0;
        while (true)
        {
            string info = $"desen {Doc.HatchPattern}, ölçek {F(Doc.HatchScale)}, açı {F(GeoUtil.RadToDeg(Doc.HatchAngle))}°";
            var r = await GetInput(new PointRequest
            {
                Prompt = $"Taranacak alanın içine tıklayın ({info}) (bitirmek için Enter)",
                Keywords = new[] { "Desen", "Olcek", "Aci", "Nesneler" },
                AllowEnter = true
            });
            if (r.Type == InputType.Enter) break;
            if (r.Type == InputType.Keyword)
            {
                switch (r.Text)
                {
                    case "Desen":
                        {
                            var p = await GetInput(new PointRequest { Prompt = "Desen", Keywords = HatchEntity.Patterns, AllowEnter = true });
                            if (p.Type == InputType.Keyword) Doc.HatchPattern = p.Text;
                            break;
                        }
                    case "Olcek":
                        {
                            var p = await GetInput(new PointRequest { Prompt = $"Desen ölçeği <{F(Doc.HatchScale)}>", AllowNumber = true, AllowEnter = true });
                            if (p.Type == InputType.Number && p.Number > 0) Doc.HatchScale = p.Number;
                            break;
                        }
                    case "Aci":
                        {
                            var p = await GetInput(new PointRequest { Prompt = $"Desen açısı (derece) <{F(GeoUtil.RadToDeg(Doc.HatchAngle))}>", AllowNumber = true, AllowEnter = true });
                            if (p.Type == InputType.Number) Doc.HatchAngle = GeoUtil.DegToRad(p.Number);
                            break;
                        }
                    case "Nesneler":
                        {
                            Doc.ClearSelection();
                            var sel = await GetSelection("Kapalı sınır nesnelerini seçin");
                            var loops = SectionProperties.BuildLoops(sel, LoopTolerance(sel), out _);
                            if (loops.Count == 0) { Log("  Seçimde kapalı sınır yok."); break; }
                            AddHatch(loops);
                            made++;
                            Doc.ClearSelection();
                            break;
                        }
                }
                continue;
            }
            if (r.Type != InputType.Point) continue;
            var region = FindRegion(r.Point);
            if (region == null) { Log("  Tıklanan noktayı çevreleyen kapalı bir alan bulunamadı."); continue; }
            AddHatch(region);
            made++;
        }
        if (made > 0) Log($"  {made} tarama eklendi.");
    }

    private static double LoopTolerance(IEnumerable<Entity> items)
    {
        var b = BBox.Empty;
        foreach (var e in items) b.Add(e.Bounds());
        return b.IsEmpty ? 1e-6 : Math.Max(1e-6, Math.Max(b.Width, b.Height) * 1e-6);
    }

    private void AddHatch(List<List<Vec2>> loops)
    {
        var h = new HatchEntity
        {
            Pattern = Doc.HatchPattern,
            Scale = Doc.HatchScale,
            Angle = Doc.HatchAngle,
            Layer = Doc.CurrentLayer
        };
        foreach (var l in loops) h.Loops.Add(l.Select(p => new PolyVertex(p)).ToList());
        Doc.SaveUndo();
        Doc.Add(h);
        NotifyDocumentChanged();
    }

    /// <summary>Noktayı çevreleyen en küçük kapalı çevreyi ve içindeki boşlukları bulur.</summary>
    private List<List<Vec2>>? FindRegion(Vec2 p)
    {
        var cands = Doc.VisibleEntities.Where(e => e is not (TextEntity or DimensionEntity or HatchEntity)).Take(20000).ToList();
        var loops = SectionProperties.BuildLoops(cands, LoopTolerance(cands), out _);
        List<Vec2>? outer = null;
        double outerArea = double.MaxValue;
        foreach (var l in loops)
        {
            if (!GeoUtil.PointInPolygon(p, l)) continue;
            double a = Math.Abs(PolyArea(l));
            if (a < outerArea) { outerArea = a; outer = l; }
        }
        if (outer == null) return null;
        var result = new List<List<Vec2>> { outer };
        // Dıştaki çevrenin içinde kalan, noktayı içermeyen en dış boşluklar
        var holes = loops.Where(l => !ReferenceEquals(l, outer) && Math.Abs(PolyArea(l)) < outerArea &&
                                     GeoUtil.PointInPolygon(l[0], outer) && !GeoUtil.PointInPolygon(p, l)).ToList();
        foreach (var hole in holes)
        {
            bool nested = holes.Any(o => !ReferenceEquals(o, hole) && Math.Abs(PolyArea(o)) > Math.Abs(PolyArea(hole)) && GeoUtil.PointInPolygon(hole[0], o));
            if (!nested) result.Add(hole);
        }
        return result;
    }

    private static double PolyArea(List<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }
}
