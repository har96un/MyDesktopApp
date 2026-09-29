using System.Globalization;
using System.Text;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

public enum InputMode
{
    Idle,
    Point,
    Selection,
    Text
}

public enum InputType
{
    Point,
    Number,
    Keyword,
    Enter,
    Text
}

public readonly record struct UserInput(InputType Type, Vec2 Point = default, double Number = 0, string Text = "");

/// <summary>Nokta isteği parametreleri.</summary>
public sealed class PointRequest
{
    public string Prompt { get; init; } = "";
    public Vec2? Base { get; init; }
    public Func<Vec2, IEnumerable<Entity>>? Preview { get; init; }
    public string[] Keywords { get; init; } = Array.Empty<string>();
    public bool AllowNumber { get; init; }
    public bool AllowEnter { get; init; }
}

/// <summary>
/// Çizim düzenleyicisi: komut satırı, kullanıcı girişi, yakalama (snap), seçim ve komutların çalıştırılması.
/// </summary>
public sealed partial class CadEditor
{
    public CadDocument Doc { get; }

    // Görünüm yardımcıları (canvas tarafından ayarlanır)
    public Func<double> PixelSizeProvider { get; set; } = () => 1.0;
    public Action? ZoomExtentsAction { get; set; }
    private double PixelSize => PixelSizeProvider();

    // Ayarlar
    public bool SnapEnabled { get; set; } = true;
    public bool OrthoEnabled { get; set; }
    public bool GridEnabled { get; set; } = true;

    // İmleç / çizim yardımcıları durumu
    public InputMode Mode { get; private set; } = InputMode.Idle;
    public Vec2 RawCursor { get; private set; }
    public Vec2 Cursor { get; private set; }
    public SnapPoint? ActiveSnap { get; private set; }
    public Vec2? RubberBase => Mode == InputMode.Point ? _request?.Base : null;
    public List<Entity> PreviewEntities { get; } = new();
    public Vec2? WindowStart { get; private set; }
    public bool IsBusy => _running != null;

    public string Prompt { get; private set; } = "Komut:";

    // Olaylar
    public event Action<string>? Message;
    public event Action? OverlayChanged;
    public event Action? SceneChanged;
    public event Action? StateChanged;
    public event Action? RequestFileNew;
    /// <summary>Komut yokken sağ tık (bağlam menüsü için).</summary>
    public event Action? IdleRightClick;

    private PointRequest? _request;
    private TaskCompletionSource<UserInput>? _pending;
    private Task? _running;
    private string _runningName = "";
    private string _lastCommand = "";
    private Vec2 _lastPoint = Vec2.Zero;
    private Vec2? _windowDownScreen;
    private bool _windowDragged;

    public CadEditor(CadDocument doc)
    {
        Doc = doc;
        Doc.Changed += (_, _) => SceneChanged?.Invoke();
        Doc.SelectionChanged += (_, _) => { SceneChanged?.Invoke(); StateChanged?.Invoke(); };
        RegisterCommands();
        _builtinAliases = new Dictionary<string, string>(_aliases);
    }

    public void Log(string msg) => Message?.Invoke(msg);

    private void SetPrompt(string p)
    {
        Prompt = p;
        StateChanged?.Invoke();
    }

    // ================================================================ Komut çalıştırma

    public IReadOnlyDictionary<string, CommandInfo> Commands => _commands;

    /// <summary>Komut satırına yazılan metni işler.</summary>
    public void Submit(string text)
    {
        text = text.Trim();
        if (_pending != null && Mode != InputMode.Idle)
        {
            if (!TryParseInput(text, out var input))
            {
                Log($"Geçersiz giriş: \"{text}\"");
                return;
            }
            if (!string.IsNullOrEmpty(text)) Log($"  {text}");
            Complete(input);
            return;
        }

        if (text.Length == 0)
        {
            if (!string.IsNullOrEmpty(_lastCommand)) _ = RunCommand(_lastCommand);
            return;
        }
        _ = RunCommand(text);
    }

    public async Task RunCommand(string name)
    {
        string key = Fold(name);
        if (!_aliases.TryGetValue(key, out var cmdName))
        {
            Log($"Bilinmeyen komut: \"{name}\". Komut listesi için YARDIM yazın.");
            return;
        }

        var prev = _running;
        if (prev != null)
        {
            CancelCommand();
            try { await prev; } catch { /* yoksay */ }
        }

        var info = _commands[cmdName];
        if (info.Repeatable) _lastCommand = cmdName;
        await RunInfo(info);
    }

    /// <summary>Kayıtlı olmayan (menüden/pencereden gelen) bir işlemi komut gibi çalıştırır.</summary>
    public async Task RunAdHoc(string name, Func<Task> handler)
    {
        var prev = _running;
        if (prev != null)
        {
            CancelCommand();
            try { await prev; } catch { /* yoksay */ }
        }
        await RunInfo(new CommandInfo(name, Array.Empty<string>(), name, handler, false));
    }

    private async Task RunInfo(CommandInfo info)
    {
        Log($"Komut: {info.Name}");
        _runningName = info.Name;
        var task = ExecuteAsync(info);
        // Hiç beklemeden (senkron) biten komutlarda ExecuteAsync'in finally bloğu _running'i zaten
        // temizlemiştir; burada tamamlanmış görevi atamak düzenleyiciyi "meşgul" bırakıyordu
        // (Esc seçimi bırakmıyor, sağ tık menüsü ve özellik düzenleme çalışmıyordu).
        if (!task.IsCompleted) _running = task;
        StateChanged?.Invoke();
        await task;
    }

    private async Task ExecuteAsync(CommandInfo info)
    {
        try
        {
            await info.Handler();
        }
        catch (OperationCanceledException)
        {
            Log("*İptal*");
        }
        catch (Exception ex)
        {
            Log("Hata: " + ex.Message);
        }
        finally
        {
            _running = null;
            _runningName = "";
            _request = null;
            _pending = null;
            Mode = InputMode.Idle;
            PreviewEntities.Clear();
            WindowStart = null;
            ActiveSnap = null;
            ClearTracking();
            SetPrompt("Komut:");
            SceneChanged?.Invoke();
            OverlayChanged?.Invoke();
        }
    }

    /// <summary>Esc: çalışan komutu iptal eder veya seçimi temizler.</summary>
    public void Cancel()
    {
        // Yarım kalmış pencere seçimi de seçim de aynı Esc ile temizlenir
        // (eskiden ilk Esc yalnızca pencereyi iptal ediyordu, seçim ikinci Esc'e kalıyordu).
        if (WindowStart != null)
        {
            WindowStart = null;
            _windowDownScreen = null;
            _windowDragged = false;
            OverlayChanged?.Invoke();
        }
        if (_pending != null)
        {
            bool selecting = Mode == InputMode.Selection;
            var p = _pending;
            _pending = null;
            p.TrySetCanceled();
            // Komut nesne seçimi beklerken iptal edildiyse o ana kadar seçilenleri de bırak
            if (selecting) Doc.ClearSelection();
            return;
        }
        if (_running == null) Doc.ClearSelection();
    }

    /// <summary>Çalışan komutu koşulsuz iptal eder.</summary>
    public void CancelCommand()
    {
        WindowStart = null;
        var p = _pending;
        _pending = null;
        p?.TrySetCanceled();
    }

    private void Complete(UserInput input)
    {
        var p = _pending;
        _pending = null;
        if (input.Type == InputType.Point)
        {
            _lastPoint = input.Point;
            ClearTracking();
        }
        p?.TrySetResult(input);
    }

    // ================================================================ Kullanıcıdan giriş isteme

    private Task<UserInput> Request(InputMode mode, string prompt, PointRequest? req = null)
    {
        _request = req;
        Mode = mode;
        _pending = new TaskCompletionSource<UserInput>();
        SetPrompt(prompt);
        UpdatePreview();
        OverlayChanged?.Invoke();
        return _pending.Task;
    }

    /// <summary>Nokta / sayı / anahtar kelime / Enter isteği.</summary>
    public Task<UserInput> GetInput(PointRequest req)
    {
        string prompt = req.Prompt;
        if (req.Keywords.Length > 0) prompt += " [" + string.Join("/", req.Keywords) + "]";
        return Request(InputMode.Point, prompt + ":", req);
    }

    /// <summary>Yalnızca nokta ister; Enter → null.</summary>
    public async Task<Vec2?> GetPoint(string prompt, Vec2? basePt = null, Func<Vec2, IEnumerable<Entity>>? preview = null, bool allowEnter = false)
    {
        while (true)
        {
            var r = await GetInput(new PointRequest { Prompt = prompt, Base = basePt, Preview = preview, AllowEnter = allowEnter });
            if (r.Type == InputType.Point) return r.Point;
            if (r.Type == InputType.Enter) return null;
        }
    }

    /// <summary>Serbest metin ister (boşluk dahil).</summary>
    public async Task<string> GetText(string prompt)
    {
        var r = await Request(InputMode.Text, prompt + ":");
        return r.Text;
    }

    /// <summary>Nesne seçimi ister. Önceden seçim varsa onu kullanır.</summary>
    public async Task<List<Entity>> GetSelection(string prompt = "Nesneleri seçin")
    {
        if (Doc.Selection.Count > 0)
        {
            var pre = Doc.Selection.ToList();
            Log($"  {pre.Count} nesne seçili.");
            return pre;
        }
        while (true)
        {
            var r = await Request(InputMode.Selection, prompt + " (bitirmek için Enter):");
            if (r.Type == InputType.Enter) break;
        }
        var list = Doc.Selection.ToList();
        if (list.Count == 0) throw new OperationCanceledException();
        Log($"  {list.Count} nesne seçildi.");
        return list;
    }

    // ================================================================ Metin ayrıştırma

    private bool TryParseInput(string text, out UserInput input)
    {
        input = default;
        if (Mode == InputMode.Text)
        {
            input = new UserInput(InputType.Text, Text: text);
            return true;
        }
        if (text.Length == 0)
        {
            if (Mode == InputMode.Selection || _request?.AllowEnter == true)
            {
                input = new UserInput(InputType.Enter);
                return true;
            }
            return false;
        }
        if (Mode == InputMode.Selection)
        {
            string f = Fold(text);
            if (f == "ALL" || f == "TUMU" || f == "T")
            {
                Doc.SetSelection(Doc.VisibleEntities);
                input = new UserInput(InputType.Enter);
                return true;
            }
            return false;
        }

        var req = _request;
        // Anahtar kelime
        if (req != null && req.Keywords.Length > 0)
        {
            string f = Fold(text);
            foreach (var kw in req.Keywords)
            {
                if (Fold(kw).StartsWith(f, StringComparison.Ordinal))
                {
                    input = new UserInput(InputType.Keyword, Text: kw);
                    return true;
                }
            }
        }

        // Nokta
        if (TryParsePoint(text, out var pt))
        {
            input = new UserInput(InputType.Point, Point: pt);
            return true;
        }

        // Sayı
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
        {
            if (req?.AllowNumber == true)
            {
                input = new UserInput(InputType.Number, Number: num);
                return true;
            }
            if (req?.Base is { } b)
            {
                // Doğrudan mesafe girişi: imleç yönünde
                var dir = (Cursor - b).Normalized();
                if (dir.LengthSquared < 1e-12) dir = Vec2.UnitX;
                input = new UserInput(InputType.Point, Point: b + dir * num);
                return true;
            }
        }
        return false;
    }

    private bool TryParsePoint(string text, out Vec2 p)
    {
        p = default;
        bool rel = text.StartsWith('@');
        string s = rel ? text[1..] : text;
        var basePt = _request?.Base ?? _lastPoint;
        var inv = CultureInfo.InvariantCulture;

        int lt = s.IndexOf('<');
        if (lt >= 0)
        {
            string ds = s[..lt], angS = s[(lt + 1)..];
            if (!double.TryParse(angS, NumberStyles.Float, inv, out double ang)) return false;
            double dist;
            if (ds.Length == 0) dist = Vec2.Distance(Cursor, basePt);
            else if (!double.TryParse(ds, NumberStyles.Float, inv, out dist)) return false;
            var v = Vec2.Polar(dist, GeoUtil.DegToRad(ang));
            // @d<a ve <a: temel noktaya göre; d<a: orijine göre (mutlak)
            p = (rel || ds.Length == 0) ? basePt + v : v;
            return true;
        }

        var parts = s.Split(',');
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, inv, out double x)) return false;
        if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, inv, out double y)) return false;
        p = rel ? basePt + new Vec2(x, y) : new Vec2(x, y);
        return true;
    }

    /// <summary>Türkçe karakterleri sadeleştirip büyük harfe çevirir.</summary>
    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Trim())
        {
            sb.Append(ch switch
            {
                'ç' or 'Ç' => 'C',
                'ğ' or 'Ğ' => 'G',
                'ı' or 'I' or 'i' or 'İ' => 'I',
                'ö' or 'Ö' => 'O',
                'ş' or 'Ş' => 'S',
                'ü' or 'Ü' => 'U',
                _ => char.ToUpperInvariant(ch)
            });
        }
        return sb.ToString();
    }

    // ================================================================ Fare olayları (canvas'tan)

    public void MouseMove(Vec2 world)
    {
        RawCursor = world;
        ActiveSnap = null;
        var c = world;

        ActiveTrackLines.Clear();
        TrackLabel = null;
        if (Mode == InputMode.Point)
        {
            if (SnapEnabled)
            {
                var s = FindSnap(world);
                if (s != null) { ActiveSnap = s; c = s.Value.Point; }
            }
            UpdateHover(ActiveSnap);
            if (ActiveSnap == null && SnapEnabled && ApplyTracking(world, out var tr))
            {
                c = tr;
                ActiveSnap = new SnapPoint(tr, SnapKind.Tracking);
            }
            if (OrthoEnabled && ActiveSnap == null && _request?.Base is { } b)
            {
                var d = c - b;
                c = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vec2(c.X, b.Y) : new Vec2(b.X, c.Y);
            }
        }
        Cursor = c;
        UpdatePreview();
        OverlayChanged?.Invoke();
    }

    private void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Mode == InputMode.Point && _request?.Preview != null)
        {
            try { PreviewEntities.AddRange(_request.Preview(Cursor)); }
            catch { /* önizleme hatası yoksayılır */ }
        }
    }

    /// <summary>Sol tık. screenPos piksel cinsinden, pencere sürükleme algısı için.</summary>
    public void LeftDown(Vec2 world, System.Windows.Point screenPos, bool shift, bool ctrl = false)
    {
        if (Mode == InputMode.Point)
        {
            Complete(new UserInput(InputType.Point, Point: Cursor));
            return;
        }
        if (Mode == InputMode.Text) return;

        // Idle veya Selection: seçim
        if (WindowStart is { } ws)
        {
            FinishWindow(ws, world, shift);
            return;
        }

        var hit = HitTest(world);
        if (hit != null)
        {
            // Ctrl+tık: grubun içinden tek nesne seç
            var targets = ctrl ? new[] { hit } : Doc.ExpandGroups(new[] { hit });
            foreach (var t in targets)
            {
                if (shift) Doc.Selection.Remove(t);
                else Doc.Selection.Add(t);
            }
            Doc.RaiseSelectionChanged();
            return;
        }
        WindowStart = world;
        _windowDownScreen = new Vec2(screenPos.X, screenPos.Y);
        _windowDragged = false;
        OverlayChanged?.Invoke();
    }

    public void LeftDrag(System.Windows.Point screenPos)
    {
        if (WindowStart != null && _windowDownScreen is { } d)
        {
            if ((new Vec2(screenPos.X, screenPos.Y) - d).Length > 6) _windowDragged = true;
        }
    }

    public void LeftUp(Vec2 world, bool shift)
    {
        if (WindowStart is { } ws && _windowDragged)
            FinishWindow(ws, world, shift);
    }

    private void FinishWindow(Vec2 start, Vec2 end, bool shift)
    {
        WindowStart = null;
        _windowDownScreen = null;
        var box = BBox.FromPoints(start, end);
        bool crossing = end.X < start.X;
        var found = Doc.ExpandGroups(Doc.VisibleEntities.Where(e => crossing ? e.CrossesWindow(box) : e.InsideWindow(box))).ToList();
        // Boş alana tıklama / boş pencere: seçimi temizle (Shift'siz)
        if (found.Count == 0 && !shift && Mode == InputMode.Idle)
        {
            Doc.ClearSelection();
            OverlayChanged?.Invoke();
            return;
        }
        foreach (var e in found)
        {
            if (shift) Doc.Selection.Remove(e);
            else Doc.Selection.Add(e);
        }
        Doc.RaiseSelectionChanged();
        OverlayChanged?.Invoke();
    }

    public void RightClick()
    {
        if (WindowStart != null) { WindowStart = null; OverlayChanged?.Invoke(); return; }
        if (Mode == InputMode.Selection || (Mode == InputMode.Point && _request?.AllowEnter == true))
        {
            Complete(new UserInput(InputType.Enter));
            return;
        }
        if (Mode == InputMode.Idle && _running == null)
        {
            if (IdleRightClick != null) IdleRightClick.Invoke();
            else if (!string.IsNullOrEmpty(_lastCommand)) _ = RunCommand(_lastCommand);
        }
    }

    public Entity? HitTest(Vec2 world)
    {
        double tol = PixelSize * 6;
        Entity? best = null;
        double bestD = tol;
        foreach (var e in Doc.VisibleEntities)
        {
            var b = e.Bounds();
            if (world.X < b.MinX - tol || world.X > b.MaxX + tol || world.Y < b.MinY - tol || world.Y > b.MaxY + tol) continue;
            double d = e.Distance(world);
            if (d <= bestD)
            {
                bestD = d;
                best = e;
            }
        }
        return best;
    }

    // ================================================================ Nesne yakalama

    private SnapPoint? FindSnap(Vec2 world)
    {
        double tol = PixelSize * 12;
        SnapPoint? best = null;
        double bestD = tol;

        void Consider(SnapPoint sp)
        {
            double d = Vec2.Distance(sp.Point, world);
            // Öncelik: uç/merkez noktaları kesişimden hafif önce
            if (d < bestD)
            {
                bestD = d;
                best = sp;
            }
        }

        Consider(new SnapPoint(Vec2.Zero, SnapKind.Origin));

        var near = new List<Entity>();
        foreach (var e in Doc.VisibleEntities)
        {
            var b = e.Bounds();
            if (world.X < b.MinX - tol || world.X > b.MaxX + tol || world.Y < b.MinY - tol || world.Y > b.MaxY + tol) continue;
            foreach (var sp in e.SnapPoints()) Consider(sp);
            if (e.Distance(world) < tol) near.Add(e);
        }

        // Kesişimler (yalnızca imlece yakın nesneler arasında)
        if (near.Count is > 0 and < 40)
        {
            var prims = near.SelectMany(e => e.Primitives()).Where(p => p.Distance(world) < tol).ToList();
            for (int i = 0; i < prims.Count; i++)
                for (int j = i + 1; j < prims.Count; j++)
                    foreach (var q in Prim.Intersect(prims[i], prims[j]))
                        Consider(new SnapPoint(q, SnapKind.Intersection));
        }
        return best;
    }

    // ================================================================ Yardımcılar

    public void ZoomExtents() => ZoomExtentsAction?.Invoke();

    public void NotifyDocumentChanged()
    {
        Doc.IsModified = true;
        Doc.RaiseChanged();
        StateChanged?.Invoke();
    }

    public void NewFile() => RequestFileNew?.Invoke();

    public string RunningCommandName => _runningName;
}
