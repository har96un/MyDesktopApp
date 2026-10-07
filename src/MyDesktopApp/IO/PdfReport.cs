using System.IO;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MyDesktopApp.IO;

/// <summary>Rapor girdisi.</summary>
public sealed class ReportInput
{
    public required IReadOnlyList<Entity> Entities { get; init; }
    public string Title { get; init; } = "Profil Kesit Raporu";
    public string SourceName { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string LogoPath { get; init; } = "";
    public string Author { get; init; } = "";
    public bool AutoDimensions { get; init; } = true;
    /// <summary>Çizim birimi (etiketlerde).</summary>
    public string Unit { get; init; } = "mm";
}

/// <summary>A4 PDF profil raporu (logo, çizim, otomatik ölçüler, sınır kutusu ölçüleri).</summary>
public static class PdfReport
{
    private const double Margin = 36;
    private static readonly XPdfFontOptions FontOpts = new(PdfFontEncoding.Unicode);

    private static XFont Font(double size, bool bold = false) =>
        new("Segoe UI", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular, FontOpts);

    public static void Create(string path, ReportInput input)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = input.Title;
        doc.Info.Creator = "Profil CAD";
        if (!string.IsNullOrWhiteSpace(input.Author)) doc.Info.Author = input.Author;
        AddPage(doc, input);
        doc.Save(path);
    }

    public static void AddPage(PdfDocument doc, ReportInput input)
    {
        var page = doc.AddPage();
        page.Size = PageSize.A4;
        using var g = XGraphics.FromPdfPage(page);
        double W = g.PageSize.Width, H = g.PageSize.Height;
        var ink = XBrushes.Black;
        var gray = new XSolidBrush(XColor.FromArgb(90, 90, 90));
        var accent = XColor.FromArgb(0x1F, 0x4E, 0x79);

        // ---------------------------------------------------------------- Başlık
        double y = Margin;
        double x = Margin;
        double headerH = 56;
        if (!string.IsNullOrWhiteSpace(input.LogoPath) && File.Exists(input.LogoPath))
        {
            try
            {
                var img = XImage.FromFile(input.LogoPath);
                double iw = img.PointWidth, ih = img.PointHeight;
                double sc = Math.Min(130 / iw, headerH / ih);
                g.DrawImage(img, x, y + (headerH - ih * sc) / 2, iw * sc, ih * sc);
                x += iw * sc + 12;
            }
            catch { /* logo okunamadı */ }
        }
        if (!string.IsNullOrWhiteSpace(input.CompanyName))
            g.DrawString(input.CompanyName, Font(13, true), ink, new XRect(x, y + 4, W - Margin - x - 150, 18), XStringFormats.TopLeft);
        g.DrawString(input.Title, Font(16, true), new XSolidBrush(accent), new XRect(x, y + 24, W - Margin - x - 150, 22), XStringFormats.TopLeft);

        var right = new XRect(W - Margin - 150, y + 4, 150, 14);
        g.DrawString(DateTime.Now.ToString("dd.MM.yyyy HH:mm"), Font(9), gray, right, XStringFormats.TopRight);
        right.Offset(0, 14);
        if (!string.IsNullOrWhiteSpace(input.SourceName))
            g.DrawString(Shorten(input.SourceName, 40), Font(9), gray, right, XStringFormats.TopRight);
        right.Offset(0, 14);
        if (!string.IsNullOrWhiteSpace(input.Author))
            g.DrawString("Hazırlayan: " + input.Author, Font(9), gray, right, XStringFormats.TopRight);

        y += headerH + 8;
        g.DrawLine(new XPen(accent, 1.5), Margin, y, W - Margin, y);
        y += 10;

        // ---------------------------------------------------------------- Çizim
        var frame = new XRect(Margin, y, W - 2 * Margin, 390);
        g.DrawRectangle(new XPen(XColor.FromArgb(180, 180, 180), 0.6), frame);

        var ents = BlockRefEntity.Flatten(input.Entities).ToList();
        var box = BBox.Empty;
        foreach (var e in ents) box.Add(e.Bounds());
        var geomBox = BBox.Empty;
        foreach (var e in ents.Where(e => e is not (TextEntity or DimensionEntity))) geomBox.Add(e.Bounds());
        if (geomBox.IsEmpty) geomBox = box;

        double scale = 1;
        if (!box.IsEmpty)
        {
            double pad = input.AutoDimensions ? 46 : 20;
            double bw = Math.Max(box.Width, 1e-9), bh = Math.Max(box.Height, 1e-9);
            scale = Math.Min((frame.Width - 2 * pad) / bw, (frame.Height - 2 * pad) / bh);
            double ox = frame.X + (frame.Width - bw * scale) / 2 - box.MinX * scale;
            double oy = frame.Y + (frame.Height + bh * scale) / 2 + box.MinY * scale;
            XPoint P(Vec2 v) => new(ox + v.X * scale, oy - v.Y * scale);

            var state = g.Save();
            g.IntersectClip(frame);
            DrawEntities(g, ents, P, scale);

            if (input.AutoDimensions && !geomBox.IsEmpty) DrawAutoDims(g, geomBox, P, input.Unit);
            g.Restore(state);

            // Ölçek bilgisi (1 birim = 1 mm varsayımı)
            double ptPerMm = 72 / 25.4;
            double ratio = scale / ptPerMm;
            string sc = ratio >= 1 ? $"{ratio:0.##}:1" : $"1:{1 / ratio:0.##}";
            g.DrawString($"Ölçek ≈ {sc} (1 birim = 1 {input.Unit})", Font(8), gray,
                new XRect(frame.X + 6, frame.Bottom - 16, 250, 12), XStringFormats.TopLeft);
        }
        y = frame.Bottom + 14;

        // ---------------------------------------------------------------- Tablo
        g.DrawString("Profil Ölçüleri", Font(12, true), new XSolidBrush(accent), new XRect(Margin, y, 300, 16), XStringFormats.TopLeft);
        y += 20;
        var rows = BuildRows(geomBox, ents, input.Unit);
        double colW = (W - 2 * Margin) / 2;
        int half = (rows.Count + 1) / 2;
        double rowH = 17;
        var linePen = new XPen(XColor.FromArgb(215, 215, 215), 0.5);
        for (int i = 0; i < rows.Count; i++)
        {
            int col = i < half ? 0 : 1;
            int row = i < half ? i : i - half;
            double cx = Margin + col * colW, cy = y + row * rowH;
            if (row % 2 == 0) g.DrawRectangle(new XSolidBrush(XColor.FromArgb(244, 246, 249)), cx, cy, colW - 8, rowH);
            g.DrawString(rows[i].Label, Font(9), gray, new XRect(cx + 4, cy, colW * 0.45, rowH), XStringFormats.CenterLeft);
            g.DrawString(rows[i].Value, Font(9, true), ink, new XRect(cx + colW * 0.42, cy, colW * 0.58 - 12, rowH), XStringFormats.CenterRight);
            g.DrawLine(linePen, cx, cy + rowH, cx + colW - 8, cy + rowH);
        }
        y += half * rowH + 10;

        // ---------------------------------------------------------------- Alt bilgi
        g.DrawLine(new XPen(XColor.FromArgb(200, 200, 200), 0.5), Margin, H - Margin - 14, W - Margin, H - Margin - 14);
        g.DrawString("Profil CAD ile oluşturuldu", Font(8), gray, new XRect(Margin, H - Margin - 10, 250, 10), XStringFormats.TopLeft);
        g.DrawString("Ölçüler profilin sınır kutusuna (dikdörtgen) göredir", Font(8), gray,
            new XRect(W - Margin - 300, H - Margin - 10, 300, 10), XStringFormats.TopRight);
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : "…" + s[^(max - 1)..];

    private static string N(double v)
    {
        double a = Math.Abs(v);
        if (a != 0 && (a >= 1e7 || a < 1e-3)) return v.ToString("0.###E+0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        return v.ToString(a >= 1000 ? "#,0.##" : "0.####", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
    }

    private static List<(string Label, string Value)> BuildRows(BBox b, IReadOnlyList<Entity> ents, string u)
    {
        var rows = new List<(string, string)>();
        if (b.IsEmpty) return rows;
        rows.Add(("Genişlik × Yükseklik", $"{N(b.Width)} × {N(b.Height)} {u}"));
        rows.Add(("Genişlik (X)", $"{N(b.Width)} {u}"));
        rows.Add(("Yükseklik (Y)", $"{N(b.Height)} {u}"));
        rows.Add(("Sol alt köşe", $"{N(b.MinX)} ; {N(b.MinY)}"));
        rows.Add(("Sağ üst köşe", $"{N(b.MaxX)} ; {N(b.MaxY)}"));
        rows.Add(("Kutu merkezi", $"{N((b.MinX + b.MaxX) / 2)} ; {N((b.MinY + b.MaxY) / 2)}"));
        int circles = ents.Count(e => e is CircleEntity);
        rows.Add(("Nesne sayısı", ents.Count(e => e is not (TextEntity or DimensionEntity or HatchEntity)).ToString()));
        if (circles > 0) rows.Add(("Delik / daire", circles.ToString()));
        return rows;
    }

    // ================================================================ Çizim

    private static void DrawPolyline(XGraphics g, XPen pen, List<Vec2> pts, Func<Vec2, XPoint> P)
    {
        if (pts.Count < 2) return;
        g.DrawLines(pen, pts.Select(P).ToArray());
    }

    private static void DrawEntities(XGraphics g, IReadOnlyList<Entity> ents, Func<Vec2, XPoint> P, double scale)
    {
        var pen = new XPen(XColors.Black, 0.9) { LineJoin = XLineJoin.Round, LineCap = XLineCap.Round };
        var hatchPen = new XPen(XColor.FromArgb(120, 120, 120), 0.4);
        var hatchFill = new XSolidBrush(XColor.FromArgb(200, 200, 200));
        var dimColor = XColor.FromArgb(0x1F, 0x4E, 0x79);
        var dimPen = new XPen(dimColor, 0.5);
        var dimBrush = new XSolidBrush(dimColor);

        // Önce taramalar (altta kalsın)
        foreach (var h in ents.OfType<HatchEntity>())
        {
            if (h.IsSolid)
            {
                var path = new XGraphicsPath { FillMode = XFillMode.Alternate };
                foreach (var poly in h.LoopPolygons()) path.AddPolygon(poly.Select(P).ToArray());
                g.DrawPath(hatchFill, path);
            }
            else
            {
                foreach (var (a, b) in h.PatternSegments(4000)) g.DrawLine(hatchPen, P(a), P(b));
            }
        }

        foreach (var e in ents)
        {
            switch (e)
            {
                case HatchEntity:
                    break;
                case TextEntity t:
                    {
                        double size = Math.Max(1, t.Height * scale * 1.35);
                        var lines = t.Lines;
                        for (int li = 0; li < lines.Length; li++)
                        {
                            if (lines[li].Length == 0) continue;
                            var at = P(t.LineOrigin(li));
                            var st = g.Save();
                            g.RotateAtTransform(-GeoUtil.RadToDeg(t.Rotation), at);
                            g.DrawString(lines[li], Font(size), XBrushes.Black, at, XStringFormats.BaseLineLeft);
                            g.Restore(st);
                        }
                        break;
                    }
                case DimensionEntity d:
                    {
                        var geo = d.Build();
                        foreach (var (a, b) in geo.Lines) g.DrawLine(dimPen, P(a), P(b));
                        foreach (var arc in geo.Arcs) DrawPolyline(g, dimPen, arc.Tessellate(), P);
                        foreach (var arr in geo.Arrows) g.DrawPolygon(dimBrush, arr.Select(P).ToArray(), XFillMode.Winding);
                        foreach (var (anchor, rot, hgt, text) in geo.Texts)
                        {
                            double size = Math.Max(5, hgt * scale * 1.35);
                            var at = P(anchor);
                            var st = g.Save();
                            g.RotateAtTransform(-GeoUtil.RadToDeg(rot), at);
                            g.DrawString(text, Font(size), dimBrush, at, XStringFormats.BottomCenter);
                            g.Restore(st);
                        }
                        break;
                    }
                default:
                    foreach (var pr in e.Primitives()) DrawPolyline(g, pen, pr.Tessellate(), P);
                    break;
            }
        }
    }

    private static void DrawAutoDims(XGraphics g, BBox b, Func<Vec2, XPoint> P, string unit)
    {
        var col = XColor.FromArgb(0x1F, 0x4E, 0x79);
        var pen = new XPen(col, 0.5);
        var brush = new XSolidBrush(col);
        var font = Font(8);

        var bl = P(new Vec2(b.MinX, b.MinY));
        var br = P(new Vec2(b.MaxX, b.MinY));
        var tr = P(new Vec2(b.MaxX, b.MaxY));

        // Genişlik (altta)
        double yd = bl.Y + 22;
        g.DrawLine(pen, bl.X, bl.Y + 3, bl.X, yd + 4);
        g.DrawLine(pen, br.X, br.Y + 3, br.X, yd + 4);
        g.DrawLine(pen, bl.X, yd, br.X, yd);
        Arrow(g, brush, new XPoint(bl.X, yd), new XVector(1, 0));
        Arrow(g, brush, new XPoint(br.X, yd), new XVector(-1, 0));
        g.DrawString(N(b.Width), font, brush, new XPoint((bl.X + br.X) / 2, yd - 2), XStringFormats.BottomCenter);

        // Yükseklik (sağda)
        double xd = br.X + 22;
        g.DrawLine(pen, br.X + 3, br.Y, xd + 4, br.Y);
        g.DrawLine(pen, tr.X + 3, tr.Y, xd + 4, tr.Y);
        g.DrawLine(pen, xd, br.Y, xd, tr.Y);
        Arrow(g, brush, new XPoint(xd, br.Y), new XVector(0, -1));
        Arrow(g, brush, new XPoint(xd, tr.Y), new XVector(0, 1));
        var mid = new XPoint(xd - 2, (br.Y + tr.Y) / 2);
        var st = g.Save();
        g.RotateAtTransform(-90, mid);
        g.DrawString(N(b.Height), font, brush, mid, XStringFormats.BottomCenter);
        g.Restore(st);
    }

    private static void Arrow(XGraphics g, XBrush brush, XPoint tip, XVector dir)
    {
        var n = new XVector(-dir.Y, dir.X);
        var basePt = tip + dir * 5;
        g.DrawPolygon(brush, new[] { tip, basePt + n * 1.6, basePt - n * 1.6 }, XFillMode.Winding);
    }
}
