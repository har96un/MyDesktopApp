using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyDesktopApp.Geometry;
using MyDesktopApp.IO;
using MyDesktopApp.Model;

namespace MyDesktopApp.UI;

/// <summary>Dosyada 3B katı bulunduğunda hangi görünüşün (üst / ön / yan) alınacağını sordurur.</summary>
public sealed class SolidViewWindow : Window
{
    public SolidView? Chosen { get; private set; }

    public SolidViewWindow(Window owner, IReadOnlyList<SolidImport> solids, string source)
    {
        Owner = owner;
        Title = "3B Katı — Görünüş Seçimi";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"{source} dosyasında {solids.Count} adet 3B katı nesne var.",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });
        root.Children.Add(new TextBlock
        {
            Text = "Profil CAD 2B çalışır; katının kenarları seçtiğiniz yönden 2B çizgilere izdüşürülür.\n" +
                   "Profil kesiti için genellikle ekstrüzyon yönüne bakan görünüş doğrudur.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (view, name, axes) in new[]
                 {
                     (SolidView.Top, "Üstten", "X–Y düzlemi"),
                     (SolidView.Front, "Önden", "X–Z düzlemi"),
                     (SolidView.Side, "Yandan", "Y–Z düzlemi")
                 })
        {
            var ents = solids.SelectMany(s => CadFileIO.ProjectSolid(s, view)).ToList();
            var bb = BBox.Empty;
            foreach (var e in ents) bb.Add(e.Bounds());
            var sp = new StackPanel { Width = 160 };
            sp.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD8)),
                BorderThickness = new Thickness(1),
                Child = new Image { Source = Thumbnail.Render(ents, 150), Width = 150, Height = 150 },
                HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(new TextBlock
            {
                Text = $"{axes}\n{Vec2.Format(Math.Round(bb.Width, 2))} × {Vec2.Format(Math.Round(bb.Height, 2))}",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var btn = new Button
            {
                Content = sp,
                Margin = new Thickness(4),
                Padding = new Thickness(6),
                IsDefault = view == SolidView.Top
            };
            var v = view;
            btn.Click += (_, _) => { Chosen = v; DialogResult = true; };
            row.Children.Add(btn);
        }
        root.Children.Add(row);

        var cancel = new Button
        {
            Content = "Üstten görünüşle devam et",
            Padding = new Thickness(12, 3, 12, 3),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        root.Children.Add(cancel);
        Content = root;
    }
}
