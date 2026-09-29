using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

/// <summary>Blok tanımı: yerel koordinatlarda (temel nokta = 0,0) nesneler.</summary>
public sealed class BlockDef
{
    public string Name { get; set; }
    public List<Entity> Entities { get; } = new();

    public BlockDef(string name) { Name = name; }

    public BBox Bounds()
    {
        var b = BBox.Empty;
        foreach (var e in Entities) b.Add(e.Bounds());
        return b;
    }
}

/// <summary>Blok referansı (INSERT): bir blok tanımının dönüşümlü kopyası.</summary>
public sealed class BlockRefEntity : Entity
{
    public BlockDef Def { get; private set; }
    /// <summary>Yerel → dünya dönüşümü.</summary>
    public Mat2D M { get; private set; }

    private List<Entity>? _cache;

    public BlockRefEntity(BlockDef def, Mat2D m)
    {
        Def = def;
        M = m;
    }

    public override string TypeName => "Blok";
    public string Name => Def.Name;
    public Vec2 InsertPoint => new(M.E, M.F);
    public double RotationRad => Math.Atan2(M.C, M.A);
    public double ScaleFactor => Math.Sqrt(Math.Abs(M.Determinant));
    public bool Mirrored => M.IsMirroring;

    /// <summary>Tanım değiştiğinde (yeniden tanımlama) önbelleği temizler.</summary>
    public void Invalidate() => _cache = null;

    /// <summary>Dönüşmüş alt nesneler (geometri önbelleği; katman/renk ayrıca çözülür).</summary>
    private List<Entity> Children()
    {
        if (_cache != null) return _cache;
        var list = new List<Entity>(Def.Entities.Count);
        foreach (var c in Def.Entities)
        {
            var n = c.Clone();
            n.Transform(M);
            list.Add(n);
        }
        _cache = list;
        return list;
    }

    /// <summary>Alt nesnelerin dünya koordinatlarındaki bağımsız kopyaları (katman "0" → referansın katmanı).</summary>
    public IEnumerable<Entity> Explode()
    {
        foreach (var c in Children())
        {
            var n = c.Clone();
            if (n is BlockRefEntity nb) nb._cache = null;
            if (n.Layer == "0" || string.IsNullOrEmpty(n.Layer)) n.Layer = Layer;
            if (n.Color == null && Color != null) n.Color = Color;
            n.GroupId = GroupId;
            yield return n;
        }
    }

    /// <summary>Çizim için: alt nesneler ve görüntü katmanları (kopyalamadan).</summary>
    public IEnumerable<(Entity Child, string Layer, EntColor? Color)> DrawItems()
    {
        foreach (var c in Children())
        {
            var layer = c.Layer == "0" || string.IsNullOrEmpty(c.Layer) ? Layer : c.Layer;
            yield return (c, layer, c.Color ?? Color);
        }
    }

    public override IEnumerable<Prim> Primitives()
    {
        foreach (var c in Children())
            foreach (var p in c.Primitives()) yield return p;
    }

    public override void Transform(Mat2D m)
    {
        M = Mat2D.Then(M, m);
        _cache = null;
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        yield return new(InsertPoint, SnapKind.Insertion);
        foreach (var c in Children())
            foreach (var s in c.SnapPoints()) yield return s;
    }

    public override BBox Bounds()
    {
        var b = BBox.Empty;
        foreach (var c in Children()) b.Add(c.Bounds());
        return b;
    }

    public override double Distance(Vec2 p)
    {
        double d = double.MaxValue;
        foreach (var c in Children()) d = Math.Min(d, c.Distance(p));
        return d;
    }

    public override bool CrossesWindow(BBox w) => Children().Any(c => c.CrossesWindow(w));

    public override Entity Clone()
    {
        // Tanım paylaşılır; önbellek de paylaşılabilir (Transform yeni liste üretir)
        return (Entity)MemberwiseClone();
    }

    /// <summary>Blok referanslarını (iç içe dahil) patlatılmış nesnelere açar; diğerlerini aynen döndürür.</summary>
    public static IEnumerable<Entity> Flatten(IEnumerable<Entity> items, int depth = 0)
    {
        foreach (var e in items)
        {
            if (e is BlockRefEntity br && depth < 16)
            {
                foreach (var c in Flatten(br.Explode(), depth + 1)) yield return c;
            }
            else yield return e;
        }
    }
}
