namespace DupFinder.Core;

/// <summary>
/// На каком томе лежит путь. В Windows это корень пути (C:\, \\server\share\), на macOS и Linux
/// корень всегда «/», поэтому том — самая длинная точка монтирования, с которой начинается путь
/// (/, /Volumes/Backup и т. п.).
/// </summary>
public static class Volumes
{
    private static readonly Lazy<string[]> MountPoints = new(LoadMountPoints);

    public static string RootOf(string path)
    {
        if (OperatingSystem.IsWindows())
            return Path.GetPathRoot(path) ?? "";
        return MountPoints.Value.FirstOrDefault(m => IsUnder(path, m)) ?? "/";
    }

    public static bool Same(string a, string b) =>
        string.Equals(RootOf(a), RootOf(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string mount) =>
        mount == "/" || (path.StartsWith(mount, StringComparison.OrdinalIgnoreCase)
                         && (path.Length == mount.Length || path[mount.Length] == '/'));

    /// <summary>Длинные — первыми, чтобы /Volumes/Backup выигрывал у /.</summary>
    private static string[] LoadMountPoints()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Select(d => d.RootDirectory.FullName.TrimEnd('/'))
                .Select(m => m.Length == 0 ? "/" : m)
                .Distinct()
                .OrderByDescending(m => m.Length)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ["/"];
        }
    }
}
