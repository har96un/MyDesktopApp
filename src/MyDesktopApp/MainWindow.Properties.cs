using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp;

/// <summary>Özellikler paneli: seçili nesnelerin katman, renk ve geometrisini düzenler.</summary>
public partial class MainWindow
{
    private int _propVertex;
    private Entity? _propVertexOwner;

    private static string Fmt(double v) => Vec2.Format(v);

    private static bool ParseNum(string s, out double v) =>
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>Seçili nesnelerde değişiklik yapar (geri alınabilir).</summary>
    private void EditSelection(Action change)
    {
        if (_editor.IsBusy) { AppendHistory("Komut çalışırken özellik değiştirilemez (Esc ile iptal edin)."); return; }
        _doc.SaveUndo();
        change();
        _editor.NotifyDocumentChanged();
        _doc.RaiseSelectionChanged();
    }

    private void PropHeader(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2), Foreground = Brushes.DimGray });

    private DockPanel PropRowFrame(string label, FrameworkElement editor)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var l = new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        row.Children.Add(editor);
        PropsPanel.Children.Add(row);
        return row;
    }

    /// <summary>Metin kutulu özellik satırı. commit null ise salt okunur.</summary>
    private void PropText(string label, string value, Action<string>? commit)
    {
        var tb = new TextBox
        {
            Text = value,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(2, 1, 2, 1),
            IsReadOnly = commit == null,
            Background = commit == null ? new SolidColorBrush(Color.FromRgb(0xEE, 0xEF, 0xF2)) : Brushes.White
        };
        if (commit != null)
        {
            bool done = false;
            void Commit()
            {
                if (done || tb.Text == value) return;
                done = true;
                commit(tb.Text);
            }
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Commit(); InputBox.Focus(); }
                else if (e.Key == Key.Escape) { tb.Text = value; e.Handled = true; InputBox.Focus(); }
            };
            tb.LostKeyboardFocus += (_, _) => Commit();
        }
        PropRowFrame(label, tb);
    }

    /// <summary>Sayısal özellik satırı.</summary>
    private void PropNum(string label, double value, Action<double>? commit, Func<double, bool>? valid = null, bool degrees = false)
    {
        double shown = degrees ? GeoUtil.RadToDeg(value) : value;
        PropText(label, Fmt(shown), commit == null ? null : s =>
        {
            if (!ParseNum(s, out double v) || (valid != null && !valid(v)))
            {
                AppendHistory($"Geçersiz değer: {s}");
                _doc.RaiseSelectionChanged();
                return;
            }
            if (degrees) v = GeoUtil.DegToRad(v);
            commit(v);
        });
    }

    private void PropCombo(string label, IEnumerable<string> items, string? selected, Action<string> commit)
    {
        var cb = new ComboBox { FontSize = 12 };
        foreach (var i in items) cb.Items.Add(i);
        cb.SelectedItem = selected;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is string s && s != selected) commit(s);
        };
        PropRowFrame(label, cb);
    }

    private void PropCheck(string label, bool value, Action<bool> commit)
    {
        var cb = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        cb.Click += (_, _) => commit(cb.IsChecked == true);
        PropRowFrame(label, cb);
    }

    private const string ByLayer = "Katmandan";

    private static string ColorName(EntColor? c)
    {
        if (c is not { } col) return ByLayer;
        foreach (var (n, pc) in Palette)
            if (pc.R == col.R && pc.G == col.G && pc.B == col.B) return n;
        return $"RGB {col.R},{col.G},{col.B}";
    }

    private void BuildPropertiesPanel()
    {
        PropsPanel.Children.Clear();
        var sel = _doc.Selection.ToList();
        if (sel.Count == 0)
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Nesne seçin.", Foreground = Brushes.Gray, FontSize = 12 });
            return;
        }

        // Ortak: katman ve renk
        var layers = _doc.Layers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var selLayers = sel.Select(e => e.Layer).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PropCombo("Katman", layers, selLayers.Count == 1 ? layers.FirstOrDefault(l => string.Equals(l, selLayers[0], StringComparison.OrdinalIgnoreCase)) : null,
            l => EditSelection(() => { foreach (var e in sel) e.Layer = l; }));

        var colorNames = new List<string> { ByLayer };
        colorNames.AddRange(Palette.Select(p => p.Name));
        var selColors = sel.Select(e => ColorName(e.Color)).Distinct().ToList();
        string? curColor = selColors.Count == 1 ? selColors[0] : null;
        if (curColor != null && !colorNames.Contains(curColor)) colorNames.Add(curColor);
        PropCombo("Renk", colorNames, curColor, n =>
        {
            if (n.StartsWith("RGB", StringComparison.Ordinal)) return;
            EntColor? c = n == ByLayer ? null : Palette.First(p => p.Name == n).C;
            EditSelection(() => { foreach (var e in sel) e.Color = c; });
        });

        if (sel.Count > 1)
        {
            // Aynı türden çok nesne: bazı ortak alanlar
            if (sel.All(e => e is DimensionEntity))
            {
                var dims = sel.Cast<DimensionEntity>().ToList();
                PropHeader($"{dims.Count} ölçü");
                PropNum("Yazı yük.", dims[0].TextHeight, v => EditSelection(() => dims.ForEach(d => d.TextHeight = v)), v => v > 0);
                PropNum("Ok boyu", dims[0].ArrowSize, v => EditSelection(() => dims.ForEach(d => d.ArrowSize = v)), v => v > 0);
                PropNum("Ondalık", dims[0].Decimals, v => EditSelection(() => dims.ForEach(d => d.Decimals = (int)v)), v => v >= 0 && v <= 8);
            }
            else if (sel.All(e => e is HatchEntity))
            {
                var hs = sel.Cast<HatchEntity>().ToList();
                PropHeader($"{hs.Count} tarama");
                PropCombo("Desen", HatchEntity.Patterns, hs.Select(h => h.Pattern.ToUpperInvariant()).Distinct().Count() == 1 ? hs[0].Pattern.ToUpperInvariant() : null,
                    p => EditSelection(() => hs.ForEach(h => h.Pattern = p)));
                PropNum("Ölçek", hs[0].Scale, v => EditSelection(() => hs.ForEach(h => h.Scale = v)), v => v > 0);
            }
            else if (sel.All(e => e is TextEntity))
            {
                var ts = sel.Cast<TextEntity>().ToList();
                PropHeader($"{ts.Count} yazı");
                PropNum("Yükseklik", ts[0].Height, v => EditSelection(() => ts.ForEach(t => t.Height = v)), v => v > 0);
            }
            return;
        }

        var ent = sel[0];
        PropHeader(ent.TypeName + (ent.GroupId != null ? $"  ({ent.GroupId})" : ""));
        switch (ent)
        {
            case LineEntity l:
                PropNum("Başlangıç X", l.Start.X, v => EditSelection(() => l.Start = new Vec2(v, l.Start.Y)));
                PropNum("Başlangıç Y", l.Start.Y, v => EditSelection(() => l.Start = new Vec2(l.Start.X, v)));
                PropNum("Bitiş X", l.End.X, v => EditSelection(() => l.End = new Vec2(v, l.End.Y)));
                PropNum("Bitiş Y", l.End.Y, v => EditSelection(() => l.End = new Vec2(l.End.X, v)));
                PropNum("Uzunluk", l.Length, v => EditSelection(() =>
                {
                    var d = (l.End - l.Start).Normalized();
                    if (d.LengthSquared < 1e-12) d = Vec2.UnitX;
                    l.End = l.Start + d * v;
                }), v => v > 0);
                PropNum("Açı (°)", (l.End - l.Start).Angle, v => EditSelection(() => l.End = l.Start + Vec2.Polar(l.Length, v)), degrees: true);
                PropNum("ΔX", l.End.X - l.Start.X, null);
                PropNum("ΔY", l.End.Y - l.Start.Y, null);
                break;

            case CircleEntity c:
                PropNum("Merkez X", c.Center.X, v => EditSelection(() => c.Center = new Vec2(v, c.Center.Y)));
                PropNum("Merkez Y", c.Center.Y, v => EditSelection(() => c.Center = new Vec2(c.Center.X, v)));
                PropNum("Yarıçap", c.Radius, v => EditSelection(() => c.Radius = v), v => v > 0);
                PropNum("Çap", c.Radius * 2, v => EditSelection(() => c.Radius = v / 2), v => v > 0);
                PropNum("Çevre", 2 * Math.PI * c.Radius, null);
                PropNum("Alan", Math.PI * c.Radius * c.Radius, null);
                break;

            case ArcEntity a:
                PropNum("Merkez X", a.Center.X, v => EditSelection(() => a.Center = new Vec2(v, a.Center.Y)));
                PropNum("Merkez Y", a.Center.Y, v => EditSelection(() => a.Center = new Vec2(a.Center.X, v)));
                PropNum("Yarıçap", a.Radius, v => EditSelection(() => a.Radius = v), v => v > 0);
                PropNum("Başl. açısı (°)", a.StartAngle, v => EditSelection(() => a.StartAngle = GeoUtil.NormalizeAngle(v)), degrees: true);
                PropNum("Bitiş açısı (°)", a.EndAngle, v => EditSelection(() => a.EndAngle = GeoUtil.NormalizeAngle(v)), degrees: true);
                PropNum("Yay uzunluğu", a.Radius * a.Sweep, null);
                break;

            case PolylineEntity p:
                {
                    PropCheck("Kapalı", p.Closed, v => EditSelection(() => p.Closed = v));
                    double len = p.Primitives().Sum(pr =>
                    {
                        var pts = pr.Tessellate();
                        double s = 0;
                        for (int i = 1; i < pts.Count; i++) s += Vec2.Distance(pts[i - 1], pts[i]);
                        return s;
                    });
                    PropNum("Uzunluk", len, null);
                    PropNum("Köşe sayısı", p.Vertices.Count, null);
                    if (p.Vertices.Count == 0) break;
                    if (_propVertexOwner != p) { _propVertex = 0; _propVertexOwner = p; }
                    _propVertex = Math.Clamp(_propVertex, 0, p.Vertices.Count - 1);
                    var idx = Enumerable.Range(1, p.Vertices.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
                    PropCombo("Köşe", idx, (_propVertex + 1).ToString(CultureInfo.InvariantCulture), s =>
                    {
                        _propVertex = int.Parse(s, CultureInfo.InvariantCulture) - 1;
                        BuildPropertiesPanel();
                    });
                    int k = _propVertex;
                    var vx = p.Vertices[k];
                    PropNum("  X", vx.P.X, v => EditSelection(() => p.Vertices[k] = new PolyVertex(new Vec2(v, p.Vertices[k].P.Y), p.Vertices[k].Bulge)));
                    PropNum("  Y", vx.P.Y, v => EditSelection(() => p.Vertices[k] = new PolyVertex(new Vec2(p.Vertices[k].P.X, v), p.Vertices[k].Bulge)));
                    PropNum("  Bulge", vx.Bulge, v => EditSelection(() => p.Vertices[k] = new PolyVertex(p.Vertices[k].P, v)));
                    break;
                }

            case TextEntity t:
                PropText("Metin", t.Value, s => EditSelection(() => t.Value = s));
                PropNum("X", t.Position.X, v => EditSelection(() => t.Position = new Vec2(v, t.Position.Y)));
                PropNum("Y", t.Position.Y, v => EditSelection(() => t.Position = new Vec2(t.Position.X, v)));
                PropNum("Yükseklik", t.Height, v => EditSelection(() => t.Height = v), v => v > 0);
                PropNum("Açı (°)", t.Rotation, v => EditSelection(() => t.Rotation = GeoUtil.NormalizeAngle(v)), degrees: true);
                break;

            case DimensionEntity d:
                PropText("Ölçü değeri", d.FormatValue(d.Measurement), null);
                PropText("Metin", d.TextOverride ?? "", s => EditSelection(() => d.TextOverride = string.IsNullOrWhiteSpace(s) ? null : s));
                PropNum("Yazı yük.", d.TextHeight, v => EditSelection(() => d.TextHeight = v), v => v > 0);
                PropNum("Ok boyu", d.ArrowSize, v => EditSelection(() => d.ArrowSize = v), v => v > 0);
                PropNum("Ondalık", d.Decimals, v => EditSelection(() => d.Decimals = (int)v), v => v >= 0 && v <= 8);
                PropsPanel.Children.Add(new TextBlock { Text = "Metinde <> ölçü değerinin yerine geçer.", FontSize = 10, Foreground = Brushes.Gray });
                break;

            case HatchEntity h:
                PropCombo("Desen", HatchEntity.Patterns, HatchEntity.Patterns.FirstOrDefault(x => string.Equals(x, h.Pattern, StringComparison.OrdinalIgnoreCase)),
                    s => EditSelection(() => h.Pattern = s));
                PropNum("Ölçek", h.Scale, v => EditSelection(() => h.Scale = v), v => v > 0);
                PropNum("Açı (°)", h.Angle, v => EditSelection(() => h.Angle = v), degrees: true);
                PropNum("Çevre sayısı", h.Loops.Count, null);
                break;
        }
    }
}
