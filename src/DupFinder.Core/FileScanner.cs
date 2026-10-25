using System.IO.Enumeration;

namespace DupFinder.Core;

/// <summary>Обход каталогов без лишних обращений к диску: размер и дата берутся из записи каталога.</summary>
public static class FileScanner
{
    public static IEnumerable<FileEntry> Enumerate(
        string root, int rootIndex, ScanOptions options, ICollection<string> errors)
    {
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Junction и symlink не обходим: иначе возможны циклы и двойной учёт одних и тех же файлов.
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        var enumerable = new FileSystemEnumerable<FileEntry>(
            root,
            (ref FileSystemEntry e) => new FileEntry(
                e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcDateTime, rootIndex),
            enumOptions)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !e.IsDirectory && e.Length >= Math.Max(1, options.MinSize),
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
                !options.ExcludedDirectoryNames.Contains(e.FileName.ToString()),
        };

        using var enumerator = enumerable.GetEnumerator();
        while (true)
        {
            FileEntry current;
            try
            {
                if (!enumerator.MoveNext())
                    yield break;
                current = enumerator.Current;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{root}: {ex.Message}");
                yield break;
            }
            yield return current;
        }
    }
}
