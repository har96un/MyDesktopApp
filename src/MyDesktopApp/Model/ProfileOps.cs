using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

/// <summary>Sınır kutusunun (dikdörtgenin) 9 referans noktası.</summary>
public enum RefPoint
{
    None,
    LowerLeft,
    BottomCenter,
    LowerRight,
    MiddleLeft,
    BoxCenter,
    MiddleRight,
    UpperLeft,
    TopCenter,
    UpperRight
}

/// <summary>Arayüzden bağımsız profil işlemleri (toplu işlem, kütüphane). Profil her zaman dikdörtgen (sınır kutusu) olarak ele alınır.</summary>
public static class ProfileOps
{
    public static readonly string[] RefNames =
    {
        "Değiştirme", "Sol alt → 0,0", "Alt orta → 0,0", "Sağ alt → 0,0",
        "Sol orta → 0,0", "Merkez → 0,0", "Sağ orta → 0,0",
        "Sol üst → 0,0", "Üst orta → 0,0", "Sağ üst → 0,0"
    };

    /// <summary>Referansın kutu içindeki oranı (fx, fy ∈ {0, 0.5, 1}).</summary>
    public static (double Fx, double Fy) Fractions(RefPoint kind)
    {
        int i = Math.Max(0, (int)kind - 1);
        return ((i % 3) * 0.5, (i / 3) * 0.5);
    }

    public static Vec2 Reference(IReadOnlyCollection<Entity> items, RefPoint kind)
    {
        if (kind == RefPoint.None) return Vec2.Zero;
        var b = BBox.Empty;
        foreach (var e in items) b.Add(e.Bounds());
        if (b.IsEmpty) return Vec2.Zero;
        var (fx, fy) = Fractions(kind);
        return new Vec2(b.MinX + fx * (b.MaxX - b.MinX), b.MinY + fy * (b.MaxY - b.MinY));
    }

    public static void Apply(IEnumerable<Entity> items, Mat2D m)
    {
        foreach (var e in items) e.Transform(m);
    }

    public static void MoveToOrigin(IReadOnlyCollection<Entity> items, RefPoint kind)
    {
        if (kind == RefPoint.None) return;
        Apply(items, Mat2D.Translation(-Reference(items, kind)));
    }
}
