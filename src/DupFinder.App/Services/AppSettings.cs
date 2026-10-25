using System.IO;
using System.Text.Json;

namespace DupFinder.App.Services;

/// <summary>Настройки пользователя в %LOCALAPPDATA%\DupFinder\settings.json, рядом с кэшем хешей.</summary>
public sealed class AppSettings
{
    /// <summary>Язык интерфейса ("en", "ru"); null — как у Windows.</summary>
    public string? Language { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupFinder", "settings.json");

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

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
