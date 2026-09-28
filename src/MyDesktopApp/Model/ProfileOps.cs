using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

public enum RefPoint
{
    None,
    Centroid,
    LowerLeft,
    BoxCenter
}

/// <summary>Arayüzden bağımsız profil işlemleri (toplu işlem, kütüphane).</summary>
public static class ProfileOps
{
    public static Vec2 Reference(IReadOnlyCollection<Entity> items, RefPoint kind)
    {
        var b = BBox.Empty;
        foreach (var e in items) b.Add(e.Bounds());
        if (b.IsEmpty) return Vec2.Zero;
        switch (kind)
        {
            case RefPoint.LowerLeft: return new Vec2(b.MinX, b.MinY);
            case RefPoint.BoxCenter: return b.Center;
            case RefPoint.Centroid:
                var sp = SectionProperties.Compute(items);
                return sp.IsValid ? sp.Centroid : b.Center;
        }
        return Vec2.Zero;
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

    /// <summary>Kesiti asal eksenlerine göre döndürür. Kapalı kesit yoksa false.</summary>
    public static bool AlignPrincipal(IReadOnlyCollection<Entity> items)
    {
        var sp = SectionProperties.Compute(items);
        if (!sp.IsValid) return false;
        Apply(items, Mat2D.Rotation(-sp.PrincipalAngle, sp.Centroid));
        return true;
    }
}
