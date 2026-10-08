using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>Blok komutları ve 9 noktalı referans (sınır kutusu) taşıma.</summary>
public sealed partial class CadEditor
{
    private string _lastBlock = "";

    /// <summary>Son kullanılan 9 nokta referansı (fx, fy ∈ {0, 0.5, 1}).</summary>
    public (double Fx, double Fy) LastBoxRef { get; private set; } = (0, 0);

    private void RegisterBlockCommands()
    {
        Reg("BLOCK", "Seçili nesnelerden blok oluşturur", CmdBlock, "B", "BLOK", "BLOKYAP");
        Reg("INSERT", "Blok ekler", () => CmdInsert(null), "I", "BLOKEKLE", "EKLE");
        Reg("BOXREF", "Sınır kutusunun seçilen noktasını (9 nokta) 0,0'a taşır", CmdBoxRef, "BR", "REF");
    }

    // ================================================================ 9 noktalı referans

    public static string BoxRefName(double fx, double fy)
    {
        string v = fy == 0 ? "Alt" : fy == 1 ? "Üst" : "Orta";
        string h = fx == 0 ? "Sol" : fx == 1 ? "Sağ" : "Orta";
        if (fx == 0.5 && fy == 0.5) return "Merkez";
        if (fx == 0.5) return v + " orta";
        if (fy == 0.5) return h + " orta";
        return h + " " + v.ToLowerInvariant();
    }

    /// <summary>
    /// Profili dikdörtgen gibi düşünür: sınır kutusunun (fx, fy) noktasını 0,0'a taşır.
    /// Seçim varsa seçimi (gruplarıyla), yoksa tüm görünür nesneleri taşır.
    /// </summary>
    public Task MoveBoxPointToOrigin(double fx, double fy) =>
        RunAdHoc("REFERANS → 0,0", () => { ApplyBoxRef(fx, fy); return Task.CompletedTask; });

    private void ApplyBoxRef(double fx, double fy)
    {
        var items = Doc.Selection.Count > 0
            ? Doc.ExpandGroups(Doc.Selection).ToList()
            : Doc.VisibleEntities.ToList();
        if (items.Count == 0) { Log("  Çizimde nesne yok."); return; }
        var b = Doc.Extents(items);
        if (b.IsEmpty) return;
        var p = new Vec2(b.MinX + fx * (b.MaxX - b.MinX), b.MinY + fy * (b.MaxY - b.MinY));
        LastBoxRef = (fx, fy);
        ApplyTransform(items, Mat2D.Translation(-p));
        Log($"  {BoxRefName(fx, fy)} ({L(p)}) → 0,0   ·   {items.Count} nesne, {L(b.MaxX - b.MinX)} × {L(b.MaxY - b.MinY)}");
        ZoomExtents();
        StateChanged?.Invoke();
    }

    private static readonly string[] BoxKeywords = { "SolAlt", "AltOrta", "SagAlt", "SolOrta", "Merkez", "SagOrta", "SolUst", "UstOrta", "SagUst" };

    private async Task CmdBoxRef()
    {
        var r = await GetInput(new PointRequest
        {
            Prompt = "0,0'a gelecek kutu noktası <SolAlt>",
            Keywords = BoxKeywords,
            AllowEnter = true
        });
        int idx = r.Type == InputType.Keyword ? Array.IndexOf(BoxKeywords, r.Text) : r.Type == InputType.Enter ? 0 : -1;
        if (idx < 0) return;
        ApplyBoxRef((idx % 3) * 0.5, (idx / 3) * 0.5);
    }

    // ================================================================ Blok oluştur

    private async Task CmdBlock()
    {
        var sel = Doc.ExpandGroups(await GetSelection("Bloğa girecek nesneleri seçin")).ToList();
        if (sel.Count == 0) return;

        int bn = 1;
        while (Doc.Blocks.ContainsKey("Blok" + bn)) bn++;
        string def = "Blok" + bn;
        string name = (await GetText($"Blok adı <{def}>")).Trim();
        if (name.Length == 0) name = def;
        foreach (var ch in "<>/\\\":;?*|=`,")
            name = name.Replace(ch, '_');

        bool redefine = false;
        if (Doc.Blocks.ContainsKey(name))
        {
            var q = await GetInput(new PointRequest
            {
                Prompt = $"\"{name}\" bloğu zaten var. Yeniden tanımlansın mı? <Hayir>",
                Keywords = new[] { "Evet", "Hayir" },
                AllowEnter = true
            });
            if (q.Type != InputType.Keyword || q.Text != "Evet") { Log("  İptal edildi."); return; }
            redefine = true;
        }

        var box = Doc.Extents(sel);
        var ll = new Vec2(box.MinX, box.MinY);
        var r = await GetInput(new PointRequest
        {
            Prompt = "Temel nokta <sol alt köşe>",
            Keywords = new[] { "SolAlt", "Merkez" },
            AllowEnter = true
        });
        Vec2 bp = r.Type switch
        {
            InputType.Point => r.Point,
            InputType.Keyword when r.Text == "Merkez" => box.Center,
            _ => ll
        };

        Doc.SaveUndo();
        BlockDef bd;
        if (redefine)
        {
            bd = Doc.Blocks[name];
            bd.Entities.Clear();
        }
        else
        {
            bd = new BlockDef(name);
            Doc.Blocks[name] = bd;
        }
        var toLocal = Mat2D.Translation(-bp);
        foreach (var e in sel)
        {
            var c = e.Clone();
            c.Transform(toLocal);
            c.GroupId = null;
            bd.Entities.Add(c);
        }
        if (redefine)
            foreach (var br in Doc.Entities.OfType<BlockRefEntity>().Where(b => ReferenceEquals(b.Def, bd)))
                br.Invalidate();

        Doc.Remove(sel);
        var bref = new BlockRefEntity(bd, Mat2D.Translation(bp)) { Layer = Doc.CurrentLayer };
        Doc.Add(bref);
        _lastBlock = name;
        NotifyDocumentChanged();
        Doc.SetSelection(new[] { bref });
        Log($"  \"{name}\" bloğu {(redefine ? "yeniden tanımlandı" : "oluşturuldu")} ({bd.Entities.Count} nesne, temel nokta {L(bp)}).");
    }

    // ================================================================ Blok ekle

    /// <summary>Şerit / menüden blok ekleme.</summary>
    public Task InsertBlock(string name) => RunAdHoc("BLOK EKLE", () => CmdInsert(name));

    private async Task CmdInsert(string? name)
    {
        if (Doc.Blocks.Count == 0) { Log("  Çizimde blok tanımı yok. Önce BLOK (B) komutuyla blok oluşturun."); return; }
        if (name == null)
        {
            Log("  Bloklar: " + string.Join(", ", Doc.Blocks.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)));
            string def = Doc.Blocks.ContainsKey(_lastBlock) ? _lastBlock : Doc.Blocks.Keys.First();
            name = (await GetText($"Blok adı <{def}>")).Trim();
            if (name.Length == 0) name = def;
        }
        if (!Doc.Blocks.TryGetValue(name, out var bd)) { Log($"  \"{name}\" adında blok yok."); return; }
        _lastBlock = bd.Name;
        Doc.ClearSelection();

        double rot = 0, scale = 1;
        bool mirror = false;
        Mat2D Local()
        {
            var m = Mat2D.Scaling(scale, Vec2.Zero);
            if (mirror) m = Mat2D.Then(m, Mat2D.Mirror(Vec2.Zero, Vec2.UnitY));
            return Mat2D.Then(m, Mat2D.Rotation(rot, Vec2.Zero));
        }
        while (true)
        {
            var r = await GetInput(new PointRequest
            {
                Prompt = $"Ekleme noktası ({GeoUtil.RadToDeg(rot):0}°, ölçek {F(scale)}{(mirror ? ", aynalı" : "")})",
                Keywords = new[] { "Dondur90", "Aynala", "Olcek" },
                Preview = c => new Entity[] { new BlockRefEntity(bd, Mat2D.Then(Local(), Mat2D.Translation(c))) }
            });
            if (r.Type == InputType.Keyword)
            {
                if (r.Text == "Dondur90") rot = GeoUtil.NormalizeAngle(rot + Math.PI / 2);
                else if (r.Text == "Aynala") mirror = !mirror;
                else
                {
                    var s = await GetInput(new PointRequest { Prompt = $"Ölçek <{F(scale)}>", AllowNumber = true, AllowEnter = true });
                    if (s.Type == InputType.Number && s.Number > 1e-9) scale = s.Number;
                }
                continue;
            }
            if (r.Type != InputType.Point) continue;
            Doc.SaveUndo();
            var bref = new BlockRefEntity(bd, Mat2D.Then(Local(), Mat2D.Translation(r.Point))) { Layer = Doc.CurrentLayer };
            Doc.Add(bref);
            NotifyDocumentChanged();
            Doc.SetSelection(new[] { bref });
            Log($"  \"{bd.Name}\" eklendi: {L(r.Point)}");
            return;
        }
    }
}
