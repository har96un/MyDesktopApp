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
    private readonly CadDocument _doc = new();
    private readonly CadEditor _editor;
    private readonly List<string> _history = new();
    private const int MaxHistory = 400;

    public MainWindow()
    {
        InitializeComponent();
        _editor = new CadEditor(_doc);
        DrawArea.Attach(_editor);

        _editor.Message += AppendHistory;
        _editor.StateChanged += UpdateState;
        _editor.RequestFileNew += () => New_Click(this, new RoutedEventArgs());
        _doc.Changed += (_, _) => { UpdateTitle(); RebuildLayerPanel(); };
        _doc.SelectionChanged += (_, _) => UpdatePanel();
        DrawArea.CursorMoved += p => CoordText.Text = $"{Vec2.Format(p.X),12}, {Vec2.Format(p.Y),12}";

        SnapToggle.IsChecked = _editor.SnapEnabled;
        GridToggle.IsChecked = _editor.GridEnabled;
        OrthoToggle.IsChecked = _editor.OrthoEnabled;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppendHistory("Profil CAD hazır. Komut listesi için YARDIM yazın. DWG/DXF açmak için Ctrl+O.");
        UpdateTitle();
        UpdatePanel();
        RebuildLayerPanel();
        InputBox.Focus();
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
        DrawArea.RedrawAll();
        InputBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

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
            case Key.Space when _editor.Mode != InputMode.Text:
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
            }
        }

        // Klavye her zaman komut satırına yazsın
        if (!InputBox.IsKeyboardFocusWithin && !(Keyboard.FocusedElement is TextBox))
            InputBox.Focus();
    }

    // ================================================================ Sağ panel

    private void UpdatePanel()
    {
        var sel = _doc.Selection.ToList();
        if (sel.Count == 0)
        {
            var all = _doc.Extents();
            SelectionInfo.Text = $"Seçim yok. Toplam {_doc.Entities.Count} nesne.\n" +
                                 (all.IsEmpty ? "" : $"Çizim sınırı:\n  Min {Vec2.Format(all.Min)}\n  Max {Vec2.Format(all.Max)}");
            SectionInfo.Text = "Kesit özellikleri için kapalı profil çizgilerini seçin.";
            return;
        }

        var b = _doc.Extents(sel);
        var types = sel.GroupBy(e => e.TypeName).Select(g => $"{g.Key}×{g.Count()}");
        SelectionInfo.Text =
            $"{sel.Count} nesne: {string.Join(", ", types)}\n" +
            $"Min  {Vec2.Format(b.Min)}\n" +
            $"Max  {Vec2.Format(b.Max)}\n" +
            $"Genişlik {Vec2.Format(b.Width)}  Yükseklik {Vec2.Format(b.Height)}";

        if (sel.Count > 20000)
        {
            SectionInfo.Text = "Seçim çok büyük; MP komutunu kullanın.";
            return;
        }
        var sp = SectionProperties.Compute(sel);
        if (!sp.IsValid)
        {
            SectionInfo.Text = "Kapalı kesit bulunamadı." +
                               (sp.OpenChains > 0 ? $"\n{sp.OpenChains} açık zincir var (uçlar birleşmiyor)." : "");
            return;
        }
        SectionInfo.Text = string.Join("\n", CadEditor.FormatSection(sp, b));
    }

    private void RebuildLayerPanel()
    {
        var names = _doc.Layers.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (LayerPanel.Tag is string sig && sig == string.Join("|", names)) return;
        LayerPanel.Tag = string.Join("|", names);
        LayerPanel.Children.Clear();
        foreach (var name in names)
        {
            var li = _doc.Layers[name];
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var swatch = new Border
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 0, 6, 0),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(li.Color.R, li.Color.G, li.Color.B))
            };
            var cb = new CheckBox { Content = name, IsChecked = li.Visible, Focusable = false };
            cb.Click += (_, _) =>
            {
                if (_doc.Layers.TryGetValue(name, out var l)) l.Visible = cb.IsChecked == true;
                DrawArea.RedrawAll();
                InputBox.Focus();
            };
            sp.Children.Add(swatch);
            sp.Children.Add(cb);
            LayerPanel.Children.Add(sp);
        }
    }

    private void UpdateTitle()
    {
        string name = _doc.FilePath != null ? System.IO.Path.GetFileName(_doc.FilePath) : "Adsız";
        Title = $"Profil CAD - {name}{(_doc.IsModified ? " *" : "")}";
    }

    // ================================================================ Dosya işlemleri

    private const string OpenFilter = "CAD dosyaları (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf";
    private const string SaveFilter = "DXF (*.dxf)|*.dxf|DWG (*.dwg)|*.dwg";

    private bool ConfirmDiscard()
    {
        if (!_doc.IsModified) return true;
        var r = MessageBox.Show(this, "Çizimde kaydedilmemiş değişiklikler var. Kaydedilsin mi?", "Profil CAD",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return SaveDocument(_doc.FilePath);
        return true;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _editor.CancelCommand();
        _doc.Clear();
        LayerPanel.Tag = null;
        RebuildLayerPanel();
        DrawArea.ZoomExtents();
        UpdateTitle();
        UpdatePanel();
        AppendHistory("Yeni çizim.");
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "DWG / DXF Aç" };
        if (dlg.ShowDialog(this) != true) return;
        _editor.CancelCommand();
        var tmp = new CadDocument();
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            AppendHistory($"Açılıyor: {dlg.FileName}");
            var report = await Task.Run(() => CadFileIO.Load(dlg.FileName, tmp));
            _doc.Clear();
            _doc.Merge(tmp);
            _doc.FilePath = dlg.FileName;
            _doc.IsModified = false;
            AppendHistory(report.ToString());
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
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "İçe Aktar", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        _editor.CancelCommand();
        _doc.SaveUndo();
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var added = new List<Entity>();
            foreach (var f in dlg.FileNames)
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
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument(_doc.FilePath);

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveDocument(null);

    private bool SaveDocument(string? path)
    {
        if (path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = SaveFilter,
                Title = "Farklı Kaydet",
                FileName = _doc.FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(_doc.FilePath) : "profil"
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            CadFileIO.Save(path, _doc);
            _doc.FilePath = path;
            _doc.IsModified = false;
            AppendHistory($"Kaydedildi: {path}");
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
        var dlg = new SaveFileDialog { Filter = SaveFilter, Title = "Seçileni Dışa Aktar", FileName = "kesit" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            CadFileIO.Save(dlg.FileName, _doc, _doc.Selection.ToList());
            AppendHistory($"{_doc.Selection.Count} nesne dışa aktarıldı: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            AppendHistory("Dışa aktarma hatası: " + ex.Message);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
    }
}
