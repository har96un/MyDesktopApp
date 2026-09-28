using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MyDesktopApp.Geometry;
using MyDesktopApp.IO;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

/// <summary>Toplu işlem: çok sayıda DWG/DXF'i konumlandırır, döndürür, aynalar, farklı formatta kaydeder, rapor üretir.</summary>
public sealed class BatchWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ListBox _files = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBox _outDir = new() { Padding = new Thickness(3) };
    private readonly ComboBox _refPoint = new();
    private readonly ComboBox _rotate = new();
    private readonly ComboBox _mirror = new();
    private readonly ComboBox _format = new();
    private readonly TextBox _scale = new() { Text = "1", Width = 70, Padding = new Thickness(2) };
    private readonly TextBox _suffix = new() { Text = "", Width = 110, Padding = new Thickness(2) };
    private readonly CheckBox _principal = new() { Content = "Asal eksenlere hizala (döndürmeden önce)" };
    private readonly CheckBox _pdf = new() { Content = "Her dosya için PDF kesit raporu" };
    private readonly CheckBox _csv = new() { Content = "Kesit özellikleri özet tablosu (CSV)", IsChecked = true };
    private readonly CheckBox _saveDrawing = new() { Content = "Dönüştürülmüş çizimi kaydet", IsChecked = true };
    private readonly ProgressBar _progress = new() { Height = 14, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBox _log = new() { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 11, Height = 130, TextWrapping = TextWrapping.NoWrap };
    private readonly Button _run = new() { Content = "Başlat", Padding = new Thickness(18, 3, 18, 3), IsDefault = true };
    private bool _busy;

    public BatchWindow(Window owner, AppSettings settings)
    {
        Owner = owner;
        _settings = settings;
        Title = "Toplu İşlem";
        Width = 640;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        foreach (var s in new[] { "Değiştirme", "Ağırlık merkezi → 0,0", "Sol alt köşe → 0,0", "Kutu merkezi → 0,0" }) _refPoint.Items.Add(s);
        _refPoint.SelectedIndex = 1;
        foreach (var s in new[] { "0°", "+90°", "180°", "−90° (270°)" }) _rotate.Items.Add(s);
        _rotate.SelectedIndex = 0;
        foreach (var s in new[] { "Yok", "X eksenine göre (y → −y)", "Y eksenine göre (x → −x)" }) _mirror.Items.Add(s);
        _mirror.SelectedIndex = 0;
        foreach (var s in new[] { "DXF", "DWG" }) _format.Items.Add(s);
        _format.SelectedIndex = 0;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(Header("1. Dosyalar"));
        var fileButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        fileButtons.Children.Add(Btn("Dosya Ekle...", AddFiles));
        fileButtons.Children.Add(Btn("Klasör Ekle...", AddFolder));
        fileButtons.Children.Add(Btn("Seçileni Çıkar", () => { foreach (var i in _files.SelectedItems.Cast<object>().ToList()) _files.Items.Remove(i); }));
        fileButtons.Children.Add(Btn("Temizle", () => _files.Items.Clear()));
        root.Children.Add(fileButtons);
        _files.Height = 140;
        root.Children.Add(_files);

        root.Children.Add(Header("2. İşlemler (sırayla uygulanır)"));
        root.Children.Add(_principal);
        root.Children.Add(Row("Döndür (0,0 etrafında, konumlandırmadan sonra):", _rotate));
        root.Children.Add(Row("Aynala:", _mirror));
        root.Children.Add(Row("Ölçek katsayısı:", _scale));
        root.Children.Add(Row("Konumlandır:", _refPoint));

        root.Children.Add(Header("3. Çıktı"));
        var outRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var browse = Btn("Gözat...", BrowseOut);
        DockPanel.SetDock(browse, Dock.Right);
        outRow.Children.Add(browse);
        outRow.Children.Add(_outDir);
        root.Children.Add(new TextBlock { Text = "Çıktı klasörü:" });
        root.Children.Add(outRow);
        root.Children.Add(_saveDrawing);
        root.Children.Add(Row("Format:", _format));
        root.Children.Add(Row("Dosya adı eki (ör. _0):", _suffix));
        root.Children.Add(_pdf);
        root.Children.Add(_csv);

        var runRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        _run.Click += async (_, _) => await Run();
        runRow.Children.Add(_run);
        var close = new Button { Content = "Kapat", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        runRow.Children.Add(close);
        root.Children.Add(runRow);
        root.Children.Add(_progress);
        root.Children.Add(_log);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
    }

    private static TextBlock Header(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79)), Margin = new Thickness(0, 10, 0, 4) };

    private static Button Btn(string text, Action a)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        b.Click += (_, _) => a();
        return b;
    }

    private static DockPanel Row(string label, FrameworkElement ctl)
    {
        var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var l = new TextBlock { Text = label, Width = 300, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        d.Children.Add(l);
        ctl.HorizontalAlignment = HorizontalAlignment.Left;
        if (ctl is ComboBox cb) cb.MinWidth = 220;
        d.Children.Add(ctl);
        return d;
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(_files.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
            if (existing.Add(p)) _files.Items.Add(p);
        if (_outDir.Text.Length == 0 && _files.Items.Count > 0)
            _outDir.Text = System.IO.Path.Combine(System.IO.Path.GetDirectoryName((string)_files.Items[0])!, "ProfilCAD_cikti");
    }

    private void AddFiles()
    {
        var dlg = new OpenFileDialog { Filter = "CAD dosyaları (*.dwg;*.dxf)|*.dwg;*.dxf", Multiselect = true, Title = "Dosya Ekle" };
        if (dlg.ShowDialog(this) == true) AddPaths(dlg.FileNames);
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Klasör Seç" };
        if (dlg.ShowDialog(this) != true) return;
        var files = Directory.EnumerateFiles(dlg.FolderName)
            .Where(f => f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase));
        AddPaths(files.OrderBy(f => f));
    }

    private void BrowseOut()
    {
        var dlg = new OpenFolderDialog { Title = "Çıktı Klasörü" };
        if (dlg.ShowDialog(this) == true) _outDir.Text = dlg.FolderName;
    }

    private void Log(string s)
    {
        _log.AppendText(s + Environment.NewLine);
        _log.ScrollToEnd();
    }

    private async Task Run()
    {
        if (_busy) return;
        var files = _files.Items.Cast<string>().ToList();
        if (files.Count == 0) { MessageBox.Show(this, "Önce dosya ekleyin.", Title); return; }
        string outDir = _outDir.Text.Trim();
        if (outDir.Length == 0) { MessageBox.Show(this, "Çıktı klasörünü seçin.", Title); return; }
        if (!double.TryParse(_scale.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) || scale <= 0)
        {
            MessageBox.Show(this, "Ölçek katsayısı pozitif bir sayı olmalı.", Title);
            return;
        }

        var opt = new
        {
            Ref = (RefPoint)_refPoint.SelectedIndex,
            Rot = _rotate.SelectedIndex switch { 1 => Math.PI / 2, 2 => Math.PI, 3 => -Math.PI / 2, _ => 0.0 },
            Mirror = _mirror.SelectedIndex,
            Principal = _principal.IsChecked == true,
            Save = _saveDrawing.IsChecked == true,
            Ext = _format.SelectedIndex == 1 ? ".dwg" : ".dxf",
            Suffix = _suffix.Text.Trim(),
            Pdf = _pdf.IsChecked == true,
            Csv = _csv.IsChecked == true,
            Scale = scale
        };

        _busy = true;
        _run.IsEnabled = false;
        _progress.Maximum = files.Count;
        _progress.Value = 0;
        _log.Clear();
        var csv = new StringBuilder();
        csv.AppendLine("Dosya;Genişlik;Yükseklik;Alan;Çevre;Gx;Gy;Ix;Iy;Ixy;I1;I2;Alfa(°);Uyarı");
        int ok = 0, fail = 0;
        string company = _settings.CompanyName, logo = _settings.LogoPath, author = _settings.ReportAuthor;

        try
        {
            Directory.CreateDirectory(outDir);
            foreach (var f in files)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(f);
                try
                {
                    var (line, outEnts, outBase) = await Task.Run(() =>
                    {
                        var doc = new CadDocument();
                        CadFileIO.Load(f, doc);
                        var ents = doc.Entities;
                        string warn = "";
                        if (opt.Principal && !ProfileOps.AlignPrincipal(ents)) warn = "kapalı kesit yok (asal hizalama atlandı)";
                        if (Math.Abs(opt.Scale - 1) > 1e-12) ProfileOps.Apply(ents, Mat2D.Scaling(opt.Scale, Vec2.Zero));
                        // Döndürme/aynalama referans noktası etrafında, sonra konumlandırma
                        var pivot = opt.Ref != RefPoint.None ? ProfileOps.Reference(ents, opt.Ref) : Vec2.Zero;
                        if (opt.Rot != 0) ProfileOps.Apply(ents, Mat2D.Rotation(opt.Rot, pivot));
                        if (opt.Mirror == 1) ProfileOps.Apply(ents, Mat2D.Mirror(pivot, pivot + Vec2.UnitX));
                        else if (opt.Mirror == 2) ProfileOps.Apply(ents, Mat2D.Mirror(pivot, pivot + Vec2.UnitY));
                        ProfileOps.MoveToOrigin(ents, opt.Ref);

                        string baseOut = System.IO.Path.Combine(outDir, name + opt.Suffix);
                        if (opt.Save) CadFileIO.Save(baseOut + opt.Ext, doc);

                        var b = doc.Extents(ents);
                        var sp = SectionProperties.Compute(ents);
                        string N(double v) => v.ToString("0.######", CultureInfo.GetCultureInfo("tr-TR"));
                        if (!sp.IsValid && warn.Length == 0) warn = "kapalı kesit bulunamadı";
                        string row = sp.IsValid
                            ? $"{name};{N(b.Width)};{N(b.Height)};{N(sp.Area)};{N(sp.Perimeter)};{N(sp.Centroid.X)};{N(sp.Centroid.Y)};{N(sp.Ix)};{N(sp.Iy)};{N(sp.Ixy)};{N(sp.I1)};{N(sp.I2)};{N(GeoUtil.RadToDeg(sp.PrincipalAngle))};{warn}"
                            : $"{name};{N(b.Width)};{N(b.Height)};;;;;;;;;;;{warn}";
                        return (row, ents, baseOut);
                    });
                    // PDF (WPF yazı tipi altyapısı) arayüz iş parçacığında üretilir
                    if (opt.Pdf)
                        PdfReport.Create(outBase + ".pdf", new ReportInput
                        {
                            Entities = outEnts,
                            SourceName = System.IO.Path.GetFileName(f),
                            CompanyName = company,
                            LogoPath = logo,
                            Author = author,
                            Title = "Profil Kesit Raporu – " + name
                        });
                    csv.AppendLine(line);
                    Log("✓ " + name);
                    ok++;
                }
                catch (Exception ex)
                {
                    Log($"✗ {name}: {ex.Message}");
                    csv.AppendLine($"{name};;;;;;;;;;;;;HATA: {ex.Message.Replace(';', ',')}");
                    fail++;
                }
                _progress.Value++;
            }

            if (opt.Csv)
            {
                string csvPath = System.IO.Path.Combine(outDir, "kesit_ozellikleri.csv");
                File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true));
                Log("Özet tablo: " + csvPath);
            }
            Log($"Bitti: {ok} başarılı, {fail} hatalı. Çıktı: {outDir}");
        }
        catch (Exception ex)
        {
            Log("Hata: " + ex.Message);
        }
        finally
        {
            _busy = false;
            _run.IsEnabled = true;
        }
    }
}
