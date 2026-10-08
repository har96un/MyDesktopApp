using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using MyDesktopApp.Editor;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

/// <summary>Klavye kısayolu ayrıştırma yardımcıları.</summary>
public static class ShortcutUtil
{
    public static bool TryParse(string text, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            gesture = new KeyGestureConverter().ConvertFromInvariantString(text.Trim()) as KeyGesture;
            return gesture != null;
        }
        catch
        {
            return false;
        }
    }

    public static string Normalize(KeyGesture g) =>
        new KeyGestureConverter().ConvertToInvariantString(g) ?? "";

    public static bool Matches(KeyGesture g, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return g.Key == key && g.Modifiers == Keyboard.Modifiers;
    }
}

/// <summary>Kısayol tablosu satırı.</summary>
public sealed class ShortcutRow
{
    public string Command { get; set; } = "";
    public string Description { get; set; } = "";
    public string BuiltIn { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string Key { get; set; } = "";
}

/// <summary>Ayarlar penceresi: genel, ölçü/tarama, kısayollar.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly CadEditor _editor;

    private readonly TextBox _company = Box();
    private readonly TextBox _logo = Box();
    private readonly TextBox _author = Box();
    private readonly TextBox _library = Box();
    private readonly TextBox _autosave = Box();
    private readonly ComboBox _saveFmt = new() { MinWidth = 260 };
    private readonly TextBox _dimText = Box();
    private readonly TextBox _dimArrow = Box();
    private readonly TextBox _dimDec = Box();
    private readonly ComboBox _hatch = new() { MinWidth = 160 };
    private readonly TextBox _hatchScale = Box();
    private readonly CheckBox _inch = new() { Content = "İnç modu (uzunluklar inç gösterilir ve girilir)", Margin = new Thickness(0, 2, 0, 8) };
    private readonly ComboBox _inchStyle = new() { MinWidth = 200 };
    private readonly ComboBox _inchDen = new() { MinWidth = 120 };
    private readonly ObservableCollection<ShortcutRow> _rows = new();

    private static TextBox Box() => new() { Padding = new Thickness(3), MinWidth = 120 };

    public SettingsWindow(Window owner, AppSettings settings, CadEditor editor)
    {
        Owner = owner;
        _s = settings;
        _editor = editor;
        Title = "Ayarlar";
        Width = 720;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var tabs = new TabControl { Margin = new Thickness(8) };
        tabs.Items.Add(new TabItem { Header = "Genel", Content = GeneralTab() });
        tabs.Items.Add(new TabItem { Header = "Ölçü ve Tarama", Content = DimTab() });
        tabs.Items.Add(new TabItem { Header = "Birim", Content = UnitTab() });
        tabs.Items.Add(new TabItem { Header = "Komut Kısa Adları ve Kısayollar", Content = ShortcutTab() });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        var ok = new Button { Content = "Tamam", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { if (Apply()) DialogResult = true; };
        var cancel = new Button { Content = "İptal", Width = 90, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;
    }

    private static FrameworkElement Field(string label, FrameworkElement ctl, Action? browse = null, string? hint = null)
    {
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var l = new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        d.Children.Add(l);
        if (browse != null)
        {
            var b = new Button { Content = "Gözat...", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
            b.Click += (_, _) => browse();
            DockPanel.SetDock(b, Dock.Right);
            d.Children.Add(b);
        }
        if (hint != null)
        {
            var h = new TextBlock { Text = hint, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(h, Dock.Right);
            d.Children.Add(h);
        }
        d.Children.Add(ctl);
        return d;
    }

    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private FrameworkElement GeneralTab()
    {
        _company.Text = _s.CompanyName;
        _logo.Text = _s.LogoPath;
        _author.Text = _s.ReportAuthor;
        _library.Text = _s.EffectiveLibraryFolder;
        _autosave.Text = _s.AutosaveMinutes.ToString(CultureInfo.InvariantCulture);
        var p = new StackPanel { Margin = new Thickness(12) };
        p.Children.Add(Field("Firma adı (rapor başlığı):", _company));
        p.Children.Add(Field("Logo (PNG/JPG):", _logo, () =>
        {
            var dlg = new OpenFileDialog { Filter = "Resim (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Title = "Logo Seç" };
            if (dlg.ShowDialog(this) == true) _logo.Text = dlg.FileName;
        }));
        p.Children.Add(Field("Raporu hazırlayan:", _author));
        p.Children.Add(Field("Profil kütüphanesi klasörü:", _library, () =>
        {
            var dlg = new OpenFolderDialog { Title = "Kütüphane Klasörü" };
            if (dlg.ShowDialog(this) == true) _library.Text = dlg.FolderName;
        }));
        p.Children.Add(Field("Otomatik kayıt aralığı (dk):", _autosave, hint: "0 = kapalı"));
        foreach (var f in MyDesktopApp.IO.CadFileIO.Formats) _saveFmt.Items.Add(f.Label + " (" + f.Ext + ")");
        _saveFmt.SelectedIndex = Math.Max(0, Array.FindIndex(MyDesktopApp.IO.CadFileIO.Formats, f => f.Id == _s.LastSaveFormat));
        p.Children.Add(Field("Varsayılan kayıt biçimi:", _saveFmt));
        p.Children.Add(new TextBlock
        {
            Text = "Otomatik kayıt, kaydedilmemiş değişiklikleri %AppData%\\ProfilCAD\\autosave klasörüne yazar. " +
                   "Program beklenmedik şekilde kapanırsa bir sonraki açılışta kurtarma önerilir.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 0)
        });
        return p;
    }

    private FrameworkElement DimTab()
    {
        _dimText.Text = N(_s.DimTextHeight);
        _dimArrow.Text = N(_s.DimArrowSize);
        _dimDec.Text = _s.DimDecimals.ToString(CultureInfo.InvariantCulture);
        foreach (var pat in HatchEntity.Patterns) _hatch.Items.Add(pat);
        _hatch.SelectedItem = HatchEntity.Patterns.FirstOrDefault(x => string.Equals(x, _s.HatchPattern, StringComparison.OrdinalIgnoreCase)) ?? "ANSI31";
        _hatchScale.Text = N(_s.HatchScale);
        var p = new StackPanel { Margin = new Thickness(12) };
        p.Children.Add(Field("Ölçü yazı yüksekliği:", _dimText));
        p.Children.Add(Field("Ok boyu:", _dimArrow));
        p.Children.Add(Field("Ondalık basamak:", _dimDec));
        p.Children.Add(Field("Varsayılan tarama deseni:", _hatch));
        p.Children.Add(Field("Varsayılan tarama ölçeği:", _hatchScale));
        p.Children.Add(new TextBlock
        {
            Text = "Bu değerler yeni ölçü ve taramalar için kullanılır. Mevcut nesneleri Özellikler panelinden değiştirebilirsiniz.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 0)
        });
        return p;
    }

    private static readonly int[] Denominators = { 2, 4, 8, 16, 32, 64, 128 };

    private FrameworkElement UnitTab()
    {
        _inch.IsChecked = _s.InchMode;
        _inchStyle.Items.Add("Ondalık (1.375\")");
        _inchStyle.Items.Add("Kesirli (1 3/8\")");
        _inchStyle.SelectedIndex = _s.InchFractional ? 1 : 0;
        foreach (var d in Denominators) _inchDen.Items.Add("1/" + d);
        _inchDen.SelectedIndex = Math.Max(0, Array.IndexOf(Denominators, _s.InchDenominator));
        _inchDen.IsEnabled = _s.InchFractional;
        _inchStyle.SelectionChanged += (_, _) => _inchDen.IsEnabled = _inchStyle.SelectedIndex == 1;
        var p = new StackPanel { Margin = new Thickness(12) };
        p.Children.Add(_inch);
        p.Children.Add(Field("İnç yazımı:", _inchStyle));
        p.Children.Add(Field("Kesir hassasiyeti:", _inchDen));
        p.Children.Add(new TextBlock
        {
            Text = "Çizim her zaman milimetre olarak saklanır ve kaydedilir; inç modu yalnızca ekranda gösterilen ve " +
                   "komut satırına yazılan uzunlukları değiştirir (1\" = 25,4 mm). Açılar ve ölçek katsayıları birimsizdir.\n\n" +
                   "Girişte kesir de yazılabilir: 3/8 veya 1-3/8 (komut satırında boşluk Enter sayıldığından tam + kesir tire ile yazılır; Özellikler panelinde 1 3/8 de olur). " +
                   "Tek bir değeri diğer birimde girmek için sonuna birim ekleyin: 25mm, 2\" veya 2in.\n\n" +
                   "İnç modu durum çubuğundaki İNÇ düğmesiyle de açılıp kapatılabilir. Ölçü/tarama varsayılanları mm cinsindendir.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 0)
        });
        return p;
    }

    private FrameworkElement ShortcutTab()
    {
        foreach (var c in _editor.Commands.Values.OrderBy(c => c.Name))
        {
            _rows.Add(new ShortcutRow
            {
                Command = c.Name,
                Description = c.Description,
                BuiltIn = string.Join(", ", c.Aliases),
                Aliases = string.Join(", ", _s.Aliases.Where(kv => string.Equals(kv.Value, c.Name, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key)),
                Key = string.Join(", ", _s.Shortcuts.Where(kv => string.Equals(kv.Value, c.Name, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key))
            });
        }
        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Komut", Binding = new Binding(nameof(ShortcutRow.Command)), IsReadOnly = true, Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Yerleşik kısa adlar", Binding = new Binding(nameof(ShortcutRow.BuiltIn)), IsReadOnly = true, Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Ek kısa adlar", Binding = new Binding(nameof(ShortcutRow.Aliases)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Klavye kısayolu", Binding = new Binding(nameof(ShortcutRow.Key)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Açıklama", Binding = new Binding(nameof(ShortcutRow.Description)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var d = new DockPanel { Margin = new Thickness(8) };
        var info = new TextBlock
        {
            Text = "Ek kısa adlar: komut satırına yazılacak adlar (virgülle ayırın, ör. \"KES, BD\"). " +
                   "Klavye kısayolu: ör. \"Ctrl+Shift+T\", \"Alt+F\", \"F9\". Harf tuşları tek başına kullanılamaz (komut satırına yazılır).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        DockPanel.SetDock(info, Dock.Top);
        d.Children.Add(info);
        d.Children.Add(grid);
        return d;
    }

    private static bool TryNum(TextBox t, out double v) =>
        double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private bool Fail(string msg)
    {
        MessageBox.Show(this, msg, "Ayarlar", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool Apply()
    {
        if (!int.TryParse(_autosave.Text.Trim(), out int autosave) || autosave < 0) return Fail("Otomatik kayıt aralığı 0 veya pozitif tam sayı olmalı.");
        if (!TryNum(_dimText, out double dt) || dt <= 0) return Fail("Ölçü yazı yüksekliği pozitif olmalı.");
        if (!TryNum(_dimArrow, out double da) || da <= 0) return Fail("Ok boyu pozitif olmalı.");
        if (!int.TryParse(_dimDec.Text.Trim(), out int dd) || dd < 0 || dd > 8) return Fail("Ondalık basamak 0-8 arası olmalı.");
        if (!TryNum(_hatchScale, out double hs) || hs <= 0) return Fail("Tarama ölçeği pozitif olmalı.");
        if (_logo.Text.Trim().Length > 0 && !File.Exists(_logo.Text.Trim())) return Fail("Logo dosyası bulunamadı.");

        // Kısa adlar ve kısayollar
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _rows)
        {
            foreach (var a in (r.Aliases ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = CadEditor.Fold(a);
                if (_editor.IsBuiltinAlias(key) || _editor.Commands.ContainsKey(key)) return Fail($"\"{a}\" zaten yerleşik bir komut/kısa ad.");
                if (aliases.TryGetValue(key, out var other) && other != r.Command) return Fail($"\"{a}\" iki komuta birden atanmış ({other}, {r.Command}).");
                aliases[key] = r.Command;
            }
            foreach (var k in (r.Key ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ShortcutUtil.TryParse(k, out var g) || g == null) return Fail($"\"{k.Trim()}\" geçerli bir klavye kısayolu değil ({r.Command}).");
                if (g.Key is Key.Escape or Key.Enter or Key.Space) return Fail($"{k.Trim()} ayrılmış bir tuştur.");
                string norm = ShortcutUtil.Normalize(g);
                if (keys.TryGetValue(norm, out var other) && other != r.Command) return Fail($"{norm} iki komuta birden atanmış ({other}, {r.Command}).");
                keys[norm] = r.Command;
            }
        }

        _s.CompanyName = _company.Text.Trim();
        _s.LogoPath = _logo.Text.Trim();
        _s.ReportAuthor = _author.Text.Trim();
        _s.LibraryFolder = _library.Text.Trim();
        _s.AutosaveMinutes = autosave;
        _s.LastSaveFormat = MyDesktopApp.IO.CadFileIO.Formats[Math.Max(0, _saveFmt.SelectedIndex)].Id;
        _s.DimTextHeight = dt;
        _s.DimArrowSize = da;
        _s.DimDecimals = dd;
        _s.HatchPattern = _hatch.SelectedItem as string ?? "ANSI31";
        _s.HatchScale = hs;
        _s.InchMode = _inch.IsChecked == true;
        _s.InchFractional = _inchStyle.SelectedIndex == 1;
        _s.InchDenominator = Denominators[Math.Max(0, _inchDen.SelectedIndex)];
        _s.Aliases = aliases;
        _s.Shortcuts = keys;
        _s.Save();
        return true;
    }
}
