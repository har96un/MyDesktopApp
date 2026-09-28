namespace MyDesktopApp.Geometry;

/// <summary>Eksenlere hizalı sınır kutusu.</summary>
public struct BBox
{
    public double MinX, MinY, MaxX, MaxY;
    public bool IsEmpty;

    public static BBox Empty => new() { IsEmpty = true, MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue };

    public static BBox FromPoints(Vec2 a, Vec2 b) => new()
    {
        MinX = Math.Min(a.X, b.X), MinY = Math.Min(a.Y, b.Y),
        MaxX = Math.Max(a.X, b.X), MaxY = Math.Max(a.Y, b.Y),
        IsEmpty = false
    };

    public void Add(Vec2 p)
    {
        if (p.X < MinX) MinX = p.X;
        if (p.Y < MinY) MinY = p.Y;
        if (p.X > MaxX) MaxX = p.X;
        if (p.Y > MaxY) MaxY = p.Y;
        IsEmpty = false;
    }

    public void Add(BBox b)
    {
        if (b.IsEmpty) return;
        Add(new Vec2(b.MinX, b.MinY));
        Add(new Vec2(b.MaxX, b.MaxY));
    }

    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Vec2 Min => new(MinX, MinY);
    public Vec2 Max => new(MaxX, MaxY);
    public Vec2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public bool Contains(Vec2 p) => !IsEmpty && p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool ContainsBox(BBox b) => !IsEmpty && !b.IsEmpty &&
        b.MinX >= MinX && b.MaxX <= MaxX && b.MinY >= MinY && b.MaxY <= MaxY;

    public bool Intersects(BBox b) => !IsEmpty && !b.IsEmpty &&
        b.MinX <= MaxX && b.MaxX >= MinX && b.MinY <= MaxY && b.MaxY >= MinY;
}
