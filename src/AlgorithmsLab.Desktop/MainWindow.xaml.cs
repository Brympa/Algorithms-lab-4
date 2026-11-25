using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace AlgorithmsLab.Desktop;

public partial class MainWindow : Window
{
    private bool closing;
    private bool readyToClose;

    public MainWindow()
    {
        InitializeComponent();
        ArraysNav.IsChecked = true;
        UpdateThemeLabel();
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        Arrays.Visibility = sender == ArraysNav ? Visibility.Visible : Visibility.Collapsed;
        Tables.Visibility = sender == TablesNav ? Visibility.Visible : Visibility.Collapsed;
        Words.Visibility = sender == WordsNav ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceScroll.ScrollToTop();
    }

    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        Theme.Apply(!Theme.IsDark);
        Arrays.RefreshTheme();
        Words.RefreshTheme();
        UpdateThemeLabel();
    }

    private void UpdateThemeLabel()
    {
        ThemeLabel.Text = Theme.IsDark ? "Светлая тема" : "Тёмная тема";
        ThemeIcon.Text = Theme.IsDark ? "\uE706" : "\uE708";
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (readyToClose)
            return;

        e.Cancel = true;
        if (closing)
            return;

        closing = true;
        IsEnabled = false;
        await Dispatcher.Yield();
        await Task.WhenAll(Arrays.CloseAsync(), Tables.CloseAsync(), Words.CloseAsync());
        readyToClose = true;
        Close();
    }
}
