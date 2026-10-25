using System.Collections.Concurrent;
using System.Diagnostics;
using DupFinder.Core.Resources;

namespace DupFinder.Core;

/// <summary>
/// Поиск дублей по нарастающей стоимости: размер → хеш начала и конца → полный хеш.
/// Каждый том читается в один поток (на HDD параллельное чтение только гоняет головку),
/// разные тома — параллельно.
/// </summary>
public static class DuplicateFinder
{
    public static Task<ScanResult> FindAsync(
        ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Find(options, progress, ct), ct);

    public static ScanResult Find(
        ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            return FindCore(options, progress, ct);
        }
        finally
        {
            // И при отмене тоже: посчитанное к этому моменту пригодится в следующий раз.
            options.Cache?.Flush();
        }
    }

    private static ScanResult FindCore(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var cache = options.Cache;
        if (options.Roots.Count is < 1 or > 2)
            throw new ArgumentException(Strings.RootsCount, nameof(options));

        var sw = Stopwatch.StartNew();
        var errors = new ConcurrentQueue<string>();
        var reporter = new ProgressReporter(progress);
        var crossOnly = options.CrossRootsOnly && options.Roots.Count == 2;

        // 1. Обход.
        reporter.Begin(ScanPhase.Enumerating, 0, 0);
        var files = EnumerateAll(options, errors, reporter, ct);
        var filesScanned = files.Count;

        // 2. Группировка по размеру.
        var candidates = files
            .GroupBy(f => f.Size)
            .Where(g => IsDuplicateSet(g, crossOnly))
            .SelectMany(g => g)
            .ToList();
        files = null;

        // 3. Частичный хеш + отсев жёстких ссылок.
        reporter.Begin(ScanPhase.PartialHashing, candidates.Count, candidates.Sum(f => Math.Min(f.Size, 2L * FileHasher.PartialChunk)));
        var partial = new ConcurrentDictionary<FileEntry, (string Hash, (uint, ulong)? Id)>();
        RunPerVolume(candidates, f =>
        {
            reporter.Current(f.Path);
            var hash = cache?.GetPartial(f);
            if (hash is null)
            {
                hash = FileHasher.ComputePartial(f.Path, f.Size, ct);
                cache?.SetPartial(f, hash);
            }
            // Идентификатор не кэшируем: после замены на жёсткую ссылку дата у файла может не измениться,
            // а идентификатор — уже другой.
            partial[f] = (hash, FileIdentity.TryGet(f.Path));
            reporter.FileDone(Math.Min(f.Size, 2L * FileHasher.PartialChunk));
        }, errors, ct);

        var afterPartial = candidates
            .Where(partial.ContainsKey)
            .GroupBy(f => (f.Size, partial[f].Hash))
            .Select(g => RemoveHardLinks(g, f => partial[f].Id))
            .Where(g => IsDuplicateSet(g, crossOnly))
            .ToList();

        // 4. Полный хеш — только там, где частичный не покрыл файл целиком.
        var needFull = afterPartial
            .Where(g => !FileHasher.PartialCoversWholeFile(g[0].Size))
            .SelectMany(g => g)
            .ToList();
        reporter.Begin(ScanPhase.FullHashing, needFull.Count, needFull.Sum(f => f.Size));
        var full = new ConcurrentDictionary<FileEntry, string>();
        RunPerVolume(needFull, f =>
        {
            reporter.Current(f.Path);
            var hash = cache?.GetFull(f);
            if (hash is null)
            {
                hash = FileHasher.ComputeFull(f.Path, reporter.AddBytes, ct);
                cache?.SetFull(f, hash);
                reporter.FileDone(0);
            }
            else
            {
                reporter.FileDone(f.Size);
            }
            full[f] = hash;
        }, errors, ct);

        var groups = afterPartial
            .SelectMany(g => FileHasher.PartialCoversWholeFile(g[0].Size)
                ? [g.Select(f => (File: f, Hash: partial[f].Hash)).ToList()]
                : g.Where(full.ContainsKey).Select(f => (File: f, Hash: full[f])).GroupBy(x => x.Hash).Select(x => x.ToList()))
            .Where(g => IsDuplicateSet(g.Select(x => x.File), crossOnly))
            .Select(g => new DuplicateGroup(
                g[0].File.Size,
                g[0].Hash,
                g.Select(x => x.File).OrderBy(f => f.RootIndex).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderByDescending(g => g.WastedBytes)
            .ToList();

        reporter.Begin(ScanPhase.Done, 0, 0);
        return new ScanResult(groups, filesScanned, errors.ToList(), sw.Elapsed);
    }

    private static List<FileEntry> EnumerateAll(
        ScanOptions options, ConcurrentQueue<string> errors, ProgressReporter reporter, CancellationToken ct)
    {
        var perRoot = new List<FileEntry>[options.Roots.Count];
        Parallel.For(0, options.Roots.Count, i =>
        {
            var list = new List<FileEntry>();
            var errs = new List<string>();
            foreach (var f in FileScanner.Enumerate(options.Roots[i], i, options, errs))
            {
                if (ct.IsCancellationRequested)
                    break;
                list.Add(f);
                reporter.Found(f.Path);
            }
            foreach (var e in errs) errors.Enqueue(e);
            perRoot[i] = list;
        });
        ct.ThrowIfCancellationRequested();

        // Если корни вложены друг в друга, один и тот же путь встретится дважды — оставляем первый.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return perRoot.SelectMany(l => l).Where(f => seen.Add(f.Path)).ToList();
    }

    private static bool IsDuplicateSet(IEnumerable<FileEntry> files, bool crossOnly)
    {
        var list = files as IReadOnlyCollection<FileEntry> ?? files.ToList();
        if (list.Count < 2)
            return false;
        return !crossOnly || list.Select(f => f.RootIndex).Distinct().Count() > 1;
    }

    private static List<FileEntry> RemoveHardLinks(IEnumerable<FileEntry> group, Func<FileEntry, (uint, ulong)?> idOf)
    {
        var seen = new HashSet<(uint, ulong)>();
        return group.Where(f => idOf(f) is not { } id || seen.Add(id)).ToList();
    }

    private static void RunPerVolume(
        IReadOnlyList<FileEntry> files, Action<FileEntry> work, ConcurrentQueue<string> errors, CancellationToken ct)
    {
        var byVolume = files
            .GroupBy(f => Path.GetPathRoot(f.Path) ?? "", StringComparer.OrdinalIgnoreCase)
            // Внутри тома — по пути: соседние файлы обычно лежат рядом и на диске.
            .Select(g => g.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();

        // Отмену не бросаем изнутри Parallel (она приехала бы завёрнутой в AggregateException),
        // а выходим из циклов и бросаем один раз снаружи.
        Parallel.ForEach(byVolume, volumeFiles =>
        {
            foreach (var f in volumeFiles)
            {
                if (ct.IsCancellationRequested)
                    return;
                try
                {
                    work(f);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Enqueue($"{f.Path}: {ex.Message}");
                }
            }
        });
        ct.ThrowIfCancellationRequested();
    }
}
