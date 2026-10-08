using System.Globalization;

namespace MyDesktopApp.Geometry;

/// <summary>
/// Gösterim/giriş birimi. Çizim her zaman milimetre olarak saklanır; inç modunda yalnızca
/// ekranda gösterilen ve komut satırına yazılan uzunluklar inç olarak yorumlanır (1" = 25,4 mm).
/// Açılar ve ölçek katsayıları birimsizdir, dönüştürülmez.
/// </summary>
public static class Units
{
    public const double MmPerInch = 25.4;

    /// <summary>İnç modu açık mı.</summary>
    public static bool Inch { get; private set; }

    /// <summary>İnç değerleri kesirli mi yazılsın (1 3/8) yoksa ondalık mı (1.375).</summary>
    public static bool Fractional { get; private set; }

    /// <summary>Kesirli yazımda en küçük kesir paydası (2'nin kuvveti).</summary>
    public static int Denominator { get; private set; } = 64;

    /// <summary>Birim ayarı değişti (ekranlar yenilensin).</summary>
    public static event Action? Changed;

    public static void Set(bool inch, bool fractional, int denominator = 64)
    {
        denominator = denominator is 2 or 4 or 8 or 16 or 32 or 64 or 128 ? denominator : 64;
        if (Inch == inch && Fractional == fractional && Denominator == denominator) return;
        Inch = inch;
        Fractional = fractional;
        Denominator = denominator;
        Changed?.Invoke();
    }

    public static string Name => Inch ? "inç" : "mm";

    /// <summary>mm → gösterim birimi.</summary>
    public static double ToDisplay(double mm) => Inch ? mm / MmPerInch : mm;

    /// <summary>Gösterim birimi → mm.</summary>
    public static double FromDisplay(double v) => Inch ? v * MmPerInch : v;

    /// <summary>
    /// Uzunluğu geçerli birimde yazar. mm: en çok 4 ondalık. İnç: ondalık (varsayılan 4 basamak)
    /// ya da ayara göre kesirli. <paramref name="decimals"/> verilirse ondalık basamak sayısı odur.
    /// </summary>
    public static string FormatLength(double mm, int? decimals = null, bool unit = false)
    {
        if (!Inch)
            return decimals is { } d ? Fixed(mm, d) : Vec2.Format(mm);
        double v = mm / MmPerInch;
        string s = Fractional ? FormatFraction(v, Denominator) : decimals is { } dd ? Fixed(v, dd) : v.ToString("0.####", CultureInfo.InvariantCulture);
        return unit ? s + "\"" : s;
    }

    /// <summary>Nokta koordinatlarını geçerli birimde yazar ("x, y").</summary>
    public static string FormatPoint(Vec2 p) => $"{FormatLength(p.X)}, {FormatLength(p.Y)}";

    private static string Fixed(double v, int decimals)
    {
        string fmt = decimals <= 0 ? "0" : "0." + new string('#', Math.Min(8, decimals));
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }

    /// <summary>Kesirli inç: 1/<paramref name="den"/>'e yuvarlanır ve sadeleştirilir (ör. 1.375 → "1 3/8").</summary>
    public static string FormatFraction(double v, int den)
    {
        long total = (long)Math.Round(Math.Abs(v) * den, MidpointRounding.AwayFromZero);
        string sign = v < 0 && total != 0 ? "-" : "";
        long whole = total / den, num = total % den;
        long d = den;
        while (num != 0 && num % 2 == 0) { num /= 2; d /= 2; }
        if (num == 0) return sign + whole.ToString(CultureInfo.InvariantCulture);
        string frac = $"{num}/{d}";
        return whole == 0 ? sign + frac : $"{sign}{whole} {frac}";
    }

    /// <summary>
    /// Yazılan uzunluğu mm'ye çevirir. Kabul edilenler: 12.5 / 12,5, kesir (3/8), tam + kesir
    /// ("1 3/8" ya da "1-3/8"). Sona yazılan birim geçerli modu geçersiz kılar: 2" / 2in → inç, 25mm → mm.
    /// </summary>
    public static bool TryParseLength(string text, out double mm)
    {
        mm = 0;
        string s = text.Trim();
        if (s.Length == 0) return false;
        bool? inch = null;
        string lower = s.ToLowerInvariant();
        if (lower.EndsWith("mm")) { inch = false; s = s[..^2]; }
        else if (lower.EndsWith("in")) { inch = true; s = s[..^2]; }
        else if (s.EndsWith('"') || s.EndsWith('″')) { inch = true; s = s[..^1]; }
        s = s.Trim();
        if (!TryParseNumber(s, out double v)) return false;
        mm = (inch ?? Inch) ? v * MmPerInch : v;
        return true;
    }

    /// <summary>Ondalık sayı, kesir ya da tam sayı + kesir (birimsiz).</summary>
    public static bool TryParseNumber(string s, out double v)
    {
        v = 0;
        s = s.Trim();
        if (s.Length == 0) return false;
        var inv = CultureInfo.InvariantCulture;
        if (!s.Contains('/'))
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, inv, out v);

        bool neg = s.StartsWith('-');
        if (neg) s = s[1..].TrimStart();
        // "1 3/8" veya "1-3/8"
        double whole = 0;
        int sep = s.IndexOfAny(new[] { ' ', '-' });
        if (sep > 0)
        {
            if (!double.TryParse(s[..sep], NumberStyles.Float, inv, out whole)) return false;
            s = s[(sep + 1)..].Trim();
        }
        int slash = s.IndexOf('/');
        if (slash <= 0) return false;
        if (!double.TryParse(s[..slash], NumberStyles.Float, inv, out double num)) return false;
        if (!double.TryParse(s[(slash + 1)..], NumberStyles.Float, inv, out double den) || den == 0) return false;
        v = whole + num / den;
        if (neg) v = -v;
        return true;
    }
}
