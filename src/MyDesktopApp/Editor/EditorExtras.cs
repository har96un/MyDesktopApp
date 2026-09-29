using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>Kısa ad yönetimi ve kütüphaneden ekleme.</summary>
public sealed partial class CadEditor
{
    private readonly Dictionary<string, string> _builtinAliases;

    public string LastCommand => _lastCommand;

    /// <summary>DENETLE komutu (pencereyi ana pencere açar).</summary>
    public event Action? AuditRequested;

    /// <summary>Kullanıcı kısa adlarını uygular. Geçersiz olanları (bilinmeyen komut / yerleşik kısa adla çakışan) döndürür.</summary>
    public List<string> SetCustomAliases(IEnumerable<KeyValuePair<string, string>> map)
    {
        var bad = new List<string>();
        _aliases.Clear();
        foreach (var kv in _builtinAliases) _aliases[kv.Key] = kv.Value;
        foreach (var (alias, cmd) in map)
        {
            string a = Fold(alias.Trim());
            string c = cmd.Trim().ToUpperInvariant();
            if (a.Length == 0 || !_commands.ContainsKey(c) || _builtinAliases.ContainsKey(a)) { bad.Add(alias); continue; }
            _aliases[a] = c;
        }
        return bad;
    }

    /// <summary>Bir komutun kullanıcı tarafından eklenmiş kısa adları.</summary>
    public IEnumerable<string> CustomAliasesOf(string cmd) =>
        _aliases.Where(kv => kv.Value == cmd && !_builtinAliases.ContainsKey(kv.Key)).Select(kv => kv.Key);

    public bool IsBuiltinAlias(string alias) => _builtinAliases.ContainsKey(Fold(alias.Trim()));

    /// <summary>
    /// Nesneleri (0,0 referanslı) çizime ekler. <paramref name="at"/> verilirse doğrudan o noktaya,
    /// verilmezse imleçle yer göstertilir. Birden çok nesne tek grup olarak eklenir.
    /// </summary>
    public Task InsertEntities(string name, IReadOnlyList<Entity> source, IEnumerable<LayerInfo>? layers = null, Vec2? at = null)
    {
        void AddLayers()
        {
            if (layers == null) return;
            foreach (var l in layers)
                if (!Doc.Layers.ContainsKey(l.Name)) Doc.Layers[l.Name] = l.Clone();
        }

        void Place(Mat2D m)
        {
            Doc.SaveUndo();
            AddLayers();
            string? group = source.Count > 1 ? Doc.NewGroupName() : null;
            var added = new List<Entity>();
            foreach (var e in source)
            {
                var c = e.Clone();
                c.Transform(m);
                c.GroupId = group;
                Doc.Add(c);
                added.Add(c);
                if (c is BlockRefEntity br && !Doc.Blocks.ContainsKey(br.Name)) Doc.Blocks[br.Name] = br.Def;
            }
            NotifyDocumentChanged();
            Doc.SetSelection(added);
            Log($"  \"{name}\" eklendi ({added.Count} nesne{(group != null ? ", grup " + group : "")}).");
        }

        if (at is { } p)
        {
            return RunAdHoc("EKLE " + name, () => { Place(Mat2D.Translation(p)); return Task.CompletedTask; });
        }

        return RunAdHoc("EKLE " + name, async () =>
        {
            Doc.ClearSelection();
            double rot = 0;
            bool mirror = false;
            Mat2D Local() => mirror
                ? Mat2D.Then(Mat2D.Mirror(Vec2.Zero, Vec2.UnitY), Mat2D.Rotation(rot, Vec2.Zero))
                : Mat2D.Rotation(rot, Vec2.Zero);
            while (true)
            {
                var r = await GetInput(new PointRequest
                {
                    Prompt = $"Ekleme noktası ({GeoUtil.RadToDeg(rot):0}°)",
                    Keywords = new[] { "Dondur90", "Aynala" },
                    Preview = c => Transformed(source.ToList(), Mat2D.Then(Local(), Mat2D.Translation(c)))
                });
                if (r.Type == InputType.Keyword)
                {
                    if (r.Text == "Dondur90") rot = GeoUtil.NormalizeAngle(rot + Math.PI / 2);
                    else mirror = !mirror;
                    continue;
                }
                if (r.Type != InputType.Point) continue;
                Place(Mat2D.Then(Local(), Mat2D.Translation(r.Point)));
                return;
            }
        });
    }
}
