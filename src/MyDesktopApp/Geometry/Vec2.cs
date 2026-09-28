using System.Globalization;

namespace MyDesktopApp.Geometry;

/// <summary>2B nokta / vektör.</summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly double X;
    public readonly double Y;

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vec2 Zero = new(0, 0);
    public static readonly Vec2 UnitX = new(1, 0);
    public static readonly Vec2 UnitY = new(0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;
    /// <summary>Radyan cinsinden açı (-π, π].</summary>
    public double Angle => Math.Atan2(Y, X);

    public Vec2 Normalized()
    {
        double l = Length;
        return l < 1e-15 ? Zero : new Vec2(X / l, Y / l);
    }

    /// <summary>Saat yönünün tersine 90° döndürülmüş vektör.</summary>
    public Vec2 PerpLeft => new(-Y, X);

    public Vec2 Rotate(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public Vec2 RotateAround(Vec2 center, double angle) => center + (this - center).Rotate(angle);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    public static Vec2 Polar(double length, double angle) => new(length * Math.Cos(angle), length * Math.Sin(angle));

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);
    public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
    public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

    public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y);

    public bool IsClose(Vec2 other, double tol) => (this - other).LengthSquared <= tol * tol;

    public override string ToString() => Format(this);

    public static string Format(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    public static string Format(Vec2 p) => $"{Format(p.X)}, {Format(p.Y)}";
}
