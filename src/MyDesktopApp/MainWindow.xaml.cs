using System.Windows;

namespace MyDesktopApp;

public partial class MainWindow : Window
{
    private int _clickCount;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        _clickCount++;
        GreetingText.Text = $"{_clickCount} kez tıkladın";
    }
}
