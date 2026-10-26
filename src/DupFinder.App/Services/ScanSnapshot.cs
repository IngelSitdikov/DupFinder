using System.IO;
using System.Text.Json;
using DupFinder.Core;

namespace DupFinder.App.Services;

/// <summary>
/// Последний результат поиска в %LOCALAPPDATA%\DupFinder\lastscan.json — чтобы после перезапуска
/// не искать заново. Список может устареть, но это безопасно: перед любым действием файлы
/// сверяются с диском (<see cref="DuplicateResolver.CheckBothUnchanged"/>).
/// </summary>
public sealed class ScanSnapshot
{
    public required IReadOnlyList<string> Roots { get; init; }
    public required DateTime SavedUtc { get; init; }
    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }

    private static string FilePath => Path.Combine(AppSettings.DataFolder, "lastscan.json");

    /// <summary>null — сохранённого результата нет или файл не читается.</summary>
    public static ScanSnapshot? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            using var stream = File.OpenRead(FilePath);
            var snapshot = JsonSerializer.Deserialize<ScanSnapshot>(stream);
            return snapshot is { Roots.Count: > 0, Groups: not null } ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Пишет во временный файл и подменяет им старый: оборванная запись не испортит прошлый результат.</summary>
    public void Save()
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        var tmp = FilePath + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, this);
        File.Move(tmp, FilePath, overwrite: true);
    }
}
