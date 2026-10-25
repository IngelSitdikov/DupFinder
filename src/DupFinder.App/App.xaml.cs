using System.Windows;
using DupFinder.App.Services;

namespace DupFinder.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // До создания первого окна: строки в XAML читаются один раз при загрузке.
        UiLanguage.Apply(AppSettings.Load().Language);
        base.OnStartup(e);
    }
}
