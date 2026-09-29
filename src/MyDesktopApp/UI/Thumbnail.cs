using System.Windows;
using System.Windows.Media;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

/// <summary>Nesnelerden küçük önizleme resmi üretir.</summary>
public static class Thumbnail
{
    public static DrawingImage Render(IEnumerable<Entity> entities, double size = 96)
    {
        var list = BlockRefEntity.Flatten(entities).ToList();
        var box = BBox.Empty;
        foreach (var e in list) box.Add(e.Bounds());
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            if (!box.IsEmpty)
            {
                double pad = size * 0.08;
                double bw = Math.Max(box.Width, 1e-9), bh = Math.Max(box.Height, 1e-9);
                double s = Math.Min((size - 2 * pad) / bw, (size - 2 * pad) / bh);
                double ox = (size - bw * s) / 2 - box.MinX * s;
                double oy = (size + bh * s) / 2 + box.MinY * s;
                Point P(Vec2 v) => new(ox + v.X * s, oy - v.Y * s);

                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x30, 0x40)), 1.1);
                pen.Freeze();
                var fill = new SolidColorBrush(Color.FromRgb(0xD8, 0xE2, 0xEE));
                fill.Freeze();

                // Kapalı kesit dolgusu
                var loops = SectionProperties.BuildLoops(list, Math.Max(1e-6, Math.Max(bw, bh) * 1e-6), out _);
                if (loops.Count > 0)
                {
                    var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
                    using (var g = geo.Open())
                        foreach (var loop in loops)
                        {
                            g.BeginFigure(P(loop[0]), true, true);
                            g.PolyLineTo(loop.Skip(1).Select(P).ToList(), true, false);
                        }
                    geo.Freeze();
                    dc.DrawGeometry(fill, null, geo);
                }

                var lines = new StreamGeometry();
                using (var g = lines.Open())
                {
                    foreach (var e in list)
                    {
                        if (e is TextEntity or DimensionEntity or HatchEntity) continue;
                        foreach (var pr in e.Primitives())
                        {
                            var pts = pr.Tessellate();
                            if (pts.Count < 2) continue;
                            g.BeginFigure(P(pts[0]), false, false);
                            g.PolyLineTo(pts.Skip(1).Select(P).ToList(), true, false);
                        }
                    }
                }
                lines.Freeze();
                dc.DrawGeometry(null, pen, lines);
            }
        }
        group.Freeze();
        var img = new DrawingImage(group);
        img.Freeze();
        return img;
    }
}
