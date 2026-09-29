using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyDesktopApp.Editor;
using MyDesktopApp.Model;

namespace MyDesktopApp;

/// <summary>Sekmeli çoklu belge: her sekmenin kendi çizimi, düzenleyicisi, geri alma geçmişi ve görünümü vardır.</summary>
public partial class MainWindow
{
    private sealed class DocTab
    {
        public int Id;
        public CadDocument Doc = null!;
        public CadEditor Editor = null!;
        public (double Scale, Point Offset)? View;
        public Border? Header;
        public TextBlock? Label;
        public string Name => Doc.FilePath != null ? System.IO.Path.GetFileName(Doc.FilePath) : $"Adsız{Id}";
    }

    private readonly List<DocTab> _tabs = new();
    private DocTab? _active;
    private int _nextTabId = 1;

    private static readonly Brush TabActiveBack = Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x36)));
    private static readonly Brush TabActiveFore = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush TabBack = Frozen(new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)));
    private static readonly Brush TabFore = Frozen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private DocTab CreateTab()
    {
        var t = new DocTab { Id = _nextTabId++ };
        t.Doc = new CadDocument();
        t.Editor = new CadEditor(t.Doc);
        t.Editor.Message += m => { if (t == _active) AppendHistory(m); };
        t.Editor.StateChanged += () => { if (t == _active) UpdateState(); };
        t.Editor.RequestFileNew += () => NewTab();
        t.Editor.IdleRightClick += () => { if (t == _active) ShowCanvasContextMenu(); };
        t.Editor.AuditRequested += () => { if (t == _active) Audit_Click(this, new RoutedEventArgs()); };
        t.Doc.Changed += (_, _) =>
        {
            if (t != _active) return;
            UpdateTitle();
            RebuildLayerPanel();
            if (!PropsPanel.IsKeyboardFocusWithin) BuildPropertiesPanel();
            UpdateRibbonState();
        };
        t.Doc.SelectionChanged += (_, _) => { if (t == _active) UpdatePanel(); };
        ApplySettingsTo(t);
        _tabs.Add(t);
        AddTabHeader(t);
        return t;
    }

    private void NewTab()
    {
        ActivateTab(CreateTab());
        AppendHistory("Yeni çizim sekmesi.");
    }

    private void ActivateTab(DocTab t)
    {
        if (_active == t) return;
        if (_active != null)
        {
            _active.Editor.CancelCommand();
            _active.View = DrawArea.ViewState;
        }
        _active = t;
        _doc = t.Doc;
        _editor = t.Editor;
        _editor.SnapEnabled = SnapToggle.IsChecked == true;
        _editor.GridEnabled = GridToggle.IsChecked == true;
        _editor.OrthoEnabled = OrthoToggle.IsChecked == true;
        _editor.TrackingEnabled = TrackToggle.IsChecked == true;
        DrawArea.Attach(_editor);
        if (t.View is { } v) DrawArea.ViewState = v;
        else DrawArea.ZoomExtents();

        LayerPanel.Tag = null;
        RebuildLayerPanel(true);
        UpdatePanel();
        UpdateState();
        UpdateTitle();
        foreach (var x in _tabs) UpdateTabHeader(x);
        t.Header?.BringIntoView();
        InputBox.Focus();
    }

    private void SwitchTab(int dir)
    {
        if (_tabs.Count < 2 || _active == null) return;
        int i = _tabs.IndexOf(_active);
        ActivateTab(_tabs[(i + dir + _tabs.Count) % _tabs.Count]);
    }

    /// <summary>Sekmeyi kapatır; kaydedilmemiş değişiklik varsa sorar.</summary>
    private bool CloseTab(DocTab t)
    {
        if (t.Doc.IsModified)
        {
            ActivateTab(t);
            if (!ConfirmDiscard()) return false;
        }
        t.Editor.CancelCommand();
        DeleteAutosave(t);
        int idx = _tabs.IndexOf(t);
        _tabs.Remove(t);
        if (t.Header != null) TabStrip.Children.Remove(t.Header);
        if (_tabs.Count == 0)
        {
            _active = null;
            ActivateTab(CreateTab());
        }
        else if (t == _active)
        {
            _active = null;
            ActivateTab(_tabs[Math.Min(idx, _tabs.Count - 1)]);
        }
        return true;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (_active != null) CloseTab(_active);
    }

    private void AddTabHeader(DocTab t)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(3, 0, 3, 0),
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Sekmeyi kapat (Ctrl+W)"
        };
        close.Click += (_, _) => CloseTab(t);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(label);
        sp.Children.Add(close);
        var border = new Border
        {
            Child = sp,
            Padding = new Thickness(10, 4, 4, 4),
            Margin = new Thickness(2, 3, 0, 0),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Cursor = Cursors.Hand
        };
        border.MouseLeftButtonDown += (_, e) => { ActivateTab(t); e.Handled = true; };
        border.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) { CloseTab(t); e.Handled = true; }
        };
        var ctx = new ContextMenu();
        var miClose = new MenuItem { Header = "Kapat" };
        miClose.Click += (_, _) => CloseTab(t);
        var miOthers = new MenuItem { Header = "Diğerlerini kapat" };
        miOthers.Click += (_, _) => { foreach (var o in _tabs.Where(x => x != t).ToList()) if (!CloseTab(o)) break; };
        var miPath = new MenuItem { Header = "Dosya yolunu kopyala" };
        miPath.Click += (_, _) => { if (t.Doc.FilePath != null) Clipboard.SetText(t.Doc.FilePath); };
        ctx.Items.Add(miClose);
        ctx.Items.Add(miOthers);
        ctx.Items.Add(new Separator());
        ctx.Items.Add(miPath);
        border.ContextMenu = ctx;
        t.Header = border;
        t.Label = label;
        TabStrip.Children.Add(border);
        UpdateTabHeader(t);
    }

    private void UpdateTabHeader(DocTab t)
    {
        if (t.Header == null || t.Label == null) return;
        bool act = t == _active;
        t.Label.Text = t.Name + (t.Doc.IsModified ? " *" : "");
        t.Label.Foreground = act ? TabActiveFore : TabFore;
        t.Label.FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal;
        t.Header.Background = act ? TabActiveBack : TabBack;
        t.Header.ToolTip = t.Doc.FilePath ?? "Kaydedilmemiş çizim";
        if (t.Header.Child is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is Button b)
            b.Foreground = act ? TabActiveFore : TabFore;
    }
}
