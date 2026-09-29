using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

public sealed class LayerInfo
{
    public string Name { get; set; } = "0";
    public EntColor Color { get; set; } = new(255, 255, 255, 7);
    public bool Visible { get; set; } = true;
    public LayerInfo Clone() => (LayerInfo)MemberwiseClone();
}

/// <summary>Çizim belgesi: nesneler, katmanlar, seçim ve geri al / yinele.</summary>
public sealed class CadDocument
{
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private const int MaxUndo = 100;

    private sealed record Snapshot(List<Entity> Entities, Dictionary<string, LayerInfo> Layers);

    public List<Entity> Entities { get; private set; } = new();
    public Dictionary<string, LayerInfo> Layers { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<Entity> Selection { get; } = new();
    /// <summary>Blok tanımları (ada göre).</summary>
    public Dictionary<string, BlockDef> Blocks { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? FilePath { get; set; }
    public bool IsModified { get; set; }
    public string CurrentLayer { get; set; } = "0";
    /// <summary>Son kayıt / açılış biçimi (ör. "dxf2010"); Kaydet aynı sürümü kullanır.</summary>
    public string? SaveFormatId { get; set; }

    // Ölçü / tarama varsayılanları (Ayarlar'dan gelir)
    public double DimTextHeight { get; set; } = 2.5;
    public double DimArrowSize { get; set; } = 2.5;
    public int DimDecimals { get; set; } = 2;
    public string HatchPattern { get; set; } = "ANSI31";
    public double HatchScale { get; set; } = 1;
    public double HatchAngle { get; set; }

    /// <summary>Geometri değiştiğinde tetiklenir.</summary>
    public event EventHandler? Changed;
    /// <summary>Seçim değiştiğinde tetiklenir.</summary>
    public event EventHandler? SelectionChanged;

    public CadDocument()
    {
        EnsureLayer("0");
    }

    public LayerInfo EnsureLayer(string name)
    {
        if (!Layers.TryGetValue(name, out var l))
        {
            l = new LayerInfo { Name = name };
            Layers[name] = l;
        }
        return l;
    }

    public EntColor ResolveColor(Entity e)
    {
        if (e.Color is { } c) return c;
        return Layers.TryGetValue(e.Layer, out var l) ? l.Color : new EntColor(255, 255, 255, 7);
    }

    public bool IsVisible(Entity e) => !Layers.TryGetValue(e.Layer, out var l) || l.Visible;

    public IEnumerable<Entity> VisibleEntities => Entities.Where(IsVisible);

    public void Clear()
    {
        Entities = new List<Entity>();
        Layers = new Dictionary<string, LayerInfo>(StringComparer.OrdinalIgnoreCase);
        EnsureLayer("0");
        Blocks = new Dictionary<string, BlockDef>(StringComparer.OrdinalIgnoreCase);
        CurrentLayer = "0";
        Selection.Clear();
        _undo.Clear();
        _redo.Clear();
        FilePath = null;
        IsModified = false;
        RaiseChanged();
        RaiseSelectionChanged();
    }

    /// <summary>Başka bir belgenin içeriğini bu belgeye ekler (içe aktarma).</summary>
    public void Merge(CadDocument other)
    {
        foreach (var l in other.Layers.Values)
            if (!Layers.ContainsKey(l.Name)) Layers[l.Name] = l.Clone();

        // Blok tanımları: aynı ad başka tanımsa yeniden adlandır
        foreach (var bd in other.Blocks.Values.ToList())
        {
            if (Blocks.TryGetValue(bd.Name, out var ex) && !ReferenceEquals(ex, bd))
                bd.Name = UniqueBlockName(bd.Name);
            Blocks[bd.Name] = bd;
        }

        // Çakışan grup adlarını yeniden adlandır
        var existing = new HashSet<string>(GroupNames);
        foreach (var g in other.GroupNames)
            if (g.StartsWith("Grup", StringComparison.Ordinal) && int.TryParse(g.AsSpan(4), out int gn) && gn > _lastGroupNo)
                _lastGroupNo = gn;
        var map = new Dictionary<string, string>();
        foreach (var e in other.Entities)
        {
            if (e.GroupId == null || !existing.Contains(e.GroupId)) continue;
            if (!map.TryGetValue(e.GroupId, out var nn)) map[e.GroupId] = nn = NewGroupName();
            e.GroupId = nn;
        }
        Entities.AddRange(other.Entities);
    }

    /// <summary>Değişiklikten önce çağrılır: mevcut durumu geri alma yığınına koyar.</summary>
    public void SaveUndo()
    {
        _undo.Push(TakeSnapshot());
        _redo.Clear();
        if (_undo.Count > MaxUndo)
        {
            var arr = _undo.ToArray();                 // en yeni önce
            _undo.Clear();
            for (int i = MaxUndo - 1; i >= 0; i--) _undo.Push(arr[i]);
        }
        IsModified = true;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(TakeSnapshot());
        Restore(_undo.Pop());
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(TakeSnapshot());
        Restore(_redo.Pop());
        return true;
    }

    private Snapshot TakeSnapshot() =>
        new(Entities.Select(e => e.Clone()).ToList(),
            Layers.ToDictionary(k => k.Key, v => v.Value.Clone(), StringComparer.OrdinalIgnoreCase));

    private void Restore(Snapshot s)
    {
        Entities = s.Entities;
        Layers = s.Layers;
        Selection.Clear();
        IsModified = true;
        RaiseChanged();
        RaiseSelectionChanged();
    }

    public void Add(Entity e)
    {
        if (string.IsNullOrEmpty(e.Layer)) e.Layer = CurrentLayer;
        EnsureLayer(e.Layer);
        Entities.Add(e);
    }

    public void Remove(IEnumerable<Entity> items)
    {
        var set = new HashSet<Entity>(items);
        Entities.RemoveAll(set.Contains);
        Selection.RemoveWhere(set.Contains);
    }

    public BBox Extents(IEnumerable<Entity>? items = null)
    {
        var b = BBox.Empty;
        foreach (var e in items ?? VisibleEntities) b.Add(BoundsOf(e));
        return b;
    }

    public void SetSelection(IEnumerable<Entity> items)
    {
        Selection.Clear();
        foreach (var e in items) Selection.Add(e);
        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0) return;
        Selection.Clear();
        RaiseSelectionChanged();
    }

    // ================================================================ Gruplar

    private int _lastGroupNo;

    /// <summary>Kullanılmayan yeni bir grup adı üretir (Grup1, Grup2...).</summary>
    public string NewGroupName()
    {
        int max = _lastGroupNo;
        foreach (var e in Entities)
        {
            if (e.GroupId != null && e.GroupId.StartsWith("Grup", StringComparison.Ordinal) &&
                int.TryParse(e.GroupId.AsSpan(4), out int n) && n > max)
                max = n;
        }
        _lastGroupNo = max + 1;
        return "Grup" + _lastGroupNo;
    }

    /// <summary>Verilen nesneleri, grup üyeleriyle birlikte döndürür.</summary>
    public IEnumerable<Entity> ExpandGroups(IEnumerable<Entity> items)
    {
        var list = items.ToList();
        var groups = new HashSet<string>(list.Where(e => e.GroupId != null).Select(e => e.GroupId!));
        if (groups.Count == 0) return list;
        var set = new HashSet<Entity>(list);
        foreach (var e in Entities)
            if (e.GroupId != null && groups.Contains(e.GroupId)) set.Add(e);
        return set;
    }

    public IEnumerable<string> GroupNames => Entities.Where(e => e.GroupId != null).Select(e => e.GroupId!).Distinct();

    /// <summary>Kopyalanan nesnelere (orijinalden ayrı) yeni grup adları verir.</summary>
    public void AssignNewGroupIds(IEnumerable<Entity> clones)
    {
        var map = new Dictionary<string, string>();
        foreach (var e in clones)
        {
            if (e.GroupId == null) continue;
            if (!map.TryGetValue(e.GroupId, out var nn))
            {
                nn = NewGroupName();
                map[e.GroupId] = nn;
            }
            e.GroupId = nn;
        }
    }

    /// <summary>
    /// Arka planda kaydetmek için belgenin hızlı kopyası (nesne klonları ucuzdur: polyline köşeleri
    /// yazma-anında-kopyala ile paylaşılır).
    /// </summary>
    public CadDocument CopyForSave()
    {
        var d = new CadDocument
        {
            Entities = Entities.Select(e => e.Clone()).ToList(),
            Layers = Layers.ToDictionary(k => k.Key, v => v.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            Blocks = new Dictionary<string, BlockDef>(Blocks, StringComparer.OrdinalIgnoreCase),
            FilePath = FilePath,
            CurrentLayer = CurrentLayer,
            SaveFormatId = SaveFormatId,
            DimTextHeight = DimTextHeight,
            DimArrowSize = DimArrowSize,
            DimDecimals = DimDecimals,
            HatchPattern = HatchPattern,
            HatchScale = HatchScale,
            HatchAngle = HatchAngle
        };
        return d;
    }

    // ================================================================ Bloklar

    public string UniqueBlockName(string baseName)
    {
        if (!Blocks.ContainsKey(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var n = $"{baseName}_{i}";
            if (!Blocks.ContainsKey(n)) return n;
        }
    }

    public int CountBlockRefs(string name) =>
        Entities.OfType<BlockRefEntity>().Count(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    // ================================================================ Katmanlar

    public int CountOnLayer(string name) => Entities.Count(e => string.Equals(e.Layer, name, StringComparison.OrdinalIgnoreCase));

    public bool RenameLayer(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || !Layers.TryGetValue(oldName, out var li)) return false;
        if (Layers.ContainsKey(newName) && !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return false;
        Layers.Remove(oldName);
        li.Name = newName;
        Layers[newName] = li;
        foreach (var e in Entities)
            if (string.Equals(e.Layer, oldName, StringComparison.OrdinalIgnoreCase)) e.Layer = newName;
        if (string.Equals(CurrentLayer, oldName, StringComparison.OrdinalIgnoreCase)) CurrentLayer = newName;
        return true;
    }

    /// <summary>Katmanı siler. Nesneler silinir ya da "0" katmanına taşınır.</summary>
    public void DeleteLayer(string name, bool deleteEntities)
    {
        if (name == "0" || !Layers.ContainsKey(name)) return;
        var items = Entities.Where(e => string.Equals(e.Layer, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (deleteEntities) Remove(items);
        else foreach (var e in items) e.Layer = "0";
        Layers.Remove(name);
        if (string.Equals(CurrentLayer, name, StringComparison.OrdinalIgnoreCase)) CurrentLayer = "0";
    }

    public void RaiseChanged()
    {
        _bounds.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ================================================================ Sınır kutusu önbelleği

    // Büyük çizimlerde her çizim/yakalama/tıklamada tüm nesnelerin sınır kutusunu yeniden
    // hesaplamak yüzlerce ms sürüyordu. Belge değiştiğinde (RaiseChanged) önbellek temizlenir.
    private readonly Dictionary<Entity, BBox> _bounds = new(ReferenceEqualityComparer.Instance);

    /// <summary>Nesnenin (önbellekli) sınır kutusu.</summary>
    public BBox BoundsOf(Entity e)
    {
        if (!_bounds.TryGetValue(e, out var b))
        {
            b = e.Bounds();
            _bounds[e] = b;
        }
        return b;
    }
    public void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
