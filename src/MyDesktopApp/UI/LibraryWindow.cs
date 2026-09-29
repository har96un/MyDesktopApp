using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyDesktopApp.Editor;
using MyDesktopApp.IO;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

/// <summary>Profil kütüphanesi: klasördeki DXF/DWG kesitleri küçük resimlerle listeler, çizime ekler.</summary>
public sealed class LibraryWindow : Window
{
    public const string DragFormat = "ProfilCADLibraryItem";

    private sealed class LibItem
    {
        public required string Path { get; init; }
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        public ListBoxItem? Ui;
        public Image? Img;
        public TextBlock? InfoText;
    }

    private readonly AppSettings _settings;
    private readonly Func<CadDocument> _docP;
    private readonly Func<CadEditor> _editorP;
    private CadDocument _doc => _docP();
    private CadEditor _editor => _editorP();
    private readonly TextBox _search = new() { Padding = new Thickness(3), MinWidth = 180 };
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(6, 3, 6, 3), Foreground = Brushes.DimGray };
    private readonly List<LibItem> _items = new();
    private Point? _dragStart;

    public LibraryWindow(Window owner, AppSettings settings, Func<CadDocument> doc, Func<CadEditor> editor)
    {
        Owner = owner;
        _settings = settings;
        _docP = doc;
        _editorP = editor;
        Title = "Profil Kütüphanesi";
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Btn("Seçimi Kütüphaneye Ekle...", AddSelectionToLibrary, "Çizimde seçili kesiti (ağırlık merkezi 0,0 olacak şekilde) kütüphaneye kaydeder"));
        buttons.Children.Add(Btn("Ekle", () => InsertSelected(), "Seçili profili çizime ekler (çift tık / sürükle-bırak da olur)"));
        buttons.Children.Add(Btn("Yenile", Reload, null));
        buttons.Children.Add(Btn("Klasör", OpenFolder, "Kütüphane klasörünü Gezgin'de aç"));
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        top.Children.Add(_search);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        _list.ItemsPanel = new ItemsPanelTemplate(panel);
        _list.MouseDoubleClick += (_, _) => InsertSelected();
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_list);
        _list.PreviewMouseMove += List_PreviewMouseMove;
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) InsertSelected();
            if (e.Key == Key.Delete) DeleteSelected();
        };
        root.Children.Add(_list);
        Content = root;

        _search.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Reload();
    }

    private static Button Btn(string text, Action a, string? tip)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = tip };
        b.Click += (_, _) => a();
        return b;
    }

    private string Folder => _settings.EffectiveLibraryFolder;

    public void Reload()
    {
        _items.Clear();
        _list.Items.Clear();
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(Folder)
                .Where(f => f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            _status.Text = "Klasör okunamadı: " + ex.Message;
            return;
        }

        foreach (var f in files)
        {
            var it = new LibItem { Path = f };
            var img = new Image { Width = 96, Height = 96, Stretch = Stretch.Uniform };
            var name = new TextBlock { Text = it.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 110, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
            var info = new TextBlock { Text = "…", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis };
            var sp = new StackPanel { Width = 112, Margin = new Thickness(2) };
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = img, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(name);
            sp.Children.Add(info);
            var lbi = new ListBoxItem { Content = sp, Tag = it, ToolTip = f };
            var ctx = new ContextMenu();
            ctx.Items.Add(Menu("Çizime ekle", () => InsertSelected()));
            ctx.Items.Add(Menu("Yeniden adlandır...", () => RenameItem(it)));
            ctx.Items.Add(Menu("Sil (Geri Dönüşüm Kutusu)", () => DeleteSelected()));
            lbi.ContextMenu = ctx;
            it.Ui = lbi;
            it.Img = img;
            it.InfoText = info;
            _items.Add(it);
            _list.Items.Add(lbi);
        }
        ApplyFilter();
        _status.Text = $"{files.Length} profil · {Folder}";

        // Küçük resimleri arka planda üret
        var snapshot = _items.ToList();
        Task.Run(() =>
        {
            foreach (var it in snapshot)
            {
                try
                {
                    var (ents, _) = LoadItem(it.Path);
                    var thumb = Thumbnail.Render(ents);
                    var sp = SectionProperties.Compute(ents);
                    string infoText = sp.IsValid ? $"A = {MyDesktopApp.Geometry.Vec2.Format(Math.Round(sp.Area, 2))}" : $"{ents.Count} nesne";
                    Dispatcher.Invoke(() =>
                    {
                        if (it.Img != null) it.Img.Source = thumb;
                        if (it.InfoText != null) it.InfoText.Text = infoText;
                    });
                }
                catch
                {
                    Dispatcher.Invoke(() => { if (it.InfoText != null) it.InfoText.Text = "okunamadı"; });
                }
            }
        });
    }

    private static MenuItem Menu(string header, Action a)
    {
        var m = new MenuItem { Header = header };
        m.Click += (_, _) => a();
        return m;
    }

    private void ApplyFilter()
    {
        string q = CadEditor.Fold(_search.Text.Trim());
        foreach (var it in _items)
            if (it.Ui != null)
                it.Ui.Visibility = q.Length == 0 || CadEditor.Fold(it.Name).Contains(q) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Kütüphane dosyasını okur: nesneler ve katmanlar.</summary>
    public static (List<Entity> Entities, List<LayerInfo> Layers) LoadItem(string path)
    {
        var tmp = new CadDocument();
        CadFileIO.Load(path, tmp);
        return (tmp.Entities.ToList(), tmp.Layers.Values.Select(l => l.Clone()).ToList());
    }

    private LibItem? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as LibItem;

    private void InsertSelected()
    {
        if (Selected is not { } it) return;
        try
        {
            var (ents, layers) = LoadItem(it.Path);
            if (ents.Count == 0) { _status.Text = "Dosyada nesne yok."; return; }
            Owner?.Activate();
            _ = _editor.InsertEntities(it.Name, ents, layers);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Profil okunamadı:\n" + ex.Message, "Kütüphane");
        }
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start) return;
        var pos = e.GetPosition(_list);
        if ((pos - start).Length < 6) return;
        _dragStart = null;
        if (Selected is not { } it) return;
        var data = new DataObject(DragFormat, it.Path);
        DragDrop.DoDragDrop(_list, data, DragDropEffects.Copy);
    }

    public void AddSelectionToLibrary()
    {
        var sel = _doc.Selection.ToList();
        if (sel.Count == 0)
        {
            MessageBox.Show(this, "Önce çizimde kütüphaneye eklenecek kesiti seçin.", "Kütüphane");
            return;
        }
        var name = InputDialog.Ask(this, "Kütüphaneye ekle", "Profil adı:", "Profil" + (_items.Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string path = System.IO.Path.Combine(Folder, name.Trim() + ".dxf");
        if (File.Exists(path) &&
            MessageBox.Show(this, $"\"{name}\" zaten var. Üzerine yazılsın mı?", "Kütüphane", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        var tmp = new CadDocument();
        foreach (var l in _doc.Layers.Values)
            if (sel.Any(e => string.Equals(e.Layer, l.Name, StringComparison.OrdinalIgnoreCase)))
                tmp.Layers[l.Name] = l.Clone();
        var clones = sel.Select(e => { var c = e.Clone(); c.GroupId = null; return c; }).ToList();
        ProfileOps.MoveToOrigin(clones, RefPoint.Centroid);
        foreach (var c in clones) tmp.Add(c);
        try
        {
            CadFileIO.Save(path, tmp);
            _status.Text = $"Eklendi: {path}";
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kaydedilemedi:\n" + ex.Message, "Kütüphane");
        }
    }

    private void RenameItem(LibItem it)
    {
        var nn = InputDialog.Ask(this, "Yeniden adlandır", "Yeni ad:", it.Name);
        if (string.IsNullOrWhiteSpace(nn) || nn == it.Name) return;
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) nn = nn.Replace(c, '_');
        var np = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(it.Path)!, nn.Trim() + System.IO.Path.GetExtension(it.Path));
        try
        {
            File.Move(it.Path, np);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Yeniden adlandırılamadı:\n" + ex.Message, "Kütüphane");
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not { } it) return;
        if (MessageBox.Show(this, $"\"{it.Name}\" kütüphaneden silinsin mi? (Geri Dönüşüm Kutusu'na taşınır)", "Kütüphane",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Silinemedi:\n" + ex.Message, "Kütüphane");
        }
    }

    private void OpenFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Folder}\"") { UseShellExecute = true }); }
        catch { /* yoksay */ }
    }
}
