using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MyDesktopApp.Editor;
using MyDesktopApp.Geometry;
using MyDesktopApp.IO;
using MyDesktopApp.Model;

namespace MyDesktopApp;

public partial class MainWindow : Window
{
    // Etkin sekmenin belgesi ve düzenleyicisi (sekme değiştikçe değişir)
    private CadDocument _doc = null!;
    private CadEditor _editor = null!;
    private readonly List<string> _history = new();
    private const int MaxHistory = 400;

    public MainWindow()
    {
        InitializeComponent();
        BuildRibbon();
        DrawArea.CursorMoved += p => CoordText.Text = $"{Vec2.Format(p.X),12}, {Vec2.Format(p.Y),12}";

        var first = CreateTab();
        SnapToggle.IsChecked = first.Editor.SnapEnabled;
        GridToggle.IsChecked = first.Editor.GridEnabled;
        OrthoToggle.IsChecked = first.Editor.OrthoEnabled;
        TrackToggle.IsChecked = first.Editor.TrackingEnabled;
        ActivateTab(first);

        InitFeatures();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppendHistory("Profil CAD hazır. Komut listesi için YARDIM yazın. DWG/DXF açmak için Ctrl+O.");
        UpdateTitle();
        UpdatePanel();
        RebuildLayerPanel();
        InputBox.Focus();
        OnLoadedFeatures();
    }

    // ================================================================ Komut satırı

    private void AppendHistory(string msg)
    {
        _history.Add(msg);
        if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
        HistoryBox.Text = string.Join(Environment.NewLine, _history);
        HistoryBox.ScrollToEnd();
    }

    private void UpdateState()
    {
        PromptText.Text = _editor.Prompt;
        StatusText.Text = _editor.IsBusy ? $"Komut: {_editor.RunningCommandName}" : "";
        UpdateTitle();
    }

    private void SubmitInput()
    {
        string text = InputBox.Text;
        InputBox.Clear();
        _editor.Submit(text);
    }

    private void Cmd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string cmd)
        {
            _ = _editor.RunCommand(cmd);
            InputBox.Focus();
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _editor.SnapEnabled = SnapToggle.IsChecked == true;
        _editor.GridEnabled = GridToggle.IsChecked == true;
        _editor.OrthoEnabled = OrthoToggle.IsChecked == true;
        _editor.TrackingEnabled = TrackToggle.IsChecked == true;
        if (!_editor.TrackingEnabled) _editor.ClearTracking();
        DrawArea.RedrawAll();
        InputBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        // Özellikler panelinde (veya başka bir metin kutusunda) düzenleme yapılıyorsa tuşlara karışma
        if (Keyboard.FocusedElement is TextBox ftb && ftb != InputBox) return;

        // Kullanıcı tanımlı klavye kısayolları
        if (TryCustomShortcut(e)) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                InputBox.Clear();
                _editor.Cancel();
                e.Handled = true;
                return;
            case Key.Enter:
                SubmitInput();
                e.Handled = true;
                return;
            case Key.Space when _editor.Mode != Editor.InputMode.Text:
                SubmitInput();
                e.Handled = true;
                return;
            case Key.F3:
                SnapToggle.IsChecked = !(SnapToggle.IsChecked == true);
                Toggle_Click(this, e);
                e.Handled = true;
                return;
            case Key.F7:
                GridToggle.IsChecked = !(GridToggle.IsChecked == true);
                Toggle_Click(this, e);
                e.Handled = true;
                return;
            case Key.F11:
                TrackToggle.IsChecked = !(TrackToggle.IsChecked == true);
                Toggle_Click(this, e);
                e.Handled = true;
                return;
            case Key.F8:
                OrthoToggle.IsChecked = !(OrthoToggle.IsChecked == true);
                Toggle_Click(this, e);
                e.Handled = true;
                return;
            case Key.Delete when InputBox.Text.Length == 0 && !_editor.IsBusy:
                _editor.EraseSelection();
                e.Handled = true;
                return;
        }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: if (!_editor.IsBusy) _editor.DoUndo(); e.Handled = true; return;
                case Key.Y: if (!_editor.IsBusy) _editor.DoRedo(); e.Handled = true; return;
                case Key.A: _ = _editor.RunCommand("SELECTALL"); e.Handled = true; return;
                case Key.O: Open_Click(this, e); e.Handled = true; return;
                case Key.S: Save_Click(this, e); e.Handled = true; return;
                case Key.N: New_Click(this, e); e.Handled = true; return;
                case Key.W: CloseTab(_active!); e.Handled = true; return;
                case Key.Tab: SwitchTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; return;
                case Key.L: Library_Click(this, e); e.Handled = true; return;
                case Key.P: Report_Click(this, e); e.Handled = true; return;
            }
        }

        // Klavye her zaman komut satırına yazsın
        if (!InputBox.IsKeyboardFocusWithin && !(Keyboard.FocusedElement is TextBox))
            InputBox.Focus();
    }

    // ================================================================ Sağ panel

    private void UpdatePanel()
    {
        BuildPropertiesPanel();
        var sel = _doc.Selection.ToList();
        if (sel.Count == 0)
        {
            var all = _doc.Extents();
            SelectionInfo.Text = $"Seçim yok. Toplam {_doc.Entities.Count} nesne.\n" +
                                 (all.IsEmpty ? "" : $"Çizim sınırı:\n  Min {Vec2.Format(all.Min)}\n  Max {Vec2.Format(all.Max)}\n" +
                                                     $"Genişlik {Vec2.Format(all.Width)}  Yükseklik {Vec2.Format(all.Height)}");
            UpdateRibbonState();
            return;
        }

        var b = _doc.Extents(sel);
        var types = sel.GroupBy(e => e.TypeName).Select(g => $"{g.Key}×{g.Count()}");
        SelectionInfo.Text =
            $"{sel.Count} nesne: {string.Join(", ", types)}\n" +
            $"Min  {Vec2.Format(b.Min)}\n" +
            $"Max  {Vec2.Format(b.Max)}\n" +
            $"Genişlik {Vec2.Format(b.Width)}  Yükseklik {Vec2.Format(b.Height)}" +
            (sel.Any(e => e.GroupId != null)
                ? "\nGrup: " + string.Join(", ", sel.Where(e => e.GroupId != null).Select(e => e.GroupId).Distinct())
                : "") +
            "\nKatman: " + string.Join(", ", sel.Select(e => e.Layer).Distinct());

        UpdateRibbonState();
    }

    private static readonly (string Name, EntColor C)[] Palette =
    {
        ("Kırmızı", new EntColor(255, 0, 0, 1)), ("Sarı", new EntColor(255, 255, 0, 2)), ("Yeşil", new EntColor(0, 255, 0, 3)),
        ("Camgöbeği", new EntColor(0, 255, 255, 4)), ("Mavi", new EntColor(0, 0, 255, 5)), ("Eflatun", new EntColor(255, 0, 255, 6)),
        ("Beyaz", new EntColor(255, 255, 255, 7)), ("Gri", new EntColor(128, 128, 128, 8)), ("Açık gri", new EntColor(192, 192, 192, 9)),
        ("Turuncu", new EntColor(255, 127, 0, 30))
    };

    private void RebuildLayerPanel(bool force = false)
    {
        UpdateLayerCombos();
        var layers = _doc.Layers.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        string sig = _doc.CurrentLayer + "#" + string.Join("|", layers.Select(l => $"{l.Name}:{l.Color}:{l.Visible}"));
        if (!force && LayerPanel.Tag is string old && old == sig) return;
        LayerPanel.Tag = sig;
        LayerPanel.Children.Clear();

        foreach (var li in layers)
        {
            string name = li.Name;
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent };

            var current = new RadioButton
            {
                GroupName = "CurrentLayer",
                IsChecked = string.Equals(_doc.CurrentLayer, name, StringComparison.OrdinalIgnoreCase),
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Geçerli katman yap (yeni çizimler bu katmana gider)"
            };
            current.Checked += (_, _) =>
            {
                _doc.CurrentLayer = name;
                AppendHistory($"Geçerli katman: {name}");
                InputBox.Focus();
            };

            var swatch = new Button
            {
                Width = 16,
                Height = 14,
                Margin = new Thickness(2, 0, 6, 0),
                Focusable = false,
                BorderBrush = Brushes.Gray,
                Background = new SolidColorBrush(Color.FromRgb(li.Color.R, li.Color.G, li.Color.B)),
                ToolTip = "Katman rengini değiştir"
            };
            swatch.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                foreach (var (cname, col) in Palette)
                {
                    var mi = new MenuItem
                    {
                        Header = cname,
                        Icon = new Border { Width = 12, Height = 12, Background = new SolidColorBrush(Color.FromRgb(col.R, col.G, col.B)) }
                    };
                    mi.Click += (_, _) => ChangeLayerColor(name, col);
                    menu.Items.Add(mi);
                }
                swatch.ContextMenu = menu;
                menu.PlacementTarget = swatch;
                menu.IsOpen = true;
            };

            var visible = new CheckBox
            {
                Content = name,
                IsChecked = li.Visible,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Görünürlük · {_doc.CountOnLayer(name)} nesne"
            };
            visible.Click += (_, _) =>
            {
                if (_doc.Layers.TryGetValue(name, out var l)) l.Visible = visible.IsChecked == true;
                if (!(visible.IsChecked == true)) _doc.SetSelection(_doc.Selection.Where(x => !string.Equals(x.Layer, name, StringComparison.OrdinalIgnoreCase)).ToList());
                DrawArea.RedrawAll();
                InputBox.Focus();
            };

            var ctx = new ContextMenu();
            var miCur = new MenuItem { Header = "Geçerli yap" };
            miCur.Click += (_, _) => { _doc.CurrentLayer = name; RebuildLayerPanel(true); };
            var miMove = new MenuItem { Header = "Seçili nesneleri bu katmana taşı" };
            miMove.Click += (_, _) => _editor.MoveToLayer(_doc.Selection.ToList(), name);
            var miSelect = new MenuItem { Header = "Bu katmandaki nesneleri seç" };
            miSelect.Click += (_, _) => _doc.SetSelection(_doc.Entities.Where(x => string.Equals(x.Layer, name, StringComparison.OrdinalIgnoreCase)));
            var miRename = new MenuItem { Header = "Yeniden adlandır...", IsEnabled = name != "0" };
            miRename.Click += (_, _) => RenameLayer(name);
            var miDelete = new MenuItem { Header = "Katmanı sil", IsEnabled = name != "0" };
            miDelete.Click += (_, _) => DeleteLayer(name);
            ctx.Items.Add(miCur);
            ctx.Items.Add(miMove);
            ctx.Items.Add(miSelect);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miRename);
            ctx.Items.Add(miDelete);
            row.ContextMenu = ctx;

            DockPanel.SetDock(current, Dock.Left);
            DockPanel.SetDock(swatch, Dock.Left);
            row.Children.Add(current);
            row.Children.Add(swatch);
            row.Children.Add(visible);
            LayerPanel.Children.Add(row);
        }
    }

    private void ChangeLayerColor(string name, EntColor col)
    {
        if (!_doc.Layers.TryGetValue(name, out var l)) return;
        _doc.SaveUndo();
        l.Color = col;
        _editor.NotifyDocumentChanged();
        RebuildLayerPanel(true);
        InputBox.Focus();
    }

    private void NewLayer_Click(object sender, RoutedEventArgs e)
    {
        int i = 1;
        while (_doc.Layers.ContainsKey("Katman" + i)) i++;
        var name = InputDialog.Ask(this, "Yeni katman", "Katman adı:", "Katman" + i);
        if (string.IsNullOrWhiteSpace(name)) { InputBox.Focus(); return; }
        name = name.Trim();
        if (_doc.Layers.ContainsKey(name))
        {
            MessageBox.Show(this, $"\"{name}\" adında bir katman zaten var.", "Katman");
            return;
        }
        _doc.SaveUndo();
        var li = _doc.EnsureLayer(name);
        li.Color = Palette[(_doc.Layers.Count - 1) % Palette.Length].C;
        _doc.CurrentLayer = name;
        _editor.NotifyDocumentChanged();
        RebuildLayerPanel(true);
        AppendHistory($"Katman oluşturuldu ve geçerli yapıldı: {name}");
        InputBox.Focus();
    }

    private void MoveSelToLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_doc.Selection.Count == 0) { AppendHistory("Önce taşınacak nesneleri seçin."); return; }
        _editor.MoveToLayer(_doc.Selection.ToList(), _doc.CurrentLayer);
        InputBox.Focus();
    }

    private void RenameLayer(string name)
    {
        var nn = InputDialog.Ask(this, "Katmanı yeniden adlandır", "Yeni ad:", name);
        if (string.IsNullOrWhiteSpace(nn) || nn.Trim() == name) return;
        _doc.SaveUndo();
        if (!_doc.RenameLayer(name, nn))
        {
            MessageBox.Show(this, "Bu ad kullanılamıyor (boş veya zaten var).", "Katman");
            return;
        }
        _editor.NotifyDocumentChanged();
        RebuildLayerPanel(true);
        AppendHistory($"Katman yeniden adlandırıldı: {name} → {nn.Trim()}");
    }

    private void DeleteLayer(string name)
    {
        int count = _doc.CountOnLayer(name);
        bool deleteEntities = false;
        if (count > 0)
        {
            var r = MessageBox.Show(this,
                $"\"{name}\" katmanında {count} nesne var.\n\nEvet: nesneler de silinsin\nHayır: nesneler \"0\" katmanına taşınsın\nİptal: vazgeç",
                "Katmanı sil", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            deleteEntities = r == MessageBoxResult.Yes;
        }
        _doc.SaveUndo();
        _doc.DeleteLayer(name, deleteEntities);
        _editor.NotifyDocumentChanged();
        _doc.RaiseSelectionChanged();
        RebuildLayerPanel(true);
        AppendHistory($"Katman silindi: {name}" + (count > 0 ? (deleteEntities ? $" ({count} nesne silindi)" : $" ({count} nesne \"0\" katmanına taşındı)") : ""));
    }

    private void UpdateTitle()
    {
        if (_active == null) return;
        Title = $"Profil CAD - {_active.Name}{(_doc.IsModified ? " *" : "")}";
        UpdateTabHeader(_active);
    }

    // ================================================================ Dosya işlemleri

    private const string OpenFilter = "CAD dosyaları (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf";

    private bool ConfirmDiscard()
    {
        if (!_doc.IsModified) return true;
        var r = MessageBox.Show(this, $"\"{_active?.Name}\" çiziminde kaydedilmemiş değişiklikler var. Kaydedilsin mi?", "Profil CAD",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return SaveDocument(_doc.FilePath);
        return true;
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewTab();

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "DWG / DXF Aç", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames) await OpenFile(f);
    }

    /// <summary>Dosyayı açar (kaydedilmemiş değişiklik onayı çağırandadır).</summary>
    private async Task OpenFile(string path, bool recovered = false, string? originalPath = null)
    {
        if (!recovered)
        {
            var open = _tabs.FirstOrDefault(t => string.Equals(t.Doc.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (open != null) { ActivateTab(open); AppendHistory($"Dosya zaten açık: {path}"); return; }
        }
        // Etkin sekme boş ve değiştirilmemişse onu kullan, yoksa yeni sekme
        if (_doc.Entities.Count > 0 || _doc.IsModified || _doc.FilePath != null) ActivateTab(CreateTab());
        _editor.CancelCommand();
        var tmp = new CadDocument();
        bool loaded = false;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            AppendHistory($"Açılıyor: {path}");
            var report = await Task.Run(() => CadFileIO.Load(path, tmp));
            _doc.Clear();
            _doc.Merge(tmp);
            _doc.FilePath = recovered ? (string.IsNullOrEmpty(originalPath) ? null : originalPath) : path;
            _doc.IsModified = recovered;
            _doc.SaveFormatId = report.FormatId;
            AppendHistory(report.ToString());
            if (!recovered) _settings.AddRecent(path);
            loaded = true;
        }
        catch (Exception ex)
        {
            AppendHistory("Dosya açılamadı: " + ex.Message);
            MessageBox.Show(this, "Dosya açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        LayerPanel.Tag = null;
        _doc.RaiseChanged();
        DrawArea.ZoomExtents();
        UpdateTitle();
        UpdatePanel();
        InputBox.Focus();
        if (loaded && !recovered) RunImportAudit(_doc.Entities.ToList(), System.IO.Path.GetFileName(path));
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "İçe Aktar", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        await ImportFiles(dlg.FileNames);
    }

    private async Task ImportFiles(IEnumerable<string> fileNames)
    {
        _editor.CancelCommand();
        _doc.SaveUndo();
        var added = new List<Entity>();
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            foreach (var f in fileNames)
            {
                var tmp = new CadDocument();
                var report = await Task.Run(() => CadFileIO.Load(f, tmp));
                _doc.Merge(tmp);
                added.AddRange(tmp.Entities);
                AppendHistory($"{System.IO.Path.GetFileName(f)}: {report}");
            }
            _doc.SetSelection(added);
            AppendHistory("İçe aktarılan nesneler seçili. Örn. \"Ağırlık Merkezi → 0,0\" ile konumlandırabilirsiniz.");
        }
        catch (Exception ex)
        {
            AppendHistory("İçe aktarma hatası: " + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        _doc.IsModified = true;
        _doc.RaiseChanged();
        DrawArea.ZoomExtents();
        InputBox.Focus();
        if (added.Count > 0) RunImportAudit(added, "İçe aktarılan nesneler");
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument(_doc.FilePath);

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveDocument(null);

    /// <summary>Kayıt biçimi seçtiren kaydetme penceresi. İptalde null.</summary>
    private (string Path, CadFileIO.SaveFormat Format)? AskSavePath(string title, string defaultName)
    {
        var def = CadFileIO.FindFormat(_doc.SaveFormatId) ?? CadFileIO.FindFormat(_settings.LastSaveFormat) ?? CadFileIO.Formats[0];
        var dlg = new SaveFileDialog
        {
            Filter = CadFileIO.SaveFilter,
            FilterIndex = Array.IndexOf(CadFileIO.Formats, def) + 1,
            Title = title,
            FileName = defaultName,
            AddExtension = true
        };
        if (dlg.ShowDialog(this) != true) return null;
        var fmt = CadFileIO.Formats[Math.Clamp(dlg.FilterIndex - 1, 0, CadFileIO.Formats.Length - 1)];
        string path = dlg.FileName;
        if (!System.IO.Path.GetExtension(path).Equals(fmt.Ext, StringComparison.OrdinalIgnoreCase))
            path = System.IO.Path.ChangeExtension(path, fmt.Ext);
        _settings.LastSaveFormat = fmt.Id;
        _settings.Save();
        return (path, fmt);
    }

    private bool SaveDocument(string? path)
    {
        CadFileIO.SaveFormat fmt;
        if (path == null)
        {
            var r = AskSavePath("Farklı Kaydet", _doc.FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(_doc.FilePath) : "profil");
            if (r is not { } rr) return false;
            path = rr.Path;
            fmt = rr.Format;
        }
        else fmt = CadFileIO.DefaultFor(path, _doc.SaveFormatId ?? _settings.LastSaveFormat);
        try
        {
            CadFileIO.Save(path, _doc, null, fmt);
            _doc.FilePath = path;
            _doc.IsModified = false;
            _doc.SaveFormatId = fmt.Id;
            _settings.AddRecent(path);
            DeleteAutosave(_active!);
            AppendHistory($"Kaydedildi ({fmt.Label}): {path}");
            if (fmt.R12) AppendHistory("  Not: R12 biçiminde ölçüler ve taramalar çizgilere dönüştürüldü, gruplar yazılmadı.");
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            AppendHistory("Kaydetme hatası: " + ex.Message);
            MessageBox.Show(this, "Kaydedilemedi:\n" + ex.Message +
                                  (path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? "\n\nDXF olarak kaydetmeyi deneyin." : ""),
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void ExportSel_Click(object sender, RoutedEventArgs e)
    {
        if (_doc.Selection.Count == 0)
        {
            MessageBox.Show(this, "Önce dışa aktarılacak nesneleri seçin.", "Profil CAD");
            return;
        }
        if (AskSavePath("Seçileni Dışa Aktar", "kesit") is not { } r) return;
        try
        {
            CadFileIO.Save(r.Path, _doc, _doc.Selection.ToList(), r.Format);
            AppendHistory($"{_doc.Selection.Count} nesne dışa aktarıldı ({r.Format.Label}): {r.Path}");
        }
        catch (Exception ex)
        {
            AppendHistory("Dışa aktarma hatası: " + ex.Message);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var t in _tabs.ToList())
        {
            if (!t.Doc.IsModified) continue;
            ActivateTab(t);
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
        }
        _autosaveTimer.Stop();
        foreach (var t in _tabs) DeleteAutosave(t);
        _settings.Save();
    }
}
