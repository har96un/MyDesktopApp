using MyDesktopApp.Geometry;

namespace MyDesktopApp.Model;

/// <summary>Renk: AutoCAD renk indeksi (ACI) ve/veya RGB.</summary>
public readonly record struct EntColor(byte R, byte G, byte B, short Aci = -1);

public enum SnapKind
{
    Endpoint,
    Midpoint,
    Center,
    Quadrant,
    Intersection,
    Insertion,
    Origin,
    Nearest
}

public readonly record struct SnapPoint(Vec2 Point, SnapKind Kind);

/// <summary>Tüm çizim nesnelerinin temel sınıfı.</summary>
public abstract class Entity
{
    public string Layer { get; set; } = "0";
    /// <summary>null = Katmandan (ByLayer).</summary>
    public EntColor? Color { get; set; }

    public abstract string TypeName { get; }

    public abstract IEnumerable<Prim> Primitives();
    public abstract void Transform(Mat2D m);
    public abstract IEnumerable<SnapPoint> SnapPoints();

    /// <summary>Kapalı bir eğri mi (kapalı polyline, çember).</summary>
    public virtual bool IsClosed => false;

    public virtual Entity Clone()
    {
        return (Entity)MemberwiseClone();
    }

    public virtual BBox Bounds()
    {
        var b = BBox.Empty;
        foreach (var p in Primitives()) b.Add(p.Bounds());
        return b;
    }

    public virtual double Distance(Vec2 p)
    {
        double d = double.MaxValue;
        foreach (var pr in Primitives()) d = Math.Min(d, pr.Distance(p));
        return d;
    }

    /// <summary>Eğriyi sıralı nokta dizisine çevirir.</summary>
    public virtual List<Vec2> ToPoints()
    {
        var list = new List<Vec2>();
        foreach (var pr in Primitives())
        {
            var pts = pr.Tessellate();
            if (list.Count > 0 && pts.Count > 0 && list[^1].IsClose(pts[0], 1e-9)) pts.RemoveAt(0);
            list.AddRange(pts);
        }
        return list;
    }

    /// <summary>Kutu (pencere) tamamen içeriyor mu.</summary>
    public virtual bool InsideWindow(BBox w) => w.ContainsBox(Bounds());

    /// <summary>Kutu nesneye dokunuyor mu (crossing).</summary>
    public virtual bool CrossesWindow(BBox w)
    {
        var b = Bounds();
        if (!w.Intersects(b)) return false;
        if (w.ContainsBox(b)) return true;
        var pts = ToPoints();
        for (int i = 0; i < pts.Count; i++)
        {
            if (w.Contains(pts[i])) return true;
            if (i == 0) continue;
            var a = pts[i - 1];
            var c = pts[i];
            var p1 = new Vec2(w.MinX, w.MinY);
            var p2 = new Vec2(w.MaxX, w.MinY);
            var p3 = new Vec2(w.MaxX, w.MaxY);
            var p4 = new Vec2(w.MinX, w.MaxY);
            if (GeoUtil.SegmentIntersect(a, c, p1, p2, out _) || GeoUtil.SegmentIntersect(a, c, p2, p3, out _) ||
                GeoUtil.SegmentIntersect(a, c, p3, p4, out _) || GeoUtil.SegmentIntersect(a, c, p4, p1, out _))
                return true;
        }
        return false;
    }
}

public sealed class LineEntity : Entity
{
    public Vec2 Start { get; set; }
    public Vec2 End { get; set; }

    public LineEntity(Vec2 start, Vec2 end) { Start = start; End = end; }

    public override string TypeName => "Çizgi";
    public double Length => Vec2.Distance(Start, End);

    public override IEnumerable<Prim> Primitives() { yield return new LinePrim(Start, End); }

    public override void Transform(Mat2D m)
    {
        Start = m.Apply(Start);
        End = m.Apply(End);
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        yield return new(Start, SnapKind.Endpoint);
        yield return new(End, SnapKind.Endpoint);
        yield return new((Start + End) / 2, SnapKind.Midpoint);
    }
}

public sealed class CircleEntity : Entity
{
    public Vec2 Center { get; set; }
    public double Radius { get; set; }

    public CircleEntity(Vec2 center, double radius) { Center = center; Radius = radius; }

    public override string TypeName => "Daire";
    public override bool IsClosed => true;

    public override IEnumerable<Prim> Primitives() { yield return new ArcPrim(Center, Radius, 0, GeoUtil.TwoPi, isFull: true); }

    public override void Transform(Mat2D m)
    {
        Center = m.Apply(Center);
        Radius *= m.UniformScale;
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        yield return new(Center, SnapKind.Center);
        for (int i = 0; i < 4; i++)
            yield return new(Center + Vec2.Polar(Radius, i * Math.PI / 2), SnapKind.Quadrant);
    }
}

public sealed class ArcEntity : Entity
{
    public Vec2 Center { get; set; }
    public double Radius { get; set; }
    /// <summary>Radyan, CCW.</summary>
    public double StartAngle { get; set; }
    public double EndAngle { get; set; }

    public ArcEntity(Vec2 center, double radius, double startAngle, double endAngle)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
    }

    public override string TypeName => "Yay";
    public Vec2 StartPoint => Center + Vec2.Polar(Radius, StartAngle);
    public Vec2 EndPoint => Center + Vec2.Polar(Radius, EndAngle);
    public double Sweep => GeoUtil.Sweep(StartAngle, EndAngle);

    public override IEnumerable<Prim> Primitives() { yield return new ArcPrim(Center, Radius, StartAngle, EndAngle); }

    public override void Transform(Mat2D m)
    {
        var s = m.Apply(StartPoint);
        var e = m.Apply(EndPoint);
        Center = m.Apply(Center);
        Radius *= m.UniformScale;
        if (m.IsMirroring)
        {
            StartAngle = (e - Center).Angle;
            EndAngle = (s - Center).Angle;
        }
        else
        {
            StartAngle = (s - Center).Angle;
            EndAngle = (e - Center).Angle;
        }
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        yield return new(StartPoint, SnapKind.Endpoint);
        yield return new(EndPoint, SnapKind.Endpoint);
        yield return new(Center + Vec2.Polar(Radius, StartAngle + Sweep / 2), SnapKind.Midpoint);
        yield return new(Center, SnapKind.Center);
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 2;
            if (GeoUtil.AngleInArc(a, StartAngle, EndAngle))
                yield return new(Center + Vec2.Polar(Radius, a), SnapKind.Quadrant);
        }
    }
}

public struct PolyVertex
{
    public Vec2 P;
    /// <summary>Bu köşeden sonraki parçanın bulge değeri (0 = düz).</summary>
    public double Bulge;

    public PolyVertex(Vec2 p, double bulge = 0) { P = p; Bulge = bulge; }
}

public sealed class PolylineEntity : Entity
{
    public List<PolyVertex> Vertices { get; private set; } = new();
    public bool Closed { get; set; }

    public PolylineEntity() { }

    public PolylineEntity(IEnumerable<Vec2> pts, bool closed)
    {
        Vertices.AddRange(pts.Select(p => new PolyVertex(p)));
        Closed = closed;
    }

    public override string TypeName => "Polyline";
    public override bool IsClosed => Closed;

    public int SegmentCount => Closed ? Vertices.Count : Math.Max(0, Vertices.Count - 1);

    public Prim Segment(int i)
    {
        var v1 = Vertices[i];
        var v2 = Vertices[(i + 1) % Vertices.Count];
        if (Math.Abs(v1.Bulge) < 1e-12 || v1.P.IsClose(v2.P, 1e-12))
            return new LinePrim(v1.P, v2.P);
        GeoUtil.BulgeArc(v1.P, v2.P, v1.Bulge, out var c, out var r, out var sa, out var ea);
        return new ArcPrim(c, r, sa, ea, reversed: v1.Bulge < 0);
    }

    public override IEnumerable<Prim> Primitives()
    {
        for (int i = 0; i < SegmentCount; i++) yield return Segment(i);
    }

    public override void Transform(Mat2D m)
    {
        bool mir = m.IsMirroring;
        for (int i = 0; i < Vertices.Count; i++)
        {
            var v = Vertices[i];
            Vertices[i] = new PolyVertex(m.Apply(v.P), mir ? -v.Bulge : v.Bulge);
        }
    }

    public override Entity Clone()
    {
        var c = (PolylineEntity)MemberwiseClone();
        c.Vertices = new List<PolyVertex>(Vertices);
        return c;
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        foreach (var v in Vertices) yield return new(v.P, SnapKind.Endpoint);
        for (int i = 0; i < SegmentCount; i++)
        {
            var s = Segment(i);
            if (s is LinePrim l) yield return new((l.A + l.B) / 2, SnapKind.Midpoint);
            else if (s is ArcPrim a)
            {
                yield return new(a.Center + Vec2.Polar(a.Radius, a.Start + GeoUtil.Sweep(a.Start, a.End) / 2), SnapKind.Midpoint);
                yield return new(a.Center, SnapKind.Center);
            }
        }
    }
}

public sealed class TextEntity : Entity
{
    public Vec2 Position { get; set; }
    public double Height { get; set; } = 2.5;
    /// <summary>Radyan.</summary>
    public double Rotation { get; set; }
    public string Value { get; set; } = "";

    public TextEntity(Vec2 position, double height, double rotation, string value)
    {
        Position = position;
        Height = height;
        Rotation = rotation;
        Value = value;
    }

    public override string TypeName => "Yazı";

    public double ApproxWidth => Math.Max(1, Value.Length) * Height * 0.7;

    private Vec2[] Corners()
    {
        var dx = Vec2.Polar(ApproxWidth, Rotation);
        var dy = Vec2.Polar(Height, Rotation + Math.PI / 2);
        return new[] { Position, Position + dx, Position + dx + dy, Position + dy };
    }

    public override IEnumerable<Prim> Primitives()
    {
        var c = Corners();
        for (int i = 0; i < 4; i++) yield return new LinePrim(c[i], c[(i + 1) % 4]);
    }

    public override double Distance(Vec2 p)
    {
        var c = Corners();
        if (GeoUtil.PointInPolygon(p, c)) return 0;
        return base.Distance(p);
    }

    public override void Transform(Mat2D m)
    {
        var dir = m.ApplyVector(Vec2.Polar(1, Rotation));
        Position = m.Apply(Position);
        Height *= m.UniformScale;
        double rot = dir.Angle;
        if (m.IsMirroring && dir.X < -1e-9)
        {
            // Yazıyı okunur tut (AutoCAD MIRRTEXT=0 benzeri)
            rot += Math.PI;
            Position -= Vec2.Polar(ApproxWidth, rot);
        }
        Rotation = GeoUtil.NormalizeAngle(rot);
    }

    public override IEnumerable<SnapPoint> SnapPoints()
    {
        yield return new(Position, SnapKind.Insertion);
    }
}
