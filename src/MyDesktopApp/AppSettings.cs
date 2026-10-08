using System.IO;
using System.Text.Json;

namespace MyDesktopApp;

/// <summary>Kullanıcı ayarları (%AppData%\ProfilCAD\settings.json).</summary>
public sealed class AppSettings
{
    // Genel
    public string CompanyName { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string LibraryFolder { get; set; } = "";
    public int AutosaveMinutes { get; set; } = 5;
    public string ReportAuthor { get; set; } = "";
    /// <summary>Farklı Kaydet'te varsayılan biçim.</summary>
    public string LastSaveFormat { get; set; } = "dxf2018";
    /// <summary>Dosya açılınca şüpheli nesne denetimi yapılsın mı.</summary>
    public bool AuditOnOpen { get; set; } = true;

    // Ölçü / tarama varsayılanları
    public double DimTextHeight { get; set; } = 2.5;
    public double DimArrowSize { get; set; } = 2.5;
    public int DimDecimals { get; set; } = 2;
    public string HatchPattern { get; set; } = "ANSI31";
    public double HatchScale { get; set; } = 1;

    // Birim: çizim her zaman mm saklanır; inç modunda gösterim ve giriş inçtir
    public bool InchMode { get; set; }
    /// <summary>İnç değerleri kesirli (1 3/8) yazılsın; false ise ondalık (1.375).</summary>
    public bool InchFractional { get; set; }
    /// <summary>Kesirli yazımda en küçük payda (2, 4, 8, 16, 32, 64, 128).</summary>
    public int InchDenominator { get; set; } = 64;

    // Son açılan dosyalar
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Kullanıcı tanımlı kısa adlar: kısa ad → komut adı.</summary>
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Klavye kısayolları: "Ctrl+Shift+T" → komut adı.</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public const int MaxRecent = 10;

    public static string AppDataDir
    {
        get
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProfilCAD");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsPath => System.IO.Path.Combine(AppDataDir, "settings.json");

    public string EffectiveLibraryFolder
    {
        get
        {
            var dir = string.IsNullOrWhiteSpace(LibraryFolder)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProfilCAD Kütüphane")
                : LibraryFolder;
            try { Directory.CreateDirectory(dir); } catch { /* yoksay */ }
            return dir;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts);
                if (s != null)
                {
                    // Büyük/küçük harf duyarsız sözlüklere çevir
                    s.Aliases = new Dictionary<string, string>(s.Aliases ?? new(), StringComparer.OrdinalIgnoreCase);
                    s.Shortcuts = new Dictionary<string, string>(s.Shortcuts ?? new(), StringComparer.OrdinalIgnoreCase);
                    s.RecentFiles ??= new();
                    return s;
                }
            }
        }
        catch { /* bozuk ayar dosyası: varsayılanlar */ }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { /* yoksay */ }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent) RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
        Save();
    }
}
