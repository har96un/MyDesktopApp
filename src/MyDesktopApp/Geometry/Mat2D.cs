namespace MyDesktopApp.Geometry;

/// <summary>
/// 2B afin dönüşüm:  x' = A·x + B·y + E ,  y' = C·x + D·y + F
/// </summary>
public readonly struct Mat2D
{
    public readonly double A, B, C, D, E, F;

    public Mat2D(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    public static readonly Mat2D Identity = new(1, 0, 0, 1, 0, 0);

    public double Determinant => A * D - B * C;
    /// <summary>Aynalama içeriyorsa true.</summary>
    public bool IsMirroring => Determinant < 0;
    /// <summary>Düzgün (uniform) ölçek katsayısı.</summary>
    public double UniformScale => Math.Sqrt(Math.Abs(Determinant));

    public Vec2 Apply(Vec2 p) => new(A * p.X + B * p.Y + E, C * p.X + D * p.Y + F);
    public Vec2 ApplyVector(Vec2 v) => new(A * v.X + B * v.Y, C * v.X + D * v.Y);

    /// <summary>Önce <paramref name="first"/>, sonra <paramref name="second"/> uygulanır.</summary>
    public static Mat2D Then(Mat2D first, Mat2D second)
    {
        var m = second;
        var n = first;
        return new Mat2D(
            m.A * n.A + m.B * n.C,
            m.A * n.B + m.B * n.D,
            m.C * n.A + m.D * n.C,
            m.C * n.B + m.D * n.D,
            m.A * n.E + m.B * n.F + m.E,
            m.C * n.E + m.D * n.F + m.F);
    }

    public static Mat2D Translation(Vec2 d) => new(1, 0, 0, 1, d.X, d.Y);

    public static Mat2D Rotation(double angle, Vec2 center)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        // Tam 90° katlarında sayısal gürültüyü temizle
        if (Math.Abs(c) < 1e-14) c = 0;
        if (Math.Abs(s) < 1e-14) s = 0;
        var r = new Mat2D(c, -s, s, c, 0, 0);
        return Then(Then(Translation(-center), r), Translation(center));
    }

    public static Mat2D Scaling(double factor, Vec2 center)
    {
        var s = new Mat2D(factor, 0, 0, factor, 0, 0);
        return Then(Then(Translation(-center), s), Translation(center));
    }

    /// <summary>p1-p2 doğrusuna göre aynalama.</summary>
    public static Mat2D Mirror(Vec2 p1, Vec2 p2)
    {
        var d = (p2 - p1).Normalized();
        if (d.LengthSquared < 1e-20) return Identity;
        double c2 = d.X * d.X - d.Y * d.Y;   // cos 2θ
        double s2 = 2 * d.X * d.Y;           // sin 2θ
        var m = new Mat2D(c2, s2, s2, -c2, 0, 0);
        return Then(Then(Translation(-p1), m), Translation(p1));
    }
}
