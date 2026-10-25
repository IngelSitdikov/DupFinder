namespace DupFinder.Core;

/// <summary>Файл, найденный при обходе.</summary>
/// <param name="RootIndex">Номер корня в <see cref="ScanOptions.Roots"/> (0 — диск A, 1 — диск B).</param>
public sealed record FileEntry(string Path, long Size, DateTime LastWriteUtc, int RootIndex);

/// <summary>Группа файлов с одинаковым содержимым.</summary>
public sealed record DuplicateGroup(long Size, string Hash, IReadOnlyList<FileEntry> Files)
{
    /// <summary>Сколько места освободится, если оставить одну копию.</summary>
    public long WastedBytes => Size * (Files.Count - 1);
}

public sealed class ScanOptions
{
    /// <summary>Один корень — ищем дубли внутри него; два — сравниваем между собой.</summary>
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>При двух корнях показывать только группы, где есть файлы с обеих сторон.</summary>
    public bool CrossRootsOnly { get; init; } = true;

    /// <summary>Файлы меньше этого размера не рассматриваются. Пустые не рассматриваются никогда.</summary>
    public long MinSize { get; init; } = 1;

    /// <summary>Кэш хешей между запусками; null — считать всё заново.</summary>
    public IHashCache? Cache { get; init; }

    /// <summary>Имена каталогов, в которые не заходим (сравнение без учёта регистра).</summary>
    public IReadOnlySet<string> ExcludedDirectoryNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System Volume Information", "$Recycle.Bin", "$RECYCLE.BIN", "RECYCLER", "$WinREAgent",
        };
}

public enum ScanPhase { Enumerating, PartialHashing, FullHashing, Done }

public sealed record ScanProgress(
    ScanPhase Phase,
    long FilesFound,
    long FilesProcessed,
    long FilesTotal,
    long BytesProcessed,
    long BytesTotal,
    string? CurrentPath);

public sealed record ScanResult(
    IReadOnlyList<DuplicateGroup> Groups,
    long FilesScanned,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed);
