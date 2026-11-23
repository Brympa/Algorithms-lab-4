using System.Windows;

namespace AlgorithmsLab.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Theme.Apply(dark: true);
        base.OnStartup(e);
    }
}
