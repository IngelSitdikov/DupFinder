using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using DupFinder.Presentation.Services;

namespace DupFinder.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // До создания первого окна: строки в XAML читаются один раз при загрузке.
        UiLanguage.Apply(AppSettings.Load().Language);

        // Привязки WPF форматируют даты и числа не по CurrentCulture, а по свойству Language элемента
        // (по умолчанию en-US) — выставляем его по региональным настройкам.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);
    }
}
