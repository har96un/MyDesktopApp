using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using MyDesktopApp.IO;
using MyDesktopApp.Model;
using MyDesktopApp.UI;

namespace MyDesktopApp;

/// <summary>Ayarlar, son dosyalar, otomatik kayıt, bağlam menüsü, kütüphane, toplu işlem, PDF rapor.</summary>
public partial class MainWindow
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _autosaveTimer = new();
    private readonly List<(KeyGesture Gesture, string Command)> _shortcuts = new();
    private LibraryWindow? _library;

    private static string AutosaveDir => System.IO.Path.Combine(AppSettings.AppDataDir, "autosave");
    private static string AutosaveFile => System.IO.Path.Combine(AutosaveDir, "autosave.dxf");
    private static string AutosaveInfo => System.IO.Path.Combine(AutosaveDir, "autosave.txt");

    private void InitFeatures()
    {
        ApplySettings();
        _autosaveTimer.Tick += (_, _) => Autosave();
        _editor.IdleRightClick += ShowCanvasContextMenu;
    }

    private void OnLoadedFeatures()
    {
        // Kurtarma
        if (File.Exists(AutosaveFile))
        {
            string orig = "";
            try { if (File.Exists(AutosaveInfo)) orig = File.ReadAllText(AutosaveInfo).Trim(); } catch { /* yoksay */ }
            var when = File.GetLastWriteTime(AutosaveFile);
            var r = MessageBox.Show(this,
                "Profil CAD beklenmedik şekilde kapanmış görünüyor.\n\n" +
                $"Otomatik kaydedilen çizim ({when:dd.MM.yyyy HH:mm}{(orig.Length > 0 ? ", " + System.IO.Path.GetFileName(orig) : "")}) kurtarılsın mı?",
                "Kurtarma", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _ = RecoverAsync(orig);
                return;
            }
            DeleteAutosave();
        }

        // Komut satırı argümanı (dosya ilişkilendirme / "Birlikte aç")
        var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToList();
        if (args.Count > 0) _ = OpenFile(args[0]);
    }

    private async Task RecoverAsync(string originalPath)
    {
        await OpenFile(AutosaveFile, recovered: true);
        AppendHistory("Çizim otomatik kayıttan kurtarıldı. Lütfen kaydedin." +
                      (originalPath.Length > 0 ? $" (Özgün dosya: {originalPath})" : ""));
        UpdateTitle();
    }

    private void ApplySettings()
    {
        _doc.DimTextHeight = _settings.DimTextHeight;
        _doc.DimArrowSize = _settings.DimArrowSize;
        _doc.DimDecimals = _settings.DimDecimals;
        _doc.HatchPattern = _settings.HatchPattern;
        _doc.HatchScale = _settings.HatchScale;

        var bad = _editor.SetCustomAliases(_settings.Aliases);
        if (bad.Count > 0) AppendHistory("Geçersiz kısa adlar yok sayıldı: " + string.Join(", ", bad));

        _shortcuts.Clear();
        foreach (var (key, cmd) in _settings.Shortcuts)
            if (ShortcutUtil.TryParse(key, out var g) && g != null && _editor.Commands.ContainsKey(cmd.ToUpperInvariant()))
                _shortcuts.Add((g, cmd.ToUpperInvariant()));

        _autosaveTimer.Stop();
        if (_settings.AutosaveMinutes > 0)
        {
            _autosaveTimer.Interval = TimeSpan.FromMinutes(_settings.AutosaveMinutes);
            _autosaveTimer.Start();
        }
    }

    private bool TryCustomShortcut(KeyEventArgs e)
    {
        foreach (var (g, cmd) in _shortcuts)
        {
            if (!ShortcutUtil.Matches(g, e)) continue;
            InputBox.Clear();
            _ = _editor.RunCommand(cmd);
            InputBox.Focus();
            return true;
        }
        return false;
    }

    // ================================================================ Otomatik kayıt

    private void Autosave()
    {
        if (!_doc.IsModified || _doc.Entities.Count == 0) return;
        try
        {
            Directory.CreateDirectory(AutosaveDir);
            string tmp = AutosaveFile + ".tmp.dxf";
            CadFileIO.Save(tmp, _doc);
            File.Move(tmp, AutosaveFile, overwrite: true);
            File.WriteAllText(AutosaveInfo, _doc.FilePath ?? "");
            StatusText.Text = $"Otomatik kaydedildi {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            AppendHistory("Otomatik kayıt başarısız: " + ex.Message);
        }
    }

    private static void DeleteAutosave()
    {
        try
        {
            if (File.Exists(AutosaveFile)) File.Delete(AutosaveFile);
            if (File.Exists(AutosaveInfo)) File.Delete(AutosaveInfo);
        }
        catch { /* yoksay */ }
    }

    // ================================================================ Son açılanlar

    private void RecentMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();
        var list = _settings.RecentFiles.ToList();
        if (list.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(boş)", IsEnabled = false });
            return;
        }
        int i = 1;
        foreach (var f in list)
        {
            string path = f;
            var mi = new MenuItem
            {
                Header = $"_{i++} {System.IO.Path.GetFileName(path)}",
                ToolTip = path,
                IsEnabled = File.Exists(path)
            };
            mi.Click += async (_, _) =>
            {
                if (!ConfirmDiscard()) return;
                await OpenFile(path);
            };
            RecentMenu.Items.Add(mi);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Listeyi temizle" };
        clear.Click += (_, _) => { _settings.RecentFiles.Clear(); _settings.Save(); };
        RecentMenu.Items.Add(clear);
    }

    // ================================================================ Bağlam menüsü

    private MenuItem CtxItem(string header, Action a, string? gesture = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        mi.Click += (_, _) => { a(); InputBox.Focus(); };
        return mi;
    }

    private MenuItem CtxCmd(string header, string cmd, string? gesture = null) =>
        CtxItem(header, () => _ = _editor.RunCommand(cmd), gesture);

    private void ShowCanvasContextMenu()
    {
        var m = new ContextMenu();
        if (!string.IsNullOrEmpty(_editor.LastCommand))
        {
            m.Items.Add(CtxCmd($"Tekrarla: {_editor.LastCommand}", _editor.LastCommand, "Enter"));
            m.Items.Add(new Separator());
        }
        if (_doc.Selection.Count > 0)
        {
            m.Items.Add(CtxCmd("Taşı", "MOVE", "M"));
            m.Items.Add(CtxCmd("Kopyala", "COPY", "CO"));
            m.Items.Add(CtxCmd("Döndür", "ROTATE", "RO"));
            m.Items.Add(CtxCmd("Aynala", "MIRROR", "MI"));
            m.Items.Add(CtxCmd("Ölçekle", "SCALE", "SC"));
            m.Items.Add(CtxCmd("Sil", "ERASE", "Del"));
            m.Items.Add(new Separator());
            m.Items.Add(CtxCmd("Grupla", "GROUP", "G"));
            m.Items.Add(CtxCmd("Grubu çöz", "UNGROUP", "UG"));
            m.Items.Add(CtxCmd("Patlat", "EXPLODE", "X"));
            m.Items.Add(new Separator());
            m.Items.Add(CtxCmd("Ağırlık merkezi → 0,0", "ORIGINCENTROID", "OC"));
            m.Items.Add(CtxCmd("Asal eksenlere hizala", "PRINCIPAL"));
            m.Items.Add(CtxCmd("Kesit özellikleri", "MASSPROP", "MP"));
            m.Items.Add(CtxItem("Kütüphaneye ekle...", () => OpenLibrary()?.AddSelectionToLibrary()));
            m.Items.Add(CtxItem("PDF kesit raporu...", CreateReport, "Ctrl+P"));
            m.Items.Add(CtxItem("Seçileni dışa aktar...", () => ExportSel_Click(this, new RoutedEventArgs())));
            m.Items.Add(new Separator());
            m.Items.Add(CtxItem("Seçimi temizle", () => _doc.ClearSelection(), "Esc"));
        }
        else
        {
            m.Items.Add(CtxCmd("Çizgi", "LINE", "L"));
            m.Items.Add(CtxCmd("Polyline", "PLINE", "PL"));
            m.Items.Add(CtxCmd("Dikdörtgen", "RECTANG", "REC"));
            m.Items.Add(CtxCmd("Daire", "CIRCLE", "C"));
            m.Items.Add(new Separator());
            m.Items.Add(CtxCmd("Buda", "TRIM", "TR"));
            m.Items.Add(CtxCmd("Yuvarla", "FILLET", "F"));
            m.Items.Add(CtxCmd("Tarama", "HATCH", "H"));
            m.Items.Add(new Separator());
            m.Items.Add(CtxCmd("Tümünü seç", "SELECTALL", "Ctrl+A"));
            m.Items.Add(CtxCmd("Tümünü göster", "ZOOMEXTENTS", "ZE"));
            m.Items.Add(CtxCmd("Geri al", "UNDO", "Ctrl+Z"));
            m.Items.Add(new Separator());
            m.Items.Add(CtxItem("Profil kütüphanesi...", () => OpenLibrary(), "Ctrl+L"));
            m.Items.Add(CtxItem("Ayarlar...", () => Settings_Click(this, new RoutedEventArgs())));
        }
        m.PlacementTarget = DrawArea;
        m.Placement = PlacementMode.MousePoint;
        m.IsOpen = true;
    }

    // ================================================================ Kütüphane / sürükle-bırak

    private LibraryWindow? OpenLibrary()
    {
        if (_library == null || !_library.IsLoaded)
        {
            _library = new LibraryWindow(this, _settings, _doc, _editor);
            _library.Closed += (_, _) => _library = null;
            _library.Show();
        }
        else
        {
            _library.Activate();
        }
        return _library;
    }

    private void Library_Click(object sender, RoutedEventArgs e) => OpenLibrary();

    private void DrawArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(LibraryWindow.DragFormat) || e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DrawArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(LibraryWindow.DragFormat) is string libPath)
        {
            var at = DrawArea.ToWorld(e.GetPosition(DrawArea));
            try
            {
                var (ents, layers) = LibraryWindow.LoadItem(libPath);
                await _editor.InsertEntities(System.IO.Path.GetFileNameWithoutExtension(libPath), ents, layers, at);
            }
            catch (Exception ex)
            {
                AppendHistory("Profil eklenemedi: " + ex.Message);
            }
            Activate();
            InputBox.Focus();
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var cad = files.Where(f => f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)).ToList();
            if (cad.Count == 0) return;
            if (_doc.Entities.Count == 0 && !_doc.IsModified && cad.Count == 1) await OpenFile(cad[0]);
            else await ImportFiles(cad);
            Activate();
        }
    }

    // ================================================================ Toplu işlem / rapor / ayarlar

    private void Batch_Click(object sender, RoutedEventArgs e)
    {
        new BatchWindow(this, _settings).Show();
    }

    private void Report_Click(object sender, RoutedEventArgs e) => CreateReport();

    private void CreateReport()
    {
        var ents = _doc.Selection.Count > 0 ? _doc.Selection.ToList() : _doc.VisibleEntities.ToList();
        if (ents.Count == 0) { MessageBox.Show(this, "Raporlanacak nesne yok.", "PDF Rapor"); return; }
        string baseName = _doc.FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(_doc.FilePath) : "kesit";
        var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", Title = "PDF Kesit Raporu", FileName = baseName + "_rapor" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            PdfReport.Create(dlg.FileName, new ReportInput
            {
                Entities = ents,
                SourceName = _doc.FilePath != null ? System.IO.Path.GetFileName(_doc.FilePath) : "",
                CompanyName = _settings.CompanyName,
                LogoPath = _settings.LogoPath,
                Author = _settings.ReportAuthor,
                Title = "Profil Kesit Raporu" + (_doc.FilePath != null ? " – " + baseName : "")
            });
            AppendHistory($"PDF raporu oluşturuldu: {dlg.FileName}" + (_doc.Selection.Count > 0 ? " (seçili nesneler)" : " (tüm çizim)"));
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
            catch { /* PDF görüntüleyici yok */ }
        }
        catch (Exception ex)
        {
            AppendHistory("PDF oluşturulamadı: " + ex.Message);
            MessageBox.Show(this, "PDF oluşturulamadı:\n" + ex.Message, "PDF Rapor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(this, _settings, _editor);
        if (w.ShowDialog() == true)
        {
            ApplySettings();
            _library?.Reload();
            AppendHistory("Ayarlar kaydedildi.");
        }
        InputBox.Focus();
    }
}
