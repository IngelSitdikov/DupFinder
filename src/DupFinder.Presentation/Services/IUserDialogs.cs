namespace DupFinder.Presentation.Services;

public enum DialogKind
{
    /// <summary>Сообщение с кнопкой OK.</summary>
    Info,
    /// <summary>Предупреждение с кнопкой OK.</summary>
    Warning,
    /// <summary>Вопрос с кнопками OK / Отмена.</summary>
    OkCancel,
    /// <summary>Вопрос с кнопками Да / Нет.</summary>
    YesNo,
}

/// <summary>
/// Всё, что модели представления нужно от окна: диалоги и перезапуск. Свои реализации у WPF и Avalonia;
/// методы асинхронные, потому что диалоги Avalonia только такие.
/// </summary>
public interface IUserDialogs
{
    /// <summary>Выбор папки; null — отказались.</summary>
    Task<string?> PickFolderAsync(string title, string? current);

    /// <summary>Куда сохранить CSV; null — отказались.</summary>
    Task<string?> PickCsvFileAsync(string title, string suggestedName);

    /// <summary>true — нажали OK или Да.</summary>
    Task<bool> ShowAsync(string text, string title, DialogKind kind);

    /// <summary>Запустить программу заново и закрыть текущую.</summary>
    void Restart();
}
