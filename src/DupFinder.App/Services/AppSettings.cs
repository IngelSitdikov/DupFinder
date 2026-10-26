using System.IO;
using System.Text.Json;

namespace DupFinder.App.Services;

/// <summary>Настройки пользователя в %LOCALAPPDATA%\DupFinder\settings.json, рядом с кэшем хешей.</summary>
public sealed class AppSettings
{
    /// <summary>Каталог данных программы: настройки, кэш хешей, последний результат поиска.</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupFinder");

    /// <summary>Язык интерфейса ("en", "ru"); null — как у Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Параметры последнего запущенного поиска.</summary>
    public string? RootA { get; set; }
    public string? RootB { get; set; }
    public bool? CrossOnly { get; set; }
    public int? MinSizeKb { get; set; }

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Испорченный или недоступный файл настроек не должен мешать запуску.
            return new AppSettings();
        }
    }

    /// <summary>Перечитать файл, поменять в нём своё и сохранить — чтобы не затереть остальные настройки.</summary>
    public static void Update(Action<AppSettings> change)
    {
        var settings = Load();
        change(settings);
        try
        {
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не сохранились настройки — не повод ронять программу.
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
