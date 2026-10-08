using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>
/// Çizim alanı: görünüm (kaydırma/yakınlaştırma), nesnelerin çizimi ve fare olaylarının düzenleyiciye iletilmesi.
/// İki katman kullanır: sahne (nesneler, ızgara) ve üst katman (imleç, önizleme).
/// </summary>
public sealed class CadCanvas : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _scene = new();
    private readonly DrawingVisual _overlay = new();

    private CadEditor? _editor;
    private double _scale = 10;          // piksel / birim
    private Point _offset;               // dünya orijininin ekran konumu
    private bool _initialized;
    private Point? _panStart;
    private Point _panOffsetStart;
    private Point _lastMouse;

    // Kalemler dondurulmuş olduğundan iş parçacıkları arasında paylaşılabilir; önbellek kilitle korunur.
    private static readonly Dictionary<EntColor, Pen> _penCache = new();
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x2B)));
    private static readonly Pen GridMinor = FrozenPen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen GridMajor = FrozenPen(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen AxisX = FrozenPen(Color.FromArgb(0xB0, 0xE0, 0x40, 0x40), 1.2);
    private static readonly Pen AxisY = FrozenPen(Color.FromArgb(0xB0, 0x40, 0xC0, 0x40), 1.2);
    private static readonly Pen SelPen = FrozenPen(Color.FromRgb(0x4D, 0xA6, 0xFF), 2.2, dashed: true);
    private static readonly Pen PreviewPen = FrozenPen(Color.FromRgb(0xFF, 0xD2, 0x4D), 1, dashed: true);
    private static readonly Pen RubberPen = FrozenPen(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 1, dashed: true);
    private static readonly Pen CrossPen = FrozenPen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen SnapPen = FrozenPen(Color.FromRgb(0x33, 0xFF, 0x66), 2);
    private static readonly Pen WindowPenW = FrozenPen(Color.FromRgb(0x4D, 0x8D, 0xFF), 1);
    private static readonly Pen WindowPenC = FrozenPen(Color.FromRgb(0x4D, 0xFF, 0x8D), 1, dashed: true);
    private static readonly Brush WindowFillW = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x4D, 0x8D, 0xFF)));
    private static readonly Brush WindowFillC = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x4D, 0xFF, 0x8D)));
    private static readonly Pen TrackPen = FrozenPen(Color.FromArgb(0xD0, 0x7C, 0xFC, 0x00), 1, dashed: true);
    private static readonly Pen TrackMarkPen = FrozenPen(Color.FromRgb(0x7C, 0xFC, 0x00), 1.5);
    private static readonly Brush TrackTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x7C, 0xFC, 0x00)));
    private static readonly Brush TipBack = Frozen(new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x14, 0x18)));
    private static readonly Brush OriginText = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)));

    /// <summary>İmleç dünya koordinatı değişti.</summary>
    public event Action<Vec2>? CursorMoved;

    public CadCanvas()
    {
        _visuals = new VisualCollection(this) { _scene, _overlay };
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.None;
        SizeChanged += (_, _) =>
        {
            if (!_initialized && ActualWidth > 0)
            {
                _offset = new Point(ActualWidth * 0.3, ActualHeight * 0.7);
                _initialized = true;
            }
            RedrawAll();
        };
        MouseLeave += (_, _) => { _mouseInside = false; RedrawOverlay(); };
        MouseEnter += (_, _) => { _mouseInside = true; };
    }

    private bool _mouseInside;

    private readonly System.Windows.Threading.DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private bool _tickHooked;

    /// <summary>Tuvali bir düzenleyiciye bağlar (sekme değişiminde önceki bağlantı çözülür).</summary>
    public void Attach(CadEditor editor)
    {
        if (_editor != null)
        {
            _editor.SceneChanged -= ScheduleRedraw;
            _editor.OverlayChanged -= RedrawOverlay;
        }
        _editor = editor;
        if (!_tickHooked)
        {
            _tickTimer.Tick += (_, _) => _editor?.Tick();
            _tickTimer.Start();
            _tickHooked = true;
        }
        editor.PixelSizeProvider = () => 1.0 / _scale;
        editor.ZoomExtentsAction = ZoomExtents;
        editor.SceneChanged += ScheduleRedraw;
        editor.OverlayChanged += RedrawOverlay;
        RedrawAll();
    }

    /// <summary>Görünüm durumu (ölçek ve kaydırma); sekme başına saklanır.</summary>
    public (double Scale, Point Offset) ViewState
    {
        get => (_scale, _offset);
        set
        {
            _scale = value.Scale;
            _offset = value.Offset;
            _initialized = true;
            RedrawAll();
        }
    }

    private bool _redrawScheduled;

    /// <summary>Birden çok değişikliği tek çizimde birleştirir.</summary>
    public void ScheduleRedraw()
    {
        _sceneDirty = true;
        if (_redrawScheduled) return;
        _redrawScheduled = true;
        Dispatcher.InvokeAsync(() =>
        {
            _redrawScheduled = false;
            // Bu arada sahne zaten yeniden çizildiyse (ör. ZoomExtents) ikinci kez çizme
            if (_sceneDirty) RedrawAll();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    // ================================================================ Koordinat dönüşümü

    public Point ToScreen(Vec2 p) => new(_offset.X + p.X * _scale, _offset.Y - p.Y * _scale);
    public Vec2 ToWorld(Point s) => new((s.X - _offset.X) / _scale, (_offset.Y - s.Y) / _scale);

    private BBox ViewBox()
    {
        var a = ToWorld(new Point(0, ActualHeight));
        var b = ToWorld(new Point(ActualWidth, 0));
        return BBox.FromPoints(a, b);
    }

    public void ZoomExtents()
    {
        if (_editor == null || ActualWidth < 10) return;
        var b = _editor.Doc.Extents();
        if (b.IsEmpty)
        {
            _scale = 10;
            _offset = new Point(ActualWidth / 2, ActualHeight / 2);
        }
        else
        {
            double w = Math.Max(b.Width, 1e-6), h = Math.Max(b.Height, 1e-6);
            _scale = Math.Min(ActualWidth * 0.9 / w, ActualHeight * 0.9 / h);
            _scale = Math.Clamp(_scale, 1e-6, 1e8);
            var c = b.Center;
            _offset = new Point(ActualWidth / 2 - c.X * _scale, ActualHeight / 2 + c.Y * _scale);
        }
        RedrawAll();
    }

    public void ZoomBy(double factor, Point at)
    {
        var w = ToWorld(at);
        _scale = Math.Clamp(_scale * factor, 1e-6, 1e8);
        _offset = new Point(at.X - w.X * _scale, at.Y + w.Y * _scale);
        ViewChanged();
    }

    // ================================================================ Fare

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (e.ClickCount == 2) { ZoomExtents(); return; }
            _panStart = pos;
            _panOffsetStart = _offset;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_editor == null) return;
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && _editor.LeftDoubleClick(ToWorld(pos)))
        {
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            CaptureMouse();
            _editor.LeftDown(ToWorld(pos), pos, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _editor.RightClick();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle && _panStart != null)
        {
            _panStart = null;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            ReleaseMouseCapture();
            _editor?.LeftUp(ToWorld(pos), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        _lastMouse = pos;
        _mouseInside = true;
        if (_panStart is { } ps)
        {
            _offset = new Point(_panOffsetStart.X + pos.X - ps.X, _panOffsetStart.Y + pos.Y - ps.Y);
            ViewChanged();
        }
        if (e.LeftButton == MouseButtonState.Pressed) _editor?.LeftDrag(pos);
        var w = ToWorld(pos);
        _editor?.MouseMove(w);
        CursorMoved?.Invoke(_editor?.Cursor ?? w);
    }

    // ================================================================ Çizim

    public void RedrawAll()
    {
        RedrawScene();
        RedrawOverlay();
    }

    // Sahnenin en son hangi görünümle çizildiği (kaydırma/yakınlaştırma sırasında
    // sahne yeniden oluşturulmadan dönüşümle kaydırılır, iş bitince yeniden çizilir).
    private double _sceneScale = 1;
    private Point _sceneOffset;
    private double _lastSceneMs;
    private System.Windows.Threading.DispatcherTimer? _viewTimer;

    // Sahne bu süreden uzun sürüyorsa (büyük çizim) arka planda kurulur; arayüz donmaz,
    // yeni sahne hazır olana kadar eski görüntü dönüşümle yerinde tutulur.
    private const double BackgroundThresholdMs = 30;
    private readonly SceneBuilder _uiBuilder = new();
    private readonly SceneBuilder _bgBuilder = new();
    private int _buildGen;          // her sahne isteğinde artar; eski arka plan sonuçları atılır
    private bool _building;         // arka planda kurulum sürüyor
    private bool _buildPending;     // kurulum sürerken yeni istek geldi
    private bool _sceneDirty;

    /// <summary>
    /// Görünüm (kaydırma/yakınlaştırma) değişti. Sahne hızlı çiziliyorsa hemen yeniden çizilir;
    /// ağır çizimlerde mevcut görüntü dönüşümle taşınır, fare durunca bir kez yeniden çizilir.
    /// </summary>
    private void ViewChanged()
    {
        if (_lastSceneMs < BackgroundThresholdMs)
        {
            RedrawAll();
            return;
        }
        ApplySceneTransform();
        RedrawOverlay();
        if (_viewTimer == null)
        {
            _viewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _viewTimer.Tick += (_, _) => { _viewTimer!.Stop(); RedrawScene(); };
        }
        _viewTimer.Stop();
        _viewTimer.Start();
    }

    /// <summary>Son çizilen sahneyi geçerli görünüme taşır (yeniden çizmeden).</summary>
    private void ApplySceneTransform()
    {
        double k = _scale / _sceneScale;
        _scene.Transform = new MatrixTransform(k, 0, 0, k, _offset.X - _sceneOffset.X * k, _offset.Y - _sceneOffset.Y * k);
    }

    private sealed class LineBatch
    {
        public readonly StreamGeometry Geo = new();
        public readonly StreamGeometryContext Ctx;
        public LineBatch() { Ctx = Geo.Open(); }
    }

    private void PrepareBuilder(SceneBuilder b)
    {
        b.Scale = _scale;
        b.Offset = _offset;
        b.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        b.Doc = _editor?.Doc;
    }

    private void RedrawScene()
    {
        _viewTimer?.Stop();
        _sceneDirty = false;
        if (_editor == null)
        {
            _buildGen++;
            Compose(null, _scale, _offset);
            return;
        }
        if (_lastSceneMs >= BackgroundThresholdMs)
        {
            RequestBackgroundBuild();
            return;
        }
        _buildGen++;
        _buildPending = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        PrepareBuilder(_uiBuilder);
        var doc = _editor.Doc;
        var g = _uiBuilder.Build(doc, doc.Entities.ToArray(), doc.Selection.ToArray(), ViewBox());
        _lastSceneMs = sw.Elapsed.TotalMilliseconds;
        Compose(g, _scale, _offset);
    }

    /// <summary>
    /// Sahneyi geçerli görünüm için oluşturur: zemin, ızgara, eksenler ve (başka bir görünüm için
    /// kurulmuş olabilen) nesne çizimi. Nesne çizimi geçerli görünüme dönüşümle yerleştirilir.
    /// </summary>
    private void Compose(DrawingGroup? entities, double builtScale, Point builtOffset)
    {
        _scene.Transform = null;
        _sceneScale = _scale;
        _sceneOffset = _offset;
        using var dc = _scene.RenderOpen();
        dc.DrawRectangle(Background, null, new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        if (_editor == null) return;
        if (_editor.GridEnabled) DrawGrid(dc);
        DrawAxes(dc);
        if (entities == null) return;
        double k = _scale / builtScale;
        bool same = Math.Abs(k - 1) < 1e-12 && Math.Abs(builtOffset.X - _offset.X) < 1e-9 && Math.Abs(builtOffset.Y - _offset.Y) < 1e-9;
        if (!same) dc.PushTransform(new MatrixTransform(k, 0, 0, k, _offset.X - builtOffset.X * k, _offset.Y - builtOffset.Y * k));
        dc.DrawDrawing(entities);
        if (!same) dc.Pop();
    }

    private void RequestBackgroundBuild()
    {
        _buildGen++;
        if (_building)
        {
            _buildPending = true;
            ApplySceneTransform();
            return;
        }
        StartBackgroundBuild();
    }

    private void StartBackgroundBuild()
    {
        if (_editor == null) return;
        _building = true;
        _buildPending = false;
        int gen = _buildGen;
        var doc = _editor.Doc;
        // Nesne listeleri arayüz iş parçacığında kopyalanır; kurulum sırasında belge değişirse
        // sonuç zaten atılır ve yeniden kurulur.
        var ents = doc.Entities.ToArray();
        var sel = doc.Selection.ToArray();
        var view = ViewBox();
        double scale = _scale;
        var offset = _offset;
        PrepareBuilder(_bgBuilder);
        var builder = _bgBuilder;
        Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DrawingGroup? g = null;
            try { g = builder.Build(doc, ents, sel, view, () => gen != Volatile.Read(ref _buildGen)); }
            catch { /* kurulum sırasında belge değişti; yeniden kurulacak */ }
            return (g, sw.Elapsed.TotalMilliseconds);
        }).ContinueWith(t =>
        {
            var (g, ms) = t.Result;
            Dispatcher.InvokeAsync(() =>
            {
                _building = false;
                if (g != null && gen == _buildGen && _editor?.Doc == doc)
                {
                    _lastSceneMs = ms;
                    Compose(g, scale, offset);
                }
                else if (g == null && gen == _buildGen) _buildPending = true;   // hata: yeniden dene
                if (_buildPending)
                {
                    if (_lastSceneMs >= BackgroundThresholdMs) StartBackgroundBuild();
                    else RedrawScene();
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }, TaskScheduler.Default);
    }
    private void DrawGrid(DrawingContext dc)
    {
        // Ekranda ~15 pikselden sık olmayacak şekilde 10'un kuvveti aralık seç
        double step = Math.Pow(10, Math.Ceiling(Math.Log10(15 / _scale)));
        if (double.IsNaN(step) || step <= 0) return;
        var v = ViewBox();
        long i0 = (long)Math.Floor(v.MinX / step), i1 = (long)Math.Ceiling(v.MaxX / step);
        long j0 = (long)Math.Floor(v.MinY / step), j1 = (long)Math.Ceiling(v.MaxY / step);
        if (i1 - i0 > 2000 || j1 - j0 > 2000) return;
        for (long i = i0; i <= i1; i++)
        {
            var x = ToScreen(new Vec2(i * step, 0)).X;
            dc.DrawLine(i % 10 == 0 ? GridMajor : GridMinor, new Point(x, 0), new Point(x, ActualHeight));
        }
        for (long j = j0; j <= j1; j++)
        {
            var y = ToScreen(new Vec2(0, j * step)).Y;
            dc.DrawLine(j % 10 == 0 ? GridMajor : GridMinor, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawAxes(DrawingContext dc)
    {
        var o = ToScreen(Vec2.Zero);
        dc.DrawLine(AxisX, new Point(0, o.Y), new Point(ActualWidth, o.Y));
        dc.DrawLine(AxisY, new Point(o.X, 0), new Point(o.X, ActualHeight));

        // Orijin simgesi (UCS)
        var xPen = FrozenPen(Color.FromRgb(0xFF, 0x50, 0x50), 2);
        var yPen = FrozenPen(Color.FromRgb(0x50, 0xE0, 0x50), 2);
        dc.DrawLine(xPen, o, new Point(o.X + 40, o.Y));
        dc.DrawLine(yPen, o, new Point(o.X, o.Y - 40));
        dc.DrawRectangle(null, CrossPen, new Rect(o.X - 3, o.Y - 3, 6, 6));
        DrawLabel(dc, "X", new Point(o.X + 42, o.Y - 8), xPen.Brush);
        DrawLabel(dc, "Y", new Point(o.X - 4, o.Y - 58), yPen.Brush);
        DrawLabel(dc, "0,0", new Point(o.X + 6, o.Y + 4), OriginText);
    }

    private void DrawLabel(DrawingContext dc, string text, Point at, Brush brush, double size = 12)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, at);
    }

    /// <summary>
    /// Sahne kurucu: nesneleri belirli bir görünüm (ölçek/kaydırma) için ekran koordinatlarında çizer.
    /// Kendi görünüm ve önbellek durumunu taşıdığı için arka plan iş parçacığında da çalışabilir
    /// (ürettiği her şey dondurulur). Aynı örnek aynı anda tek iş parçacığından kullanılmalıdır.
    /// </summary>
    private sealed class SceneBuilder
    {
        public double Scale = 10;
        public Point Offset;
        public double PixelsPerDip = 1;
        public CadDocument? Doc;
        private Dictionary<Brush, LineBatch>? _fillBatches;

        public Point ToScreen(Vec2 p) => new(Offset.X + p.X * Scale, Offset.Y - p.Y * Scale);

        /// <summary>Görünür nesneleri ve seçim vurgusunu tek bir dondurulmuş çizime toplar.</summary>
        public DrawingGroup Build(CadDocument doc, Entity[] entities, Entity[] selection, BBox view, Func<bool>? cancelled = null)
        {
            Doc = doc;
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                var batches = new Dictionary<Pen, LineBatch>();
                _fillBatches = new Dictionary<Brush, LineBatch>();
                LineBatch BatchFor(Pen pen)
                {
                    if (!batches.TryGetValue(pen, out var lb)) batches[pen] = lb = new LineBatch();
                    return lb;
                }

                int k = 0;
                foreach (var e in entities)
                {
                    if ((++k & 1023) == 0 && cancelled != null && cancelled()) break;
                    if (!doc.IsVisible(e)) continue;
                    var b = doc.BoundsOf(e);
                    if (!b.IsEmpty && !view.Intersects(b)) continue;
                    var pen = GetPen(doc.ResolveColor(e));
                    AddToScene(dc, e, b, pen, BatchFor, doc, view, 0);
                }
                // Seçim vurgusu da tek geometride
                LineBatch? sel = null;
                foreach (var e in selection)
                {
                    if (!doc.IsVisible(e)) continue;
                    var b = doc.BoundsOf(e);
                    if (!b.IsEmpty && !view.Intersects(b)) continue;
                    if (e is TextEntity or DimensionEntity or HatchEntity) { DrawEntity(dc, e, SelPen, SelPen.Brush); continue; }
                    sel ??= new LineBatch();
                    AddToScene(dc, e, b, SelPen, _ => sel!, null, view, 0);
                }

                foreach (var (pen, lb) in batches)
                {
                    lb.Ctx.Close();
                    lb.Geo.Freeze();
                    dc.DrawGeometry(null, pen, lb.Geo);
                }
                foreach (var (brush, fb) in _fillBatches)
                {
                    fb.Ctx.Close();
                    fb.Geo.Freeze();
                    dc.DrawGeometry(brush, null, fb.Geo);
                }
                _fillBatches = null;
                if (sel != null)
                {
                    sel.Ctx.Close();
                    sel.Geo.Freeze();
                    dc.DrawGeometry(null, SelPen, sel.Geo);
                }
            }
            group.Freeze();
            return group;
        }
        /// <summary>Nesneyi sahneye ekler: çizgisel olanlar kalem grubuna, yazı/ölçü/tarama doğrudan çizilir.</summary>
        private void AddToScene(DrawingContext dc, Entity e, BBox b, Pen pen, Func<Pen, LineBatch> batchFor, CadDocument? doc, BBox view, int depth)
        {
            // Ekranda 1 pikselden küçük nesneler: tek nokta (ayrıntı hesaplanmaz)
            if (e is not BlockRefEntity && !b.IsEmpty && b.Width * Scale < 1.2 && b.Height * Scale < 1.2)
            {
                var p = ToScreen(new Vec2(b.MinX, b.MinY));
                var bctx = batchFor(pen).Ctx;
                bctx.BeginFigure(p, false, false);
                bctx.LineTo(new Point(p.X + 1, p.Y), true, false);
                return;
            }
            switch (e)
            {
                case DimensionEntity when Math.Max(b.Width, b.Height) * Scale < 12:
                    // Çok küçük ölçü: yazısız, yalnızca geometrisi
                    AddPrims(batchFor(pen).Ctx, e.Primitives());
                    return;
                case HatchEntity h when !h.IsSolid:
                    {
                        // Desen çizgileri de kalem grubuna (binlerce ayrı DrawLine yerine tek geometri)
                        var hctx = batchFor(pen).Ctx;
                        double spacingPx = HatchEntity.BaseSpacing * h.Scale * Scale;
                        if (spacingPx >= 2)
                            foreach (var (a, bb) in h.PatternSegments())
                            {
                                hctx.BeginFigure(ToScreen(a), false, false);
                                hctx.LineTo(ToScreen(bb), true, false);
                            }
                        else
                            foreach (var loop in h.Loops) AddPrims(hctx, HatchEntity.LoopPrims(loop));
                        return;
                    }
                case DimensionEntity dim when _fillBatches != null:
                    {
                        // Ölçü çizgileri ve okları da gruplanır; yalnızca yazılar tek tek çizilir
                        var g = dim.Build();
                        var dctx = batchFor(pen).Ctx;
                        foreach (var (a, bb) in g.Lines)
                        {
                            dctx.BeginFigure(ToScreen(a), false, false);
                            dctx.LineTo(ToScreen(bb), true, false);
                        }
                        if (g.Arcs.Count > 0) AddPrims(dctx, g.Arcs);
                        if (g.Arrows.Count > 0)
                        {
                            if (!_fillBatches.TryGetValue(pen.Brush, out var fb)) _fillBatches[pen.Brush] = fb = new LineBatch();
                            foreach (var tri in g.Arrows)
                            {
                                fb.Ctx.BeginFigure(ToScreen(tri[0]), true, true);
                                fb.Ctx.LineTo(ToScreen(tri[1]), true, false);
                                fb.Ctx.LineTo(ToScreen(tri[2]), true, false);
                            }
                        }
                        foreach (var (anchor, rot, th, text) in g.Texts)
                            DrawCenteredText(dc, text, anchor, rot, th, pen.Brush);
                        return;
                    }
                case TextEntity or DimensionEntity or HatchEntity:
                    DrawEntity(dc, e, pen, pen.Brush);
                    return;
                case BlockRefEntity br when depth < 16:
                    foreach (var (child, layer, color, cb) in br.DrawItems())
                    {
                        if (!cb.IsEmpty && !view.Intersects(cb)) continue;
                        var cp = pen;
                        if (doc != null)
                        {
                            if (doc.Layers.TryGetValue(layer, out var li) && !li.Visible) continue;
                            cp = GetPen(color ?? (li != null ? li.Color : doc.ResolveColor(br)));
                        }
                        AddToScene(dc, child, cb, cp, batchFor, doc, view, depth + 1);
                    }
                    return;
            }

            var ctx = batchFor(pen).Ctx;
            switch (e)
            {
                case CircleEntity c:
                    {
                        double r = c.Radius * Scale;
                        var cs = ToScreen(c.Center);
                        var p0 = new Point(cs.X + r, cs.Y);
                        var p1 = new Point(cs.X - r, cs.Y);
                        ctx.BeginFigure(p0, false, true);
                        ctx.ArcTo(p1, new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                        ctx.ArcTo(p0, new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                        return;
                    }
                case PolylineEntity pl:
                    AddPolyline(ctx, pl);
                    return;
                default:
                    AddPrims(ctx, e.Primitives());
                    return;
            }
        }

        /// <summary>Polyline: düz parçalar doğrudan köşelerden, yarım pikselden kısa adımlar atlanarak.</summary>
        private void AddPolyline(StreamGeometryContext ctx, PolylineEntity pl)
        {
            var v = pl.VertexView;
            int n = pl.SegmentCount;
            if (n == 0) return;
            var start = ToScreen(v[0].P);
            ctx.BeginFigure(start, false, false);
            var last = start;
            for (int i = 0; i < n; i++)
            {
                var a = v[i];
                var bv = v[(i + 1) % v.Count];
                var bp = ToScreen(bv.P);
                bool lastSeg = i == n - 1;
                if (Math.Abs(a.Bulge) < 1e-12)
                {
                    double dx = bp.X - last.X, dy = bp.Y - last.Y;
                    if (dx * dx + dy * dy < 0.36 && !lastSeg) continue;
                    ctx.LineTo(bp, true, false);
                    last = bp;
                }
                else
                {
                    var seg = pl.Segment(i);
                    if (seg is ArcPrim arc && arc.Radius * Scale >= 1)
                    {
                        if ((last - ToScreen(seg.StartPoint)).LengthSquared > 0.36) ctx.LineTo(ToScreen(seg.StartPoint), true, false);
                        double sweep = GeoUtil.Sweep(arc.Start, arc.End);
                        double r = arc.Radius * Scale;
                        var dir = arc.Reversed ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                        ctx.ArcTo(bp, new Size(r, r), 0, sweep > Math.PI, dir, true, false);
                    }
                    else ctx.LineTo(bp, true, false);
                    last = bp;
                }
            }
        }

        private void AddPrims(StreamGeometryContext ctx, IEnumerable<Prim> prims)
        {
            Point? cur = null;
            foreach (var pr in prims)
            {
                var s = ToScreen(pr.StartPoint);
                if (cur == null || (cur.Value - s).LengthSquared > 0.36)
                {
                    ctx.BeginFigure(s, false, false);
                    cur = s;
                }
                switch (pr)
                {
                    case LinePrim l:
                        {
                            var ep = ToScreen(l.B);
                            if ((ep - cur.Value).LengthSquared < 0.36) continue;
                            ctx.LineTo(ep, true, false);
                            cur = ep;
                            break;
                        }
                    case ArcPrim a:
                        {
                            double r = a.Radius * Scale;
                            double sweep = GeoUtil.Sweep(a.Start, a.End);
                            if (a.IsFull || sweep >= GeoUtil.TwoPi - 1e-9)
                            {
                                var opp = ToScreen(a.Center + (a.Center - pr.StartPoint));
                                ctx.ArcTo(opp, new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                                ctx.ArcTo(s, new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                                cur = s;
                                break;
                            }
                            var ep = ToScreen(a.EndPoint);
                            if (r < 1) ctx.LineTo(ep, true, false);
                            else
                            {
                                var dir = a.Reversed ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                                ctx.ArcTo(ep, new Size(r, r), 0, sweep > Math.PI, dir, true, false);
                            }
                            cur = ep;
                            break;
                        }
                }
            }
        }

        public void DrawEntity(DrawingContext dc, Entity e, Pen pen, Brush brush)
        {
            if (e is TextEntity t)
            {
                DrawText(dc, t, brush);
                return;
            }
            if (e is DimensionEntity dim)
            {
                DrawDimension(dc, dim, pen, brush);
                return;
            }
            if (e is HatchEntity hatch)
            {
                DrawHatch(dc, hatch, pen, brush, highlight: ReferenceEquals(pen, SelPen) || ReferenceEquals(pen, PreviewPen));
                return;
            }
            if (e is BlockRefEntity br)
            {
                bool fixedPen = ReferenceEquals(pen, SelPen) || ReferenceEquals(pen, PreviewPen) || Doc == null;
                foreach (var (child, layer, color, _) in br.DrawItems())
                {
                    if (fixedPen) { DrawEntity(dc, child, pen, brush); continue; }
                    var doc = Doc!;
                    if (doc.Layers.TryGetValue(layer, out var li) && !li.Visible) continue;
                    var col = color ?? (li != null ? li.Color : doc.ResolveColor(br));
                    var cp = GetPen(col);
                    DrawEntity(dc, child, cp, cp.Brush);
                }
                return;
            }
            if (e is CircleEntity c)
            {
                double r = c.Radius * Scale;
                dc.DrawEllipse(null, pen, ToScreen(c.Center), r, r);
                return;
            }
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                Point? cur = null;
                foreach (var pr in e.Primitives())
                {
                    var s = ToScreen(pr.StartPoint);
                    if (cur == null || (cur.Value - s).LengthSquared > 0.25)
                        ctx.BeginFigure(s, false, false);
                    switch (pr)
                    {
                        case LinePrim l:
                            ctx.LineTo(ToScreen(l.B), true, false);
                            cur = ToScreen(l.B);
                            break;
                        case ArcPrim a:
                            {
                                var ep = ToScreen(a.EndPoint);
                                double sweep = GeoUtil.Sweep(a.Start, a.End);
                                double r = a.Radius * Scale;
                                if (a.IsFull || sweep >= GeoUtil.TwoPi - 1e-9)
                                {
                                    var opp = ToScreen(a.Center + (a.Center - pr.StartPoint));
                                    ctx.ArcTo(opp, new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                                    ctx.ArcTo(s, new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                                    cur = s;
                                }
                                else
                                {
                                    // SweepDirection ekranda görünen yöndür; y ekseni ters çevrildiği için dünyadaki CCW ekranda da CCW görünür
                                    var dir = a.Reversed ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                                    ctx.ArcTo(ep, new Size(r, r), 0, sweep > Math.PI, dir, true, false);
                                    cur = ep;
                                }
                                break;
                            }
                    }
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private void DrawDimension(DrawingContext dc, DimensionEntity dim, Pen pen, Brush brush)
        {
            var g = dim.Build();
            foreach (var (a, b) in g.Lines) dc.DrawLine(pen, ToScreen(a), ToScreen(b));
            foreach (var arc in g.Arcs) DrawPrims(dc, new Prim[] { arc }, pen);
            foreach (var tri in g.Arrows)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(ToScreen(tri[0]), true, true);
                    ctx.LineTo(ToScreen(tri[1]), true, false);
                    ctx.LineTo(ToScreen(tri[2]), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, null, geo);
            }
            foreach (var (anchor, rot, h, text) in g.Texts)
                DrawCenteredText(dc, text, anchor, rot, h, brush);
        }

        /// <summary>Alt-orta noktası verilen yazıyı çizer.</summary>
        private void DrawCenteredText(DrawingContext dc, string text, Vec2 anchor, double rotation, double height, Brush brush)
        {
            double em = height * Scale / 0.7;
            if (em < 2 || em > 2000) return;
            var ft = Ft(text, em, brush);
            var sp = ToScreen(anchor);
            dc.PushTransform(new RotateTransform(-GeoUtil.RadToDeg(rotation), sp.X, sp.Y));
            dc.DrawText(ft, new Point(sp.X - ft.Width / 2, sp.Y - ft.Baseline));
            dc.Pop();
        }

        private void DrawHatch(DrawingContext dc, HatchEntity h, Pen pen, Brush brush, bool highlight)
        {
            if (h.IsSolid)
            {
                var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
                using (var ctx = geo.Open())
                {
                    foreach (var poly in h.LoopPolygons())
                    {
                        ctx.BeginFigure(ToScreen(poly[0]), true, true);
                        for (int i = 1; i < poly.Count; i++) ctx.LineTo(ToScreen(poly[i]), true, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(highlight ? null : brush, highlight ? pen : null, geo);
                return;
            }
            // Çok küçük ölçekte desen yerine yalnızca sınır
            double spacingPx = HatchEntity.BaseSpacing * h.Scale * Scale;
            if (spacingPx >= 2)
            {
                foreach (var (a, b) in h.PatternSegments())
                    dc.DrawLine(pen, ToScreen(a), ToScreen(b));
            }
            if (highlight || spacingPx < 2)
                foreach (var loop in h.Loops) DrawPrims(dc, HatchEntity.LoopPrims(loop), pen);
        }

        private void DrawPrims(DrawingContext dc, IEnumerable<Prim> prims, Pen pen)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                Point? cur = null;
                foreach (var pr in prims)
                {
                    var s = ToScreen(pr.StartPoint);
                    if (cur == null || (cur.Value - s).LengthSquared > 0.25)
                        ctx.BeginFigure(s, false, false);
                    if (pr is LinePrim l)
                    {
                        cur = ToScreen(l.B);
                        ctx.LineTo(cur.Value, true, false);
                    }
                    else if (pr is ArcPrim a)
                    {
                        var ep = ToScreen(a.EndPoint);
                        double sweep = GeoUtil.Sweep(a.Start, a.End);
                        double r = a.Radius * Scale;
                        var dir = a.Reversed ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                        ctx.ArcTo(ep, new Size(r, r), 0, sweep > Math.PI, dir, true, false);
                        cur = ep;
                    }
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        // Yazı biçimleme (FormattedText) pahalıdır; aynı metin/boyut/renk için yeniden kullanılır.
        private readonly Dictionary<(string Text, int Em, Brush Brush), FormattedText> _ftCache = new();

        private FormattedText Ft(string text, double em, Brush brush)
        {
            int q = (int)Math.Round(em * 4);                    // çeyrek piksel hassasiyet
            var key = (text, q, brush);
            if (_ftCache.TryGetValue(key, out var ft)) return ft;
            if (_ftCache.Count > 20000) _ftCache.Clear();
            ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, q / 4.0, brush,
                PixelsPerDip);
            _ftCache[key] = ft;
            return ft;
        }

        private void DrawText(DrawingContext dc, TextEntity t, Brush brush)
        {
            double em = t.Height * Scale / 0.7;
            var lines = t.Lines;
            if (em < 2)
            {
                // Çok küçük: yalnızca taban çizgileri
                var pen = new Pen(brush, 1);
                for (int i = 0; i < lines.Length; i++)
                {
                    var o = t.LineOrigin(i);
                    dc.DrawLine(pen, ToScreen(o), ToScreen(o + Vec2.Polar(t.LineWidth(lines[i]), t.Rotation)));
                }
                return;
            }
            if (em > 2000) return;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var ft = Ft(lines[i], em, brush);
                // Tahmini genişliğe sığdır (hizalama ve seçim kutusu tahmini genişliğe göre)
                double want = t.LineWidth(lines[i]) * Scale;
                double sx = ft.Width > 1e-6 ? Math.Clamp(want / ft.Width, 0.5, 2.0) : 1;
                var sp = ToScreen(t.LineOrigin(i));
                dc.PushTransform(new RotateTransform(-GeoUtil.RadToDeg(t.Rotation), sp.X, sp.Y));
                dc.PushTransform(new ScaleTransform(sx, 1, sp.X, sp.Y));
                dc.DrawText(ft, new Point(sp.X, sp.Y - ft.Baseline));
                dc.Pop();
                dc.Pop();
            }
        }
    }

    public void RedrawOverlay()
    {
        using var dc = _overlay.RenderOpen();
        if (_editor == null) return;
        var ed = _editor;

        if (ed.PreviewEntities.Count > 0)
        {
            PrepareBuilder(_uiBuilder);
            foreach (var e in ed.PreviewEntities) _uiBuilder.DrawEntity(dc, e, PreviewPen, PreviewPen.Brush);
        }

        var cur = ToScreen(ed.Cursor);
        if (ed.RubberBase is { } rb && ed.PreviewEntities.Count == 0)
            dc.DrawLine(RubberPen, ToScreen(rb), cur);

        if (ed.WindowStart is { } ws)
        {
            var a = ToScreen(ws);
            var b = _lastMouse;
            bool crossing = b.X < a.X;
            dc.DrawRectangle(crossing ? WindowFillC : WindowFillW, crossing ? WindowPenC : WindowPenW, new Rect(a, b));
        }

        // Yakalama izi: alınmış noktalar ve aktif hizalama çizgileri
        foreach (var tp in ed.TrackPoints)
        {
            var q = ToScreen(tp.P);
            dc.DrawLine(TrackMarkPen, new Point(q.X - 5, q.Y), new Point(q.X + 5, q.Y));
            dc.DrawLine(TrackMarkPen, new Point(q.X, q.Y - 5), new Point(q.X, q.Y + 5));
        }
        double far = (ActualWidth + ActualHeight) * 2 / _scale;
        foreach (var tl in ed.ActiveTrackLines)
        {
            // İz noktasından imleç tarafına doğru uzanan çizgi
            var dir = tl.Dir;
            if (Vec2.Dot(ed.Cursor - tl.Origin, dir) < 0) dir = -dir;
            dc.DrawLine(TrackPen, ToScreen(tl.Origin), ToScreen(tl.Origin + dir * far));
        }
        if (ed.TrackLabel is { } label && ed.ActiveTrackLines.Count > 0)
        {
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, TrackTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var at = new Point(cur.X + 16, cur.Y + 14);
            dc.DrawRectangle(TipBack, null, new Rect(at.X - 3, at.Y - 1, ft.Width + 6, ft.Height + 2));
            dc.DrawText(ft, at);
        }

        if (ed.ActiveSnap is { } snap) DrawSnapMarker(dc, ToScreen(snap.Point), snap.Kind);

        if (_mouseInside)
        {
            // Artı imleç + seçim kutusu
            var m = ed.Mode == InputMode.Point ? cur : _lastMouse;
            dc.DrawLine(CrossPen, new Point(0, m.Y), new Point(ActualWidth, m.Y));
            dc.DrawLine(CrossPen, new Point(m.X, 0), new Point(m.X, ActualHeight));
            if (ed.Mode != InputMode.Point)
                dc.DrawRectangle(null, CrossPen, new Rect(m.X - 5, m.Y - 5, 10, 10));
        }
    }

    private static void DrawSnapMarker(DrawingContext dc, Point p, SnapKind kind)
    {
        const double s = 7;
        switch (kind)
        {
            case SnapKind.Endpoint:
            case SnapKind.Origin:
                dc.DrawRectangle(null, SnapPen, new Rect(p.X - s, p.Y - s, 2 * s, 2 * s));
                break;
            case SnapKind.Midpoint:
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(p.X, p.Y - s), false, true);
                        c.LineTo(new Point(p.X + s, p.Y + s), true, false);
                        c.LineTo(new Point(p.X - s, p.Y + s), true, false);
                    }
                    dc.DrawGeometry(null, SnapPen, g);
                    break;
                }
            case SnapKind.Center:
                dc.DrawEllipse(null, SnapPen, p, s, s);
                break;
            case SnapKind.Quadrant:
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(p.X, p.Y - s), false, true);
                        c.LineTo(new Point(p.X + s, p.Y), true, false);
                        c.LineTo(new Point(p.X, p.Y + s), true, false);
                        c.LineTo(new Point(p.X - s, p.Y), true, false);
                    }
                    dc.DrawGeometry(null, SnapPen, g);
                    break;
                }
            case SnapKind.Tracking:
                dc.DrawLine(TrackMarkPen, new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y + s));
                dc.DrawLine(TrackMarkPen, new Point(p.X - s, p.Y + s), new Point(p.X + s, p.Y - s));
                break;
            case SnapKind.Intersection:
                dc.DrawLine(SnapPen, new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y + s));
                dc.DrawLine(SnapPen, new Point(p.X - s, p.Y + s), new Point(p.X + s, p.Y - s));
                break;
            default:
                dc.DrawRectangle(null, SnapPen, new Rect(p.X - s / 2, p.Y - s / 2, s, s));
                break;
        }
    }

    // ================================================================ Yardımcılar

    private static Pen GetPen(EntColor c)
    {
        lock (_penCache)
        {
            if (_penCache.TryGetValue(c, out var pen)) return pen;
            var col = Color.FromRgb(c.R, c.G, c.B);
            // Koyu zeminde siyah/çok koyu renkleri görünür yap
            if (c.R + c.G + c.B < 90) col = Colors.White;
            pen = FrozenPen(col, 1);
            _penCache[c] = pen;
            return pen;
        }
    }

    private static Pen FrozenPen(Color c, double thickness, bool dashed = false)
    {
        var p = new Pen(new SolidColorBrush(c), thickness);
        if (dashed) p.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
        p.Freeze();
        return p;
    }

    private static Brush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
