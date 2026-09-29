using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyDesktopApp.Editor;
using MyDesktopApp.Geometry;
using Shapes = System.Windows.Shapes;

namespace MyDesktopApp;

/// <summary>Şeritli (ribbon) üst menü: sekmeler, gruplar, vektör simgeli büyük/küçük düğmeler.</summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------- Renkler
    private static readonly Brush RibBar = Frz(new LinearGradientBrush(Color.FromRgb(0x1B, 0x44, 0x6B), Color.FromRgb(0x24, 0x57, 0x88), 0));
    private static readonly Brush RibBody = Frz(new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA)));
    private static readonly Brush RibLine = Frz(new SolidColorBrush(Color.FromRgb(0xD3, 0xD9, 0xE1)));
    private static readonly Brush RibLabel = Frz(new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x78)));
    private static readonly Brush IconInk = Frz(new SolidColorBrush(Color.FromRgb(0x27, 0x4B, 0x73)));
    private static readonly Brush IconAccent = Frz(new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x10)));
    private static readonly Brush AccentBlue = Frz(new SolidColorBrush(Color.FromRgb(0x2E, 0x75, 0xB6)));
    private static readonly Brush TabText = Frz(new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79)));

    private static Brush Frz(Brush b) { b.Freeze(); return b; }

    // ---------------------------------------------------------------- Simgeler (24×24 çizim alanı)
    private static class Ico
    {
        public const string New = "M6,2 H14 L19,7 V22 H6 Z M14,2 V7 H19";
        public const string Open = "M2,6 H9 L11,8 H21 V19 H2 Z M2,11 H21";
        public const string Save = "M4,4 H17 L20,7 V20 H4 Z M8,4 V9 H15 V4 M7,20 V13 H17 V20";
        public const string SaveAs = "M4,4 H17 L20,7 V20 H4 Z M8,4 V9 H15 V4";
        public const string Import = "M6,2 H14 L19,7 V22 H6 Z M12,9 V18 M8.5,14.5 L12,18 L15.5,14.5";
        public const string Undo = "M9,5 L4,10 L9,15 M4,10 H15 A5,5 0 0 1 15,20 H11";
        public const string Redo = "M15,5 L20,10 L15,15 M20,10 H9 A5,5 0 0 0 9,20 H13";
        public const string Line = "M5,19 L19,5";
        public const string LineA = "M3.5,17.5 h3 v3 h-3 z M17.5,3.5 h3 v3 h-3 z";
        public const string Pline = "M3,19 L9,7 L15,15 L21,5";
        public const string Rect = "M4,6 H20 V18 H4 Z";
        public const string Circle = "M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12";
        public const string CircleA = "M11,12 h2 M12,11 v2";
        public const string Arc = "M4,18 A9,9 0 0 1 20,18";
        public const string Text = "M5,6 V4 H19 V6 M12,4 V20 M9,20 H15";
        public const string Move = "M12,3 V21 M3,12 H21 M12,3 L9,6 M12,3 L15,6 M12,21 L9,18 M12,21 L15,18 M3,12 L6,9 M3,12 L6,15 M21,12 L18,9 M21,12 L18,15";
        public const string Copy = "M4,8 H14 V20 H4 Z M9,8 V4 H20 V15 H14";
        public const string Rotate = "M19,13 A7,7 0 1 1 16.5,6.5 M17,2 V7 H12";
        public const string RotateCw = "M5,13 A7,7 0 1 0 7.5,6.5 M7,2 V7 H12";
        public const string Mirror = "M12,2 V22 M9,6 L3,18 H9 Z M15,6 L21,18 H15 Z";
        public const string Scale = "M4,20 H11 V13 H4 Z M4,13 V4 H20 V20 H11 M13,11 L20,4 M15,4 H20 V9";
        public const string Offset = "M3,16 L9,5 H21 M3,21 L11,9.5 H21";
        public const string Trim = "M4,20 L20,4 M4,4 L9.5,9.5 M14.5,14.5 L20,20";
        public const string Extend = "M3,18 H13 M20,4 V22 M10,15 L13,18 L10,21";
        public const string Fillet = "M4,21 V13 A9,9 0 0 1 13,4 H21";
        public const string Chamfer = "M4,21 V11 L11,4 H21";
        public const string Join = "M3,18 L9,8 L15,16 L21,6";
        public const string JoinA = "M7.5,6.5 h3 v3 h-3 z M13.5,14.5 h3 v3 h-3 z";
        public const string Explode = "M12,2 V7 M12,17 V22 M2,12 H7 M17,12 H22 M5,5 L8.5,8.5 M19,19 L15.5,15.5 M19,5 L15.5,8.5 M5,19 L8.5,15.5";
        public const string Erase = "M5,7 H19 M9,7 V4 H15 V7 M7,7 L8,21 H16 L17,7 M10,11 V17 M14,11 V17";
        public const string Group = "M4,4 H11 V11 H4 Z M13,13 H20 V20 H13 Z";
        public const string GroupA = "M2,2 H22 V22 H2 Z";
        public const string DimLin = "M4,8 V21 M20,8 V21 M4,12 H20 M4,12 L7,10 M4,12 L7,14 M20,12 L17,10 M20,12 L17,14";
        public const string DimAli = "M3,14 L13,2 M11,22 L21,10 M6,18 L17,5";
        public const string DimRad = "M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 M12,12 L18,6";
        public const string DimDia = "M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 M6.5,17.5 L17.5,6.5";
        public const string DimAng = "M3,20 H21 M3,20 L16,5 M12,20 A9,9 0 0 0 9.5,13.5";
        public const string Hatch = "M4,4 H20 V20 H4 Z";
        public const string HatchA = "M4,12 L12,4 M4,20 L20,4 M12,20 L20,12";
        public const string Dist = "M3,15 L15,3 L21,9 L9,21 Z M7,11 L9,13 M10,8 L12,10 M13,5 L15,7";
        public const string Zoom = "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M8,8 H16 V16 H8 Z";
        public const string Layers = "M12,3 L21,8 L12,13 L3,8 Z M3,12 L12,17 L21,12 M3,16 L12,21 L21,16";
        public const string Plus = "M18,15 V23 M14,19 H22";
        public const string ArrowIn = "M15,20 H23 M20,17 L23,20 L20,23";
        public const string Block = "M4,7 L12,3 L20,7 V17 L12,21 L4,17 Z M4,7 L12,11 L20,7 M12,11 V21";
        public const string Insert = "M6,11 L12,8 L18,11 V18 L12,21 L6,18 Z M6,11 L12,14 L18,11 M12,14 V21";
        public const string InsertA = "M12,1 V6.5 M9.5,4 L12,6.5 L14.5,4";
        public const string Library = "M4,4 H8 V20 H4 Z M10,4 H14 V20 H10 Z M16,5 L19,4.2 L22.5,19 L19.5,19.8 Z";
        public const string Batch = "M9,2 H19 V16 H9 Z M6,5 V19 H16 M3,8 V22 H13";
        public const string Pdf = "M6,2 H14 L19,7 V22 H6 Z M14,2 V7 H19 M9,12 H16 M9,15 H16 M9,18 H13";
        public const string Settings = "M4,6 H20 M4,12 H20 M4,18 H20";
        public const string SettingsA = "M8,4 V8 M15,10 V14 M10,16 V20";
        public const string Audit = "M4,10 A6,6 0 1 0 16,10 A6,6 0 1 0 4,10 M14.5,14.5 L21,21";
        public const string Help = "M9,9 A3,3 0 1 1 13,11.8 C12.3,12.1 12,12.6 12,13.5 V14.5 M12,18 V18.5";
        public const string HelpA = "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12";
        public const string Origin = "M12,2 V22 M2,12 H22";
        public const string OriginA = "M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12";
        public const string Align = "M3,21 H21 M3,21 V3 M7,17 L18,6";
        public const string AlignA = "M13,6 H18 V11";
        public const string MirX = "M2,12 H22 M6,10 L12,3 L18,10 Z M6,14 L12,21 L18,14 Z";
        public const string MirY = "M12,2 V22 M10,6 L3,12 L10,18 Z M14,6 L21,12 L14,18 Z";
        public const string Rot180 = "M5,12 A7,7 0 1 1 19,12 M16,9 L19,12 L22,9";
        public const string List = "M4,6 H6 M9,6 H20 M4,12 H6 M9,12 H20 M4,18 H6 M9,18 H20";
        public const string Close = "M5,5 L19,19 M19,5 L5,19";
        public const string Exit = "M14,4 H20 V20 H14 M4,12 H15 M11,8 L15,12 L11,16";
        public const string Recent = "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 M12,7 V12 L15.5,14";
        public const string Ungroup = "M4,4 H11 V11 H4 Z M13,13 H20 V20 H13 Z M14,4 L20,10 M20,4 L14,10";
    }

    private static FrameworkElement MkIcon(string data, string? accent, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(IconPath(data, IconInk));
        if (accent != null) canvas.Children.Add(IconPath(accent, IconAccent));
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }

    private static Shapes.Path IconPath(string data, Brush stroke) => new()
    {
        Data = System.Windows.Media.Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = 1.7,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    // ---------------------------------------------------------------- Yapı

    private readonly List<(Button Header, FrameworkElement Body)> _ribTabs = new();
    private Border? _ribBodyHost;
    private bool _ribCollapsed;
    private readonly List<(Button Btn, double Fx, double Fy)> _refButtons = new();
    private readonly List<TextBlock> _ribSizeTexts = new();
    private readonly List<ComboBox> _ribLayerCombos = new();
    private Button? _qaUndo, _qaRedo;
    private bool _ribUpdating;

    private Style RibStyle => (Style)FindResource("RibbonBtn");

    private void RibRun(string cmd)
    {
        _ = _editor.RunCommand(cmd);
        InputBox.Focus();
    }

    private void Act(Action a)
    {
        a();
        InputBox.Focus();
    }

    private static string Tip(string title, string? keys, string? body)
    {
        var s = title + (keys != null ? $"  ({keys})" : "");
        return body != null ? s + "\n" + body : s;
    }

    private Button LargeBtn(string label, string icon, string? accent, Action click, string? keys = null, string? tip = null)
    {
        var sp = new StackPanel();
        sp.Children.Add(MkIcon(icon, accent, 30));
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 66,
            Margin = new Thickness(0, 3, 0, 0),
            LineHeight = 12,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        });
        var b = new Button
        {
            Style = RibStyle,
            Content = sp,
            MinWidth = 50,
            Height = 70,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(5, 5, 5, 2),
            ToolTip = Tip(label.Replace("\n", " "), keys, tip)
        };
        b.Click += (_, _) => click();
        return b;
    }

    private Button SmallBtn(string label, string icon, string? accent, Action click, string? keys = null, string? tip = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(MkIcon(icon, accent, 16));
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(5, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Style = RibStyle,
            Content = sp,
            Height = 22,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(3, 1, 4, 1),
            ToolTip = Tip(label, keys, tip)
        };
        b.Click += (_, _) => click();
        return b;
    }

    private Button LargeCmd(string label, string icon, string? accent, string cmd, string? keys = null, string? tip = null) =>
        LargeBtn(label, icon, accent, () => RibRun(cmd), keys, tip);

    private Button SmallCmd(string label, string icon, string? accent, string cmd, string? keys = null, string? tip = null) =>
        SmallBtn(label, icon, accent, () => RibRun(cmd), keys, tip);

    /// <summary>Küçük düğmeleri 3'lü sütunlara dizer.</summary>
    private static Panel Stack3(params FrameworkElement[] items)
    {
        var wrap = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < items.Length; i += 3)
        {
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 2, 0) };
            for (int j = i; j < Math.Min(i + 3, items.Length); j++) col.Children.Add(items[j]);
            wrap.Children.Add(col);
        }
        return wrap;
    }

    private static FrameworkElement RibGroup(string title, params FrameworkElement[] content)
    {
        var dp = new DockPanel { Margin = new Thickness(4, 2, 4, 0) };
        var lbl = new TextBlock
        {
            Text = title,
            FontSize = 10.5,
            Foreground = RibLabel,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        DockPanel.SetDock(lbl, Dock.Bottom);
        dp.Children.Add(lbl);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        foreach (var c in content) row.Children.Add(c);
        dp.Children.Add(row);

        var outer = new StackPanel { Orientation = Orientation.Horizontal };
        outer.Children.Add(dp);
        outer.Children.Add(new Border { Width = 1, Background = RibLine, Margin = new Thickness(2, 6, 2, 6) });
        return outer;
    }

    private static FrameworkElement TabBody(params FrameworkElement[] groups)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var g in groups) sp.Children.Add(g);
        return new ScrollViewer
        {
            Content = sp,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
    }

    // ---------------------------------------------------------------- 9 noktalı referans seçici

    private FrameworkElement RefPicker()
    {
        const double W = 84, H = 60, D = 16;
        var canvas = new Canvas { Width = W + D, Height = H + D, Margin = new Thickness(6, 4, 6, 0) };
        var rect = new Shapes.Rectangle
        {
            Width = W,
            Height = H,
            Stroke = IconInk,
            StrokeThickness = 1.4,
            Fill = Frz(new SolidColorBrush(Color.FromRgb(0xE8, 0xEF, 0xF8)))
        };
        Canvas.SetLeft(rect, D / 2);
        Canvas.SetTop(rect, D / 2);
        canvas.Children.Add(rect);

        foreach (var fy in new[] { 1.0, 0.5, 0.0 })
            foreach (var fx in new[] { 0.0, 0.5, 1.0 })
            {
                double cfx = fx, cfy = fy;
                var dot = new Button
                {
                    Width = D,
                    Height = D,
                    Focusable = false,
                    Cursor = Cursors.Hand,
                    ToolTip = CadEditor.BoxRefName(fx, fy) + " → 0,0\nSeçim varsa seçili nesneler, yoksa tüm çizim taşınır.",
                    Template = DotTemplate()
                };
                dot.Click += (_, _) => Act(() => _ = _editor.MoveBoxPointToOrigin(cfx, cfy));
                Canvas.SetLeft(dot, fx * W);
                Canvas.SetTop(dot, (1 - fy) * H);
                canvas.Children.Add(dot);
                _refButtons.Add((dot, fx, fy));
            }
        return canvas;
    }

    private static ControlTemplate? _dotTemplate;

    /// <summary>Yuvarlak nokta düğmesi: Tag="on" iken dolu turuncu.</summary>
    private static ControlTemplate DotTemplate()
    {
        if (_dotTemplate != null) return _dotTemplate;
        var t = new ControlTemplate(typeof(Button));
        var el = new FrameworkElementFactory(typeof(Shapes.Ellipse), "E");
        el.SetValue(Shapes.Shape.FillProperty, Brushes.White);
        el.SetValue(Shapes.Shape.StrokeProperty, IconInk);
        el.SetValue(Shapes.Shape.StrokeThicknessProperty, 1.6);
        el.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        t.VisualTree = el;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Shapes.Shape.FillProperty, Frz(new SolidColorBrush(Color.FromRgb(0x9F, 0xC0, 0xE8))), "E"));
        hover.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0), "E"));
        var on = new Trigger { Property = FrameworkElement.TagProperty, Value = "on" };
        on.Setters.Add(new Setter(Shapes.Shape.FillProperty, IconAccent, "E"));
        on.Setters.Add(new Setter(Shapes.Shape.StrokeProperty, IconAccent, "E"));
        t.Triggers.Add(on);
        t.Triggers.Add(hover);
        t.Seal();
        _dotTemplate = t;
        return t;
    }

    private FrameworkElement SizeInfo()
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Foreground = TabText,
            Margin = new Thickness(6, 8, 6, 0),
            MinWidth = 120,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 170
        };
        _ribSizeTexts.Add(tb);
        return tb;
    }

    private FrameworkElement LayerCombo()
    {
        var cb = new ComboBox { Width = 150, Height = 22, Margin = new Thickness(2, 3, 2, 2), Focusable = false, ToolTip = "Geçerli katman (yeni çizimler bu katmana gider)" };
        cb.SelectionChanged += (_, _) =>
        {
            if (_ribUpdating || cb.SelectedItem is not string name) return;
            _doc.CurrentLayer = name;
            AppendHistory($"Geçerli katman: {name}");
            RebuildLayerPanel(true);
            InputBox.Focus();
        };
        _ribLayerCombos.Add(cb);
        return cb;
    }

    // ---------------------------------------------------------------- Kurulum

    private void BuildRibbon()
    {
        _ribTabs.Clear();
        _refButtons.Clear();
        _ribSizeTexts.Clear();
        _ribLayerCombos.Clear();

        // ---- Sekme içerikleri
        var home = TabBody(
            RibGroup("Çizim",
                LargeCmd("Çizgi", Ico.Line, Ico.LineA, "LINE", "L"),
                LargeCmd("Polyline", Ico.Pline, null, "PLINE", "PL"),
                Stack3(
                    SmallCmd("Dikdörtgen", Ico.Rect, null, "RECTANG", "REC"),
                    SmallCmd("Daire", Ico.Circle, Ico.CircleA, "CIRCLE", "C"),
                    SmallCmd("Yay", Ico.Arc, null, "ARC", "A", "Üç noktadan yay"),
                    SmallCmd("Yazı", Ico.Text, null, "TEXT", "DT"))),
            RibGroup("Değiştir",
                LargeCmd("Taşı", Ico.Move, null, "MOVE", "M"),
                Stack3(
                    SmallCmd("Kopyala", Ico.Copy, null, "COPY", "CO"),
                    SmallCmd("Döndür", Ico.Rotate, null, "ROTATE", "RO"),
                    SmallCmd("Aynala", Ico.Mirror, null, "MIRROR", "MI"),
                    SmallCmd("Ölçekle", Ico.Scale, null, "SCALE", "SC"),
                    SmallCmd("Ötele", Ico.Offset, null, "OFFSET", "O", "Paralel kopya"),
                    SmallCmd("Birleştir", Ico.Join, Ico.JoinA, "JOIN", "J", "Uç uca nesneleri polyline yapar"),
                    SmallCmd("Buda", Ico.Trim, null, "TRIM", "TR", "Kesme kenarları arasında kalan parçayı siler"),
                    SmallCmd("Uzat", Ico.Extend, null, "EXTEND", "EX", "Nesneyi sınıra kadar uzatır"),
                    SmallCmd("Patlat", Ico.Explode, null, "EXPLODE", "X", "Polyline, blok ve ölçüleri parçalar"),
                    SmallCmd("Yuvarla", Ico.Fillet, null, "FILLET", "F"),
                    SmallCmd("Pah", Ico.Chamfer, null, "CHAMFER", "CHA"),
                    SmallCmd("Sil", Ico.Erase, null, "ERASE", "E / Del"))),
            RibGroup("Referans → 0,0", RefPicker(), SizeInfo()),
            RibGroup("Blok",
                LargeCmd("Blok\nOluştur", Ico.Block, null, "BLOCK", "B", "Seçili nesnelerden blok yapar"),
                BlockInsertButton(large: true)),
            RibGroup("Katman",
                new StackPanel
                {
                    Children =
                    {
                        LayerCombo(),
                        SmallBtn("Yeni katman", Ico.Layers, Ico.Plus, () => Act(() => NewLayer_Click(this, new RoutedEventArgs())), null, "Yeni katman oluştur ve geçerli yap"),
                        SmallBtn("Seçileni taşı", Ico.Layers, Ico.ArrowIn, () => Act(() => MoveSelToLayer_Click(this, new RoutedEventArgs())), null, "Seçili nesneleri geçerli katmana taşır")
                    }
                }));

        var annotate = TabBody(
            RibGroup("Ölçü",
                LargeCmd("Doğrusal", Ico.DimLin, null, "DIMLINEAR", "DLI", "Yatay / dikey ölçü"),
                LargeCmd("Paralel", Ico.DimAli, null, "DIMALIGNED", "DAL"),
                Stack3(
                    SmallCmd("Yarıçap", Ico.DimRad, null, "DIMRADIUS", "DRA"),
                    SmallCmd("Çap", Ico.DimDia, null, "DIMDIAMETER", "DDI"),
                    SmallCmd("Açı", Ico.DimAng, null, "DIMANGULAR", "DAN"))),
            RibGroup("Tarama",
                LargeCmd("Tarama", Ico.Hatch, Ico.HatchA, "HATCH", "H", "Kapalı alanın içine tıklayın")),
            RibGroup("Yazı",
                LargeCmd("Yazı", Ico.Text, null, "TEXT", "DT")),
            RibGroup("Sorgula",
                Stack3(
                    SmallCmd("Mesafe", Ico.Dist, null, "DIST", "DI"),
                    SmallCmd("Nokta", Ico.Origin, Ico.CircleA, "ID", "ID", "Nokta koordinatı"),
                    SmallCmd("Liste", Ico.List, null, "LIST", "LI", "Seçili nesne bilgileri"))));

        var profile = TabBody(
            RibGroup("Referans noktası → 0,0", RefPicker(), SizeInfo()),
            RibGroup("Konum",
                LargeCmd("Nokta\n→ 0,0", Ico.Origin, Ico.OriginA, "ORIGIN", "OR", "Tıkladığınız noktayı 0,0'a taşır"),
                LargeCmd("Hizala\n(2 nokta)", Ico.Align, Ico.AlignA, "ALIGN", "AL", "1. nokta 0,0'a, 2. nokta +X yönüne")),
            RibGroup("Döndür / Aynala (0,0 etrafında)",
                LargeCmd("+90°", Ico.Rotate, null, "ROT90", "R90"),
                LargeCmd("−90°", Ico.RotateCw, null, "ROTM90", "R-90"),
                Stack3(
                    SmallCmd("180°", Ico.Rot180, null, "ROT180", "R180"),
                    SmallCmd("Aynala X", Ico.MirX, null, "MIRRORX", "MX", "y → −y"),
                    SmallCmd("Aynala Y", Ico.MirY, null, "MIRRORY", "MY", "x → −x"))),
            RibGroup("Çıktı",
                LargeBtn("PDF\nRapor", Ico.Pdf, null, () => Act(CreateReport), "Ctrl+P", "Seçili profil (ya da tüm çizim) için PDF"),
                LargeBtn("Kütüphane", Ico.Library, null, () => Act(() => OpenLibrary()), "Ctrl+L"),
                LargeBtn("Toplu\nİşlem", Ico.Batch, null, () => Act(() => Batch_Click(this, new RoutedEventArgs())), null, "Çok sayıda dosyayı konumlandır / dönüştür")));

        var layerBlock = TabBody(
            RibGroup("Katmanlar",
                LargeBtn("Yeni\nKatman", Ico.Layers, Ico.Plus, () => Act(() => NewLayer_Click(this, new RoutedEventArgs())), null, "Yeni katman oluştur ve geçerli yap"),
                LargeBtn("Seçileni\nKatmana Taşı", Ico.Layers, Ico.ArrowIn, () => Act(() => MoveSelToLayer_Click(this, new RoutedEventArgs())), null, "Seçili nesneleri geçerli katmana taşır"),
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Geçerli katman:", FontSize = 11, Foreground = RibLabel, Margin = new Thickness(3, 4, 0, 0) },
                        LayerCombo(),
                        new TextBlock { Text = "Renk / görünürlük / ad: sağ paneldeki\nKatmanlar listesi", FontSize = 10, Foreground = RibLabel, Margin = new Thickness(3, 2, 0, 0) }
                    }
                }),
            RibGroup("Bloklar",
                LargeCmd("Blok\nOluştur", Ico.Block, null, "BLOCK", "B", "Seçili nesnelerden blok yapar; temel nokta varsayılan olarak sol alt köşe"),
                BlockInsertButton(large: true),
                LargeCmd("Patlat", Ico.Explode, null, "EXPLODE", "X", "Bloğu / polyline'ı parçalar")),
            RibGroup("Gruplar",
                LargeCmd("Grupla", Ico.Group, Ico.GroupA, "GROUP", "G", "Ctrl+tık gruptan tek nesne seçer"),
                LargeCmd("Grubu\nÇöz", Ico.Ungroup, null, "UNGROUP", "UG")));

        var tools = TabBody(
            RibGroup("Görünüm",
                LargeCmd("Tümünü\nGöster", Ico.Zoom, null, "ZOOMEXTENTS", "ZE", "Orta tuşa çift tık da aynı işi yapar")),
            RibGroup("Araçlar",
                LargeBtn("Kütüphane", Ico.Library, null, () => Act(() => OpenLibrary()), "Ctrl+L"),
                LargeBtn("Toplu\nİşlem", Ico.Batch, null, () => Act(() => Batch_Click(this, new RoutedEventArgs()))),
                LargeBtn("PDF\nRapor", Ico.Pdf, null, () => Act(CreateReport), "Ctrl+P"),
                LargeBtn("Çizimi\nDenetle", Ico.Audit, null, () => Act(() => Audit_Click(this, new RoutedEventArgs())), null, "Çok uzak, çok büyük, sıfır boylu ve kopya nesneleri bulur")),
            RibGroup("Ayarlar",
                LargeBtn("Ayarlar", Ico.Settings, Ico.SettingsA, () => Act(() => Settings_Click(this, new RoutedEventArgs())), null, "Ayarlar, kısa adlar ve klavye kısayolları"),
                LargeCmd("Komut\nListesi", Ico.Help, Ico.HelpA, "HELP", "YARDIM")));

        // ---- Üst şerit çubuğu
        var bar = new DockPanel { Background = RibBar, Height = 32 };

        var fileBtn = new Button
        {
            Style = (Style)FindResource("RibbonTab"),
            Content = new TextBlock { Text = "Dosya", FontWeight = FontWeights.SemiBold },
            Background = AccentBlue,
            Margin = new Thickness(4, 4, 6, 0)
        };
        fileBtn.Click += (_, _) => ShowFileMenu(fileBtn);
        DockPanel.SetDock(fileBtn, Dock.Left);
        bar.Children.Add(fileBtn);

        // Hızlı erişim (sağda)
        var qa = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        qa.Children.Add(QuickBtn(Ico.New, "Yeni sekme (Ctrl+N)", () => NewTab()));
        qa.Children.Add(QuickBtn(Ico.Open, "Aç (Ctrl+O)", () => Open_Click(this, new RoutedEventArgs())));
        qa.Children.Add(QuickBtn(Ico.Save, "Kaydet (Ctrl+S)", () => Save_Click(this, new RoutedEventArgs())));
        qa.Children.Add(new Border { Width = 1, Background = Frz(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))), Margin = new Thickness(4, 6, 4, 6) });
        qa.Children.Add(_qaUndo = QuickBtn(Ico.Undo, "Geri al (Ctrl+Z)", () => RibRun("UNDO")));
        qa.Children.Add(_qaRedo = QuickBtn(Ico.Redo, "Yinele (Ctrl+Y)", () => RibRun("REDO")));
        DockPanel.SetDock(qa, Dock.Right);
        bar.Children.Add(qa);

        var tabsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(tabsPanel);

        _ribBodyHost = new Border
        {
            Background = RibBody,
            BorderBrush = RibLine,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 96,
            Padding = new Thickness(2, 2, 2, 0)
        };

        void AddTab(string title, FrameworkElement body)
        {
            var h = new Button { Style = (Style)FindResource("RibbonTab"), Content = title };
            int idx = _ribTabs.Count;
            h.Click += (_, _) => SelectRibbonTab(idx, fromUser: true);
            h.MouseDoubleClick += (_, _) => ToggleRibbon();
            tabsPanel.Children.Add(h);
            _ribTabs.Add((h, body));
        }

        AddTab("Giriş", home);
        AddTab("Açıklama", annotate);
        AddTab("Profil", profile);
        AddTab("Katman ve Blok", layerBlock);
        AddTab("Görünüm ve Araçlar", tools);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_ribBodyHost);
        RibbonHost.Child = root;

        SelectRibbonTab(0, fromUser: false);
    }

    private Button QuickBtn(string icon, string tip, Action a)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(IconPath(icon, Brushes.White));
        var b = new Button
        {
            Style = RibStyle,
            Content = new Viewbox { Width = 16, Height = 16, Child = canvas },
            Width = 28,
            Height = 26,
            Padding = new Thickness(2),
            ToolTip = tip
        };
        b.Click += (_, _) => Act(a);
        return b;
    }

    private void SelectRibbonTab(int idx, bool fromUser)
    {
        if (_ribBodyHost == null) return;
        if (fromUser && _ribCollapsed)
        {
            _ribCollapsed = false;
            _ribBodyHost.Visibility = Visibility.Visible;
        }
        for (int i = 0; i < _ribTabs.Count; i++)
        {
            var (h, _) = _ribTabs[i];
            bool sel = i == idx;
            h.Background = sel ? RibBody : Brushes.Transparent;
            h.Foreground = sel ? TabText : Brushes.White;
            h.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal;
        }
        _ribBodyHost.Child = _ribTabs[idx].Body;
        if (fromUser) InputBox.Focus();
    }

    private void ToggleRibbon()
    {
        if (_ribBodyHost == null) return;
        _ribCollapsed = !_ribCollapsed;
        _ribBodyHost.Visibility = _ribCollapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private Button BlockInsertButton(bool large)
    {
        Button b = null!;
        b = LargeBtn("Blok\nEkle", Ico.Insert, Ico.InsertA, () => ShowBlockMenu(b), "I", "Çizimdeki bir bloğu ekler (Dondur90 / Aynala / Olcek seçenekleri)");
        return b;
    }

    private void ShowBlockMenu(Button owner)
    {
        var m = new ContextMenu { PlacementTarget = owner, Placement = PlacementMode.Bottom };
        var names = _doc.Blocks.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (names.Count == 0)
        {
            m.Items.Add(new MenuItem { Header = "(Çizimde blok yok — önce Blok Oluştur)", IsEnabled = false });
        }
        foreach (var n in names)
        {
            string name = n;
            var bd = _doc.Blocks[name];
            var mi = new MenuItem
            {
                Header = $"{name}   ({bd.Entities.Count} nesne, {_doc.CountBlockRefs(name)} kopya)",
                Icon = MkIcon(Ico.Block, null, 16)
            };
            mi.Click += (_, _) => Act(() => _ = _editor.InsertBlock(name));
            m.Items.Add(mi);
        }
        m.IsOpen = true;
    }

    private MenuItem FileItem(string header, string? icon, Action a, string? gesture = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        if (icon != null) mi.Icon = MkIcon(icon, null, 16);
        mi.Click += (_, _) => Act(a);
        return mi;
    }

    private void ShowFileMenu(Button owner)
    {
        var e = new RoutedEventArgs();
        var m = new ContextMenu { PlacementTarget = owner, Placement = PlacementMode.Bottom, MinWidth = 260 };
        m.Items.Add(FileItem("Yeni (yeni sekme)", Ico.New, () => NewTab(), "Ctrl+N"));
        m.Items.Add(FileItem("Aç (DWG/DXF)...", Ico.Open, () => Open_Click(this, e), "Ctrl+O"));
        m.Items.Add(FileItem("İçe Aktar (mevcut çizime ekle)...", Ico.Import, () => Import_Click(this, e)));
        var recent = new MenuItem { Header = "Son Açılanlar", Icon = MkIcon(Ico.Recent, null, 16) };
        FillRecentMenu(recent);
        m.Items.Add(recent);
        m.Items.Add(new Separator());
        m.Items.Add(FileItem("Kaydet", Ico.Save, () => Save_Click(this, e), "Ctrl+S"));
        m.Items.Add(FileItem("Farklı Kaydet...", Ico.SaveAs, () => SaveAs_Click(this, e)));
        m.Items.Add(FileItem("Seçileni Dışa Aktar...", Ico.Import, () => ExportSel_Click(this, e)));
        m.Items.Add(new Separator());
        m.Items.Add(FileItem("PDF Profil Raporu...", Ico.Pdf, CreateReport, "Ctrl+P"));
        m.Items.Add(FileItem("Toplu İşlem...", Ico.Batch, () => Batch_Click(this, e)));
        m.Items.Add(new Separator());
        m.Items.Add(FileItem("Sekmeyi Kapat", Ico.Close, () => CloseTab_Click(this, e), "Ctrl+W"));
        m.Items.Add(FileItem("Çıkış", Ico.Exit, () => Exit_Click(this, e)));
        m.IsOpen = true;
    }

    // ---------------------------------------------------------------- Durum güncelleme

    private void UpdateRibbonState()
    {
        if (_ribBodyHost == null || _doc == null || _editor == null) return;
        if (_qaUndo != null) _qaUndo.IsEnabled = _doc.CanUndo;
        if (_qaRedo != null) _qaRedo.IsEnabled = _doc.CanRedo;

        var (lfx, lfy) = _editor.LastBoxRef;
        foreach (var (btn, fx, fy) in _refButtons)
            btn.Tag = fx == lfx && fy == lfy ? "on" : null;

        bool hasSel = _doc.Selection.Count > 0;
        var b = hasSel ? _doc.Extents(_doc.ExpandGroups(_doc.Selection)) : _doc.Extents();
        string txt = b.IsEmpty
            ? "Çizim boş"
            : $"{(hasSel ? "Seçim" : "Tüm çizim")}\nG {Vec2.Format(Math.Round(b.Width, 3))}\nY {Vec2.Format(Math.Round(b.Height, 3))}\nSol alt {Vec2.Format(Math.Round(b.MinX, 3))}, {Vec2.Format(Math.Round(b.MinY, 3))}";
        foreach (var tb in _ribSizeTexts) tb.Text = txt;

        UpdateLayerCombos();
    }

    private void UpdateLayerCombos()
    {
        if (_doc == null) return;
        _ribUpdating = true;
        try
        {
            var names = _doc.Layers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var cb in _ribLayerCombos)
            {
                var cur = cb.Items.Cast<string>().ToList();
                if (!cur.SequenceEqual(names))
                {
                    cb.Items.Clear();
                    foreach (var n in names) cb.Items.Add(n);
                }
                cb.SelectedItem = names.FirstOrDefault(n => string.Equals(n, _doc.CurrentLayer, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            _ribUpdating = false;
        }
    }
}
