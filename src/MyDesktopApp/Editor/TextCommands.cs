using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.Editor;

/// <summary>Yazı penceresinin değerleri (açı derece).</summary>
public sealed record TextSpec(string Value, double Height, double RotationDeg, TextAlign Align);

/// <summary>Metin ekleme / düzenleme (çok satırlı, hizalamalı).</summary>
public sealed partial class CadEditor
{
    private TextAlign _lastAlign = TextAlign.BottomLeft;

    /// <summary>
    /// Arayüzün metin penceresi (null dönerse iptal). Atanmamışsa komut satırından sorulur.
    /// Parametre: başlangıç değerleri, başlık.
    /// </summary>
    public Func<TextSpec, string, TextSpec?>? TextDialog { get; set; }

    private void RegisterTextCommands()
    {
        Reg("MTEXT", "Çok satırlı metin ekler (pencere ile)", CmdMText, "MT", "METIN");
        Reg("TEXTEDIT", "Seçili yazıyı düzenler (yazıya çift tıklamak da olur)", CmdTextEdit, "ED", "DDEDIT", "METINDUZENLE");
    }

    /// <summary>Metin penceresi açılır, sonra yazı imleçle yerleştirilir.</summary>
    private async Task CmdMText()
    {
        if (TextDialog == null) { await CmdText(); return; }
        var spec = TextDialog(new TextSpec("", _lastTextHeight, 0, _lastAlign), "Metin Ekle");
        if (spec == null || string.IsNullOrWhiteSpace(spec.Value)) return;
        _lastTextHeight = spec.Height;
        _lastAlign = spec.Align;
        TextEntity Make(Vec2 at) => new(at, spec.Height, GeoUtil.DegToRad(spec.RotationDeg), spec.Value.TrimEnd()) { Align = spec.Align };
        var p = await GetPoint("Yazının konumu (hizalama noktası)", null, c => new Entity[] { Make(c) });
        if (p is not { } pos) return;
        AddEntity(Make(pos));
        Log($"  Metin eklendi ({Make(pos).Lines.Length} satır).");
    }

    private async Task CmdTextEdit()
    {
        var t = Doc.Selection.OfType<TextEntity>().FirstOrDefault();
        if (t == null)
        {
            var p = await GetPoint("Düzenlenecek yazıyı seçin");
            if (p is not { } pt) return;
            t = HitTest(pt) as TextEntity;
            if (t == null) { Log("  Yazı bulunamadı."); return; }
        }
        ApplyTextEdit(t);
    }

    /// <summary>Yazıya çift tıklandığında (boşta) düzenleme penceresi.</summary>
    public void EditText(TextEntity t)
    {
        if (IsBusy) return;
        _ = RunAdHoc("METİN DÜZENLE", () => { ApplyTextEdit(t); return Task.CompletedTask; });
    }

    private void ApplyTextEdit(TextEntity t)
    {
        if (TextDialog == null) { Log("  Metin penceresi yok."); return; }
        var spec = TextDialog(new TextSpec(t.Value, t.Height, GeoUtil.RadToDeg(t.Rotation), t.Align), "Metni Düzenle");
        if (spec == null || string.IsNullOrWhiteSpace(spec.Value)) return;
        Doc.SaveUndo();
        t.Value = spec.Value.TrimEnd();
        t.Height = spec.Height;
        t.Rotation = GeoUtil.NormalizeAngle(GeoUtil.DegToRad(spec.RotationDeg));
        t.Align = spec.Align;
        _lastTextHeight = spec.Height;
        NotifyDocumentChanged();
        Doc.RaiseSelectionChanged();
        Log("  Yazı güncellendi.");
    }

    /// <summary>Boşta çift tık: yazıysa düzenle.</summary>
    public bool LeftDoubleClick(Vec2 world)
    {
        if (Mode != InputMode.Idle || IsBusy || WindowStart != null) return false;
        if (HitTest(world) is not TextEntity t) return false;
        EditText(t);
        return true;
    }
}
