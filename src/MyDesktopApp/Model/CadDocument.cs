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

    public string? FilePath { get; set; }
    public bool IsModified { get; set; }
    public string CurrentLayer { get; set; } = "0";

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
        foreach (var e in items ?? VisibleEntities) b.Add(e.Bounds());
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

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
