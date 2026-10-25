using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace DupFinder.App.Services;

public sealed record LanguageOption(string Code, string Name)
{
    // Это имя видят экранные дикторы и UI Automation у пунктов списка.
    public override string ToString() => Name;
}

/// <summary>
/// Язык выбирается один раз при запуске: строки в XAML берутся через x:Static и на лету не меняются.
/// Нейтральные ресурсы английские, поэтому при любом языке Windows, кроме русского, интерфейс английский.
/// </summary>
public static class UiLanguage
{
    public static IReadOnlyList<LanguageOption> Supported { get; } =
    [
        new("en", "English"),
        new("ru", "Русский"),
    ];

    /// <summary>Код языка, с которым запущена программа.</summary>
    public static string Current { get; private set; } = "en";

    public static void Apply(string? preferred)
    {
        var code = Supported.Any(l => l.Code == preferred)
            ? preferred!
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

        // Только язык интерфейса; числа и даты остаются по региональным настройкам Windows.
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Current = code;

        // Привязки WPF форматируют даты и числа не по CurrentCulture, а по свойству Language элемента
        // (по умолчанию en-US) — выставляем его по региональным настройкам.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
    }
}
