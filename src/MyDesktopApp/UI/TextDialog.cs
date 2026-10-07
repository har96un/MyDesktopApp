using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyDesktopApp.Editor;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

/// <summary>Metin ekleme / düzenleme penceresi: çok satırlı metin, yükseklik, açı, 9 noktalı hizalama.</summary>
public sealed class TextDialog : Window
{
    private readonly TextBox _text;
    private readonly TextBox _height;
    private readonly TextBox _angle;
    private readonly ToggleButton[] _align = new ToggleButton[9];
    private TextAlign _alignValue;

    public TextSpec? Result { get; private set; }

    public static readonly string[] AlignNames =
    {
        "Sol alt", "Orta alt", "Sağ alt",
        "Sol orta", "Merkez", "Sağ orta",
        "Sol üst", "Orta üst", "Sağ üst"
    };

    public TextDialog(Window owner, TextSpec init, string title)
    {
        Owner = owner;
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _alignValue = init.Align;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock { Text = "Metin (Enter: yeni satır)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _text = new TextBox
        {
            Text = init.Value,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 120,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Padding = new Thickness(4)
        };
        root.Children.Add(_text);

        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        _height = NumBox(init.Height);
        _angle = NumBox(init.RotationDeg);
        left.Children.Add(Row("Yazı yüksekliği:", _height));
        left.Children.Add(Row("Açı (°):", _angle));
        var quick = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var a in new[] { 0, 90, 180, 270 })
        {
            var b = new Button { Content = a + "°", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
            b.Click += (_, _) => _angle.Text = a.ToString(CultureInfo.InvariantCulture);
            quick.Children.Add(b);
        }
        left.Children.Add(quick);
        left.Children.Add(new TextBlock
        {
            Text = "Yazı, konum için tıkladığınız noktaya seçili hizalama noktasından yerleşir.",
            Foreground = Brushes.DimGray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 12, 0)
        });
        grid.Children.Add(left);

        // 9 noktalı hizalama
        var alignPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        alignPanel.Children.Add(new TextBlock { Text = "Hizalama", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        var ag = new UniformGrid { Rows = 3, Columns = 3, Width = 120, Height = 96, Margin = new Thickness(0, 4, 0, 0) };
        for (int row = 2; row >= 0; row--)
            for (int col = 0; col < 3; col++)
            {
                int idx = row * 3 + col;
                var tb = new ToggleButton
                {
                    Margin = new Thickness(2),
                    Focusable = false,
                    ToolTip = AlignNames[idx],
                    Content = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Color.FromRgb(0x27, 0x4B, 0x73)) },
                    IsChecked = idx == (int)_alignValue
                };
                tb.Click += (_, _) => SetAlign((TextAlign)idx);
                _align[idx] = tb;
                ag.Children.Add(tb);
            }
        alignPanel.Children.Add(ag);
        var alignLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.DimGray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        alignPanel.Children.Add(alignLabel);
        Grid.SetColumn(alignPanel, 1);
        grid.Children.Add(alignPanel);
        root.Children.Add(grid);

        void UpdateLabel() => alignLabel.Text = AlignNames[(int)_alignValue];
        UpdateLabel();
        _alignChanged = UpdateLabel;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Tamam", Padding = new Thickness(18, 3, 18, 3), ToolTip = "Ctrl+Enter" };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "İptal", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; Accept(); }
        };
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }

    private Action? _alignChanged;

    private void SetAlign(TextAlign a)
    {
        _alignValue = a;
        for (int i = 0; i < 9; i++) _align[i].IsChecked = i == (int)a;
        _alignChanged?.Invoke();
    }

    private static TextBox NumBox(double v) => new()
    {
        Text = v.ToString("0.###", CultureInfo.InvariantCulture),
        Width = 90,
        Padding = new Thickness(3, 1, 3, 1),
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static DockPanel Row(string label, FrameworkElement ctl)
    {
        var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var l = new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        d.Children.Add(l);
        d.Children.Add(ctl);
        return d;
    }

    private static bool Parse(string s, out double v) =>
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void Accept()
    {
        string text = _text.Text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
        if (text.Length == 0) { MessageBox.Show(this, "Metin boş olamaz.", Title); return; }
        if (!Parse(_height.Text, out double h) || h <= 0) { MessageBox.Show(this, "Yazı yüksekliği pozitif bir sayı olmalı.", Title); return; }
        if (!Parse(_angle.Text, out double a)) { MessageBox.Show(this, "Açı geçerli bir sayı olmalı.", Title); return; }
        Result = new TextSpec(text, h, a, _alignValue);
        DialogResult = true;
    }

    /// <summary>Pencereyi açar; iptalde null.</summary>
    public static TextSpec? Show(Window owner, TextSpec init, string title)
    {
        var d = new TextDialog(owner, init, title);
        return d.ShowDialog() == true ? d.Result : null;
    }
}
