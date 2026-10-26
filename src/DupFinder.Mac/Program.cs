using Avalonia;
using DupFinder.Presentation.Services;

namespace DupFinder.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // До создания окна: строки в разметке читаются один раз при загрузке.
        UiLanguage.Apply(AppSettings.Load().Language);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Нужен и дизайнеру Avalonia.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
