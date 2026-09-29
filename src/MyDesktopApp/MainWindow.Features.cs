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
    private static string AutosaveFile(DocTab t) => System.IO.Path.Combine(AutosaveDir, $"sekme{t.Id}.dxf");
    private static string AutosaveInfo(string dxf) => System.IO.Path.ChangeExtension(dxf, ".txt");

    private void InitFeatures()
    {
        ApplySettings();
        _autosaveTimer.Tick += (_, _) => Autosave();
    }

    private void OnLoadedFeatures()
    {
        // Kurtarma: önceki oturumdan kalan otomatik kayıtlar
        string[] leftovers = Array.Empty<string>();
        try { if (Directory.Exists(AutosaveDir)) leftovers = Directory.GetFiles(AutosaveDir, "*.dxf"); } catch { /* yoksay */ }
        if (leftovers.Length > 0)
        {
            var infos = leftovers.Select(f =>
            {
                string orig = "";
                try { if (File.Exists(AutosaveInfo(f))) orig = File.ReadAllText(AutosaveInfo(f)).Trim(); } catch { /* yoksay */ }
                return (File: f, Orig: orig, When: File.GetLastWriteTime(f));
            }).ToList();
            string list = string.Join("\n", infos.Select(i => $"  • {(i.Orig.Length > 0 ? System.IO.Path.GetFileName(i.Orig) : "Adsız çizim")} ({i.When:dd.MM.yyyy HH:mm})"));
            var r = MessageBox.Show(this,
                "Profil CAD beklenmedik şekilde kapanmış görünüyor.\n\nOtomatik kaydedilmiş çizimler:\n" + list + "\n\nKurtarılsın mı?",
                "Kurtarma", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _ = RecoverAsync(infos.Select(i => (i.File, i.Orig)).ToList());
                return;
            }
            foreach (var i in infos) DeleteAutosaveFiles(i.File);
        }

        // Komut satırı argümanları (dosya ilişkilendirme / "Birlikte aç")
        var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToList();
        if (args.Count > 0) _ = OpenFilesAsync(args);
    }

    private async Task OpenFilesAsync(IEnumerable<string> files)
    {
        foreach (var f in files) await OpenFile(f);
    }

    private async Task RecoverAsync(List<(string File, string Orig)> items)
    {
        foreach (var (file, orig) in items)
        {
            // Dosyayı geçici bir yere taşı (yeni sekme kendi adıyla otomatik kaydeder)
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ProfilCAD_kurtarma_{Guid.NewGuid():N}.dxf");
            try { File.Copy(file, tmp, true); } catch { continue; }
            DeleteAutosaveFiles(file);
            await OpenFile(tmp, recovered: true, originalPath: orig);
            try { File.Delete(tmp); } catch { /* yoksay */ }
            AppendHistory("Çizim otomatik kayıttan kurtarıldı; lütfen kaydedin." + (orig.Length > 0 ? $" (Özgün dosya: {orig})" : ""));
        }
        UpdateTitle();
    }

    private void ApplySettings()
    {
        foreach (var t in _tabs) ApplySettingsTo(t, report: t == _active);

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

    private void ApplySettingsTo(DocTab t, bool report = false)
    {
        t.Doc.DimTextHeight = _settings.DimTextHeight;
        t.Doc.DimArrowSize = _settings.DimArrowSize;
        t.Doc.DimDecimals = _settings.DimDecimals;
        t.Doc.HatchPattern = _settings.HatchPattern;
        t.Doc.HatchScale = _settings.HatchScale;
        var bad = t.Editor.SetCustomAliases(_settings.Aliases);
        if (report && bad.Count > 0) AppendHistory("Geçersiz kısa adlar yok sayıldı: " + string.Join(", ", bad));
    }

    private void Autosave()
    {
        int n = 0;
        foreach (var t in _tabs)
        {
            if (!t.Doc.IsModified || t.Doc.Entities.Count == 0) continue;
            try
            {
                Directory.CreateDirectory(AutosaveDir);
                string file = AutosaveFile(t);
                string tmp = file + ".tmp.dxf";
                CadFileIO.Save(tmp, t.Doc);
                File.Move(tmp, file, overwrite: true);
                File.WriteAllText(AutosaveInfo(file), t.Doc.FilePath ?? "");
                n++;
            }
            catch (Exception ex)
            {
                AppendHistory($"Otomatik kayıt başarısız ({t.Name}): " + ex.Message);
            }
        }
        if (n > 0) StatusText.Text = $"Otomatik kaydedildi {DateTime.Now:HH:mm} ({n} çizim)";
    }

    private static void DeleteAutosave(DocTab t) => DeleteAutosaveFiles(AutosaveFile(t));

    private static void DeleteAutosaveFiles(string dxf)
    {
        try
        {
            if (File.Exists(dxf)) File.Delete(dxf);
            if (File.Exists(AutosaveInfo(dxf))) File.Delete(AutosaveInfo(dxf));
        }
        catch { /* yoksay */ }
    }

    // ================================================================ Son açılanlar

    private void FillRecentMenu(ItemsControl RecentMenu)
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
            mi.Click += async (_, _) => await OpenFile(path);
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
            m.Items.Add(CtxCmd("Blok oluştur", "BLOCK", "B"));
            m.Items.Add(CtxItem("Sol alt → 0,0", () => _ = _editor.MoveBoxPointToOrigin(0, 0)));
            m.Items.Add(CtxItem("Merkez → 0,0", () => _ = _editor.MoveBoxPointToOrigin(0.5, 0.5)));
            m.Items.Add(CtxItem("Kütüphaneye ekle...", () => OpenLibrary()?.AddSelectionToLibrary()));
            m.Items.Add(CtxItem("PDF profil raporu...", CreateReport, "Ctrl+P"));
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
            _library = new LibraryWindow(this, _settings, () => _doc, () => _editor);
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
            // Ctrl basılıysa etkin çizime ekle, değilse her dosya kendi sekmesinde açılır
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) await ImportFiles(cad);
            else await OpenFilesAsync(cad);
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

    // ================================================================ Çizim denetimi

    private void Audit_Click(object sender, RoutedEventArgs e) =>
        RunImportAudit(_doc.Entities.ToList(), _active?.Name ?? "Çizim", manual: true);

    /// <summary>Şüpheli nesneleri bulur ve kullanıcıya ne yapılacağını sorar.</summary>
    private void RunImportAudit(List<Entity> items, string source, bool manual = false)
    {
        if (!manual && !_settings.AuditOnOpen) return;
        AuditResult res;
        try { res = ImportAudit.Analyze(items); }
        catch (Exception ex) { AppendHistory("Denetim yapılamadı: " + ex.Message); return; }
        if (res.Total == 0)
        {
            if (manual) AppendHistory("Denetim: şüpheli nesne bulunamadı.");
            return;
        }
        foreach (var (r, l) in res.Items) AppendHistory($"Denetim: {AuditResult.Describe(r)}: {l.Count}");

        var w = new AuditWindow(this, res, source, _settings.AuditOnOpen);
        bool ok = w.ShowDialog() == true;
        if (w.AuditOnOpen != _settings.AuditOnOpen) { _settings.AuditOnOpen = w.AuditOnOpen; _settings.Save(); }
        if (!ok || w.Action == AuditAction.None || w.Chosen.Count == 0) return;

        var chosen = w.Chosen.Distinct().ToList();
        switch (w.Action)
        {
            case AuditAction.Delete:
                _doc.SaveUndo();
                _doc.Remove(chosen);
                _editor.NotifyDocumentChanged();
                _doc.RaiseSelectionChanged();
                AppendHistory($"Denetim: {chosen.Count} şüpheli nesne silindi (Ctrl+Z ile geri alınabilir).");
                DrawArea.ZoomExtents();
                break;
            case AuditAction.MoveToLayer:
                {
                    _doc.SaveUndo();
                    const string name = "_ŞÜPHELİ";
                    var li = _doc.EnsureLayer(name);
                    li.Color = new EntColor(255, 0, 0, 1);
                    li.Visible = false;
                    foreach (var en in chosen) en.Layer = name;
                    _doc.ClearSelection();
                    _editor.NotifyDocumentChanged();
                    LayerPanel.Tag = null;
                    RebuildLayerPanel(true);
                    AppendHistory($"Denetim: {chosen.Count} nesne gizli \"{name}\" katmanına taşındı. Katmanlar panelinden görünür yapabilirsiniz.");
                    DrawArea.ZoomExtents();
                    break;
                }
            case AuditAction.Select:
                _doc.SetSelection(chosen);
                AppendHistory($"Denetim: {chosen.Count} şüpheli nesne seçildi. Silmek için Del, gizlemek için bir katmana taşıyın.");
                break;
        }
    }
}
