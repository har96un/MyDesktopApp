using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

public enum AuditAction
{
    None,
    Delete,
    MoveToLayer,
    Select
}

/// <summary>İçe aktarma denetimi sonucu: kullanıcı hangi şüpheli grupla ne yapılacağını seçer.</summary>
public sealed class AuditWindow : Window
{
    private readonly Dictionary<AuditReason, CheckBox> _checks = new();
    private readonly CheckBox _auto;

    public AuditAction Action { get; private set; } = AuditAction.None;
    public List<Entity> Chosen { get; } = new();
    public bool AuditOnOpen => _auto.IsChecked == true;

    public AuditWindow(Window owner, AuditResult result, string source, bool auditOnOpen)
    {
        Owner = owner;
        Title = "Çizim Denetimi";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = $"{source}: {result.Total} şüpheli nesne bulundu.",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Bunlar genellikle hatalı dışa aktarılmış bloklar, yardımcı/inşa çizgileri, üst üste kopyalanmış ya da " +
                   "boyutsuz nesnelerdir. İşlem uygulanacak grupları işaretleyin (işlem Ctrl+Z ile geri alınabilir):",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var (reason, list) in result.Items.OrderBy(k => k.Key))
        {
            var cb = new CheckBox
            {
                Content = $"{AuditResult.Describe(reason)}  ({list.Count})",
                IsChecked = reason != AuditReason.FarAway,   // uzak nesneler (ör. antet) varsayılan olarak işaretsiz
                Margin = new Thickness(0, 3, 0, 3)
            };
            _checks[reason] = cb;
            root.Children.Add(cb);
        }

        _auto = new CheckBox { Content = "Dosya açılınca otomatik denetle", IsChecked = auditOnOpen, Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.DimGray };
        root.Children.Add(_auto);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button B(string text, AuditAction a, string tip)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
            b.Click += (_, _) =>
            {
                Action = a;
                foreach (var (r, c) in _checks)
                    if (c.IsChecked == true) Chosen.AddRange(result.Items[r]);
                DialogResult = true;
            };
            return b;
        }
        buttons.Children.Add(B("Seç ve göster", AuditAction.Select, "İşaretli nesneleri seçer ve yakınlaştırır; ne yapacağınıza siz karar verin"));
        buttons.Children.Add(B("'_ŞÜPHELİ' katmanına taşı", AuditAction.MoveToLayer, "Silmeden kırmızı, gizli bir katmana taşır"));
        buttons.Children.Add(B("Sil", AuditAction.Delete, "İşaretli nesneleri siler"));
        var ignore = new Button { Content = "Yoksay", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        ignore.Click += (_, _) => Action = AuditAction.None;
        buttons.Children.Add(ignore);
        root.Children.Add(buttons);
        Content = root;
    }
}
