using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DupFinder.App.Services;
using DupFinder.Core;
using Microsoft.Win32;

namespace DupFinder.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cts;
    private SqliteHashCache? _cache;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRootB))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(KeepOnACommand), nameof(KeepOnBCommand))]
    private string _rootA = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRootB))]
    [NotifyCanExecuteChangedFor(nameof(KeepOnACommand), nameof(KeepOnBCommand))]
    private string _rootB = "";

    [ObservableProperty]
    private bool _crossOnly = true;

    [ObservableProperty]
    private int _minSizeKb = 1;

    [ObservableProperty]
    private bool _useCache = true;

    [ObservableProperty]
    private bool _verifyBeforeDelete = true;

    [ObservableProperty]
    private string _cacheInfo = "";

    /// <summary>Идёт сканирование или обработка отмеченных файлов.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(DeleteMarkedCommand), nameof(HardLinkMarkedCommand),
        nameof(ClearCacheCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _progressIndeterminate;

    [ObservableProperty]
    private string _status = Strings.StatusInitial;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteMarkedCommand), nameof(HardLinkMarkedCommand))]
    private long _markedBytes;

    private int _markedCount;
    private bool _bulkMarking;

    public MainViewModel()
    {
        _selectedLanguage = Languages.First(l => l.Code == UiLanguage.Current);
        UpdateCacheInfo();
    }

    public IReadOnlyList<LanguageOption> Languages => UiLanguage.Supported;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    /// <summary>
    /// Выбор сохраняется сразу, а применяется при запуске: строки в XAML не обновляются на лету.
    /// Вопрос о перезапуске задаётся уже на выбранном языке.
    /// </summary>
    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        new AppSettings { Language = value.Code }.Save();
        if (value.Code == UiLanguage.Current)
            return;
        var question = Strings.ResourceManager.GetString(nameof(Strings.RestartForLanguage),
            CultureInfo.GetCultureInfo(value.Code)) ?? Strings.RestartForLanguage;
        if (MessageBox.Show(question, Strings.WindowTitle, MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    public ObservableCollection<GroupItem> Groups { get; } = [];
    public ObservableCollection<string> Errors { get; } = [];

    public bool HasRootB => !string.IsNullOrWhiteSpace(RootB);
    public bool IsIdle => !IsBusy;

    public string MarkedText => _markedCount == 0
        ? Strings.NothingMarked
        : string.Format(Strings.Marked, Format.Count(_markedCount, Strings.FileForms), Format.Bytes(MarkedBytes));

    [RelayCommand]
    private void BrowseA() => RootA = PickFolder(RootA) ?? RootA;

    [RelayCommand]
    private void BrowseB() => RootB = PickFolder(RootB) ?? RootB;

    [RelayCommand]
    private void ClearB() => RootB = "";

    private static string? PickFolder(string current)
    {
        var dialog = new OpenFolderDialog { Title = Strings.PickFolderTitle };
        if (Directory.Exists(current))
            dialog.InitialDirectory = current;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    // --- Кэш ---

    private SqliteHashCache? GetCache()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            return _cache = new SqliteHashCache(SqliteHashCache.DefaultPath);
        }
        catch (Exception ex)
        {
            Errors.Add(string.Format(Strings.CacheUnavailable, ex.Message));
            return null;
        }
    }

    private void UpdateCacheInfo()
    {
        var path = SqliteHashCache.DefaultPath;
        if (!File.Exists(path))
        {
            CacheInfo = Strings.CacheEmpty;
            return;
        }
        var size = Format.Bytes(new FileInfo(path).Length);
        CacheInfo = _cache is null
            ? string.Format(Strings.CacheInfo, size)
            : string.Format(Strings.CacheInfoWithCount, Format.Count(_cache.Count, Strings.FileForms), size);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearCache()
    {
        GetCache()?.Clear();
        UpdateCacheInfo();
        Status = Strings.CacheCleared;
    }

    // --- Сканирование ---

    private bool CanScan() => !IsBusy && !string.IsNullOrWhiteSpace(RootA);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var roots = new List<string> { RootA.Trim() };
        if (HasRootB)
            roots.Add(RootB.Trim());
        var missing = roots.FirstOrDefault(r => !Directory.Exists(r));
        if (missing is not null)
        {
            Status = string.Format(Strings.NoSuchFolder, missing);
            return;
        }

        Groups.Clear();
        Errors.Clear();
        RecalcMarks();
        Summary = "";
        var options = new ScanOptions
        {
            Roots = roots,
            CrossRootsOnly = CrossOnly,
            MinSize = Math.Max(1, MinSizeKb) * 1024L,
            Cache = UseCache ? GetCache() : null,
        };

        await RunBusyAsync(async ct =>
        {
            var result = await DuplicateFinder.FindAsync(options, new Progress<ScanProgress>(OnScanProgress), ct);

            foreach (var g in result.Groups)
                Groups.Add(new GroupItem(g, RecalcMarks));
            foreach (var e in result.Errors)
                Errors.Add(e);

            var elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            var scanned = Format.Count(result.FilesScanned, Strings.FileForms);
            Status = result.Errors.Count > 0
                ? string.Format(Strings.ScanDoneWithErrors, elapsed, scanned, result.Errors.Count)
                : string.Format(Strings.ScanDone, elapsed, scanned);
            UpdateSummary();
        }, Strings.ScanStopped);
        UpdateCacheInfo();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunBusyAsync(Func<CancellationToken, Task> work, string cancelledStatus)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = cancelledStatus;
        }
        finally
        {
            IsBusy = false;
            ProgressIndeterminate = false;
            ProgressPercent = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnScanProgress(ScanProgress p)
    {
        ProgressIndeterminate = p.Phase == ScanPhase.Enumerating;
        ProgressPercent = p.BytesTotal > 0 ? 100.0 * p.BytesProcessed / p.BytesTotal : 0;
        Status = p.Phase switch
        {
            ScanPhase.Enumerating => string.Format(Strings.ProgressEnumerating,
                Format.Count(p.FilesFound, Strings.FileForms), p.CurrentPath),
            ScanPhase.PartialHashing => string.Format(Strings.ProgressPartial,
                p.FilesProcessed, p.FilesTotal, p.CurrentPath),
            ScanPhase.FullHashing => string.Format(Strings.ProgressFull,
                p.FilesProcessed, p.FilesTotal, Format.Bytes(p.BytesProcessed), Format.Bytes(p.BytesTotal), p.CurrentPath),
            _ => Status,
        };
    }

    // --- Отметки ---

    private bool CanKeepOnSide() => HasRootB && Groups.Count > 0;

    /// <summary>Оставить копии на диске A, отметить копии на B (только в группах, где на A копия есть).</summary>
    [RelayCommand(CanExecute = nameof(CanKeepOnSide))]
    private void KeepOnA() => MarkWhere(g => g.Files.Any(f => f.Entry.RootIndex == 0), f => f.Entry.RootIndex == 1);

    [RelayCommand(CanExecute = nameof(CanKeepOnSide))]
    private void KeepOnB() => MarkWhere(g => g.Files.Any(f => f.Entry.RootIndex == 1), f => f.Entry.RootIndex == 0);

    /// <summary>В каждой группе оставить самый старый файл, остальные отметить.</summary>
    [RelayCommand]
    private void KeepOldest() => Bulk(() =>
    {
        foreach (var g in Groups)
        {
            var keep = g.Files.MinBy(f => f.Entry.LastWriteUtc);
            foreach (var f in g.Files)
                f.IsMarked = f != keep;
        }
    });

    [RelayCommand]
    private void ClearMarks() => Bulk(() =>
    {
        foreach (var f in Groups.SelectMany(g => g.Files))
            f.IsMarked = false;
    });

    private void MarkWhere(Func<GroupItem, bool> groupFilter, Func<FileItem, bool> mark) => Bulk(() =>
    {
        foreach (var g in Groups)
        {
            var apply = groupFilter(g);
            foreach (var f in g.Files)
                f.IsMarked = apply && mark(f);
        }
    });

    /// <summary>Массовая расстановка отметок: пересчёт итога один раз в конце, а не на каждый файл.</summary>
    private void Bulk(Action action)
    {
        _bulkMarking = true;
        try { action(); }
        finally { _bulkMarking = false; }
        RecalcMarks();
    }

    private void RecalcMarks()
    {
        if (_bulkMarking)
            return;
        var marked = Groups.SelectMany(g => g.Files).Where(f => f.IsMarked).ToList();
        _markedCount = marked.Count;
        MarkedBytes = marked.Sum(f => f.Entry.Size);
        OnPropertyChanged(nameof(MarkedText));
    }

    private void UpdateSummary()
    {
        var wasted = Groups.Sum(g => g.WastedBytes);
        Summary = Groups.Count == 0
            ? Strings.NoDuplicates
            : string.Format(Strings.Summary, Format.Count(Groups.Count, Strings.GroupForms), Format.Bytes(wasted));
        KeepOnACommand.NotifyCanExecuteChanged();
        KeepOnBCommand.NotifyCanExecuteChanged();
    }

    // --- Действия над отмеченными ---

    private bool CanProcess() => !IsBusy && MarkedBytes > 0;

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private Task DeleteMarkedAsync() => ProcessMarkedAsync(hardLink: false);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private Task HardLinkMarkedAsync() => ProcessMarkedAsync(hardLink: true);

    private async Task ProcessMarkedAsync(bool hardLink)
    {
        // Защита: в каждой группе должна остаться хотя бы одна неотмеченная копия.
        var fullyMarked = Groups.Where(g => g.Files.All(f => f.IsMarked)).ToList();
        if (fullyMarked.Count > 0)
        {
            MessageBox.Show(
                string.Format(Strings.AllMarked, Format.Count(fullyMarked.Count, Strings.GroupInForms),
                    fullyMarked[0].Files[0].Path),
                Strings.DialogTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Для каждого отмеченного — копия, которая остаётся. Для ссылки она должна быть на том же томе.
        var plan = Groups
            .SelectMany(g => g.Files.Where(f => f.IsMarked).Select(f => (File: f, Keep: g.Files.FirstOrDefault(k =>
                !k.IsMarked && (!hardLink || DuplicateResolver.SameVolume(k.Path, f.Path))))))
            .ToList();
        var noPair = plan.Where(p => p.Keep is null).Select(p => p.File).ToList();
        var work = plan.Where(p => p.Keep is not null).Select(p => (p.File, Keep: p.Keep!)).ToList();

        if (!Confirm(hardLink, work, noPair))
            return;

        var progress = new Progress<(int Done, string Path)>(p =>
        {
            ProgressPercent = 100.0 * p.Done / work.Count;
            Status = string.Format(hardLink ? Strings.ProgressLinking : Strings.ProgressDeleting,
                p.Done, work.Count, p.Path);
        });
        var verify = VerifyBeforeDelete;
        var results = new List<(FileItem File, string? Error)>();

        await RunBusyAsync(ct => Task.Run(() =>
        {
            IProgress<(int, string)> report = progress;
            foreach (var (file, keep) in work)
            {
                if (ct.IsCancellationRequested)
                    break;
                report.Report((results.Count, file.Path));
                try
                {
                    var error = hardLink
                        ? DuplicateResolver.ReplaceWithHardLink(file.Entry, keep.Entry, ct)
                        : FileActions.MoveToRecycleBin(file.Entry, keep.Entry, verify, ct);
                    results.Add((file, error));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None), Strings.Stopped);

        // Результаты применяем в UI-потоке, в том числе после отмены — то, что уже сделано, сделано.
        foreach (var f in noPair)
            Errors.Add(string.Format(Strings.NoPairError, f.Path));
        foreach (var (file, error) in results)
        {
            if (error is null)
                file.Group.Files.Remove(file);
            else
                Errors.Add($"{file.Path}: {error}");
        }
        foreach (var g in Groups.Where(g => g.Files.Count < 2).ToList())
            Groups.Remove(g);
        RecalcMarks();
        UpdateSummary();

        var ok = results.Count(r => r.Error is null);
        var failed = results.Count - ok + noPair.Count;
        var notDone = work.Count - results.Count;
        Status = string.Format(hardLink ? Strings.DoneLinked : Strings.DoneDeleted, ok)
                 + (failed == 0 ? "." : string.Format(Strings.DoneFailedTail, failed))
                 + (notDone > 0 ? string.Format(Strings.NotProcessedTail, notDone) : "");
    }

    private bool Confirm(bool hardLink, List<(FileItem File, FileItem Keep)> work, List<FileItem> noPair)
    {
        if (work.Count == 0)
        {
            MessageBox.Show(
                hardLink ? Strings.NoSameVolumeCopies : Strings.NothingToProcess,
                Strings.DialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var bytes = work.Sum(w => w.File.Entry.Size);
        var count = $"{Format.Count(work.Count, Strings.FileForms)} ({Format.Bytes(bytes)})";
        var text = new StringBuilder();
        if (hardLink)
        {
            text.AppendFormat(Strings.ConfirmLinks, count);
        }
        else
        {
            text.AppendFormat(Strings.ConfirmDelete, count);
            if (VerifyBeforeDelete)
                text.Append(Strings.ConfirmDeleteVerify);
            var removable = work.Count(w => FileActions.IsOnRemovableDrive(w.File.Path));
            if (removable > 0)
                text.AppendFormat(Strings.ConfirmRemovable, removable);
        }
        if (noPair.Count > 0)
            text.AppendFormat(Strings.ConfirmSkipped, noPair.Count);

        return MessageBox.Show(text.ToString(), Strings.DialogTitle, MessageBoxButton.OKCancel, MessageBoxImage.Question)
               == MessageBoxResult.OK;
    }

    [RelayCommand]
    private static void ShowInExplorer(FileItem? file)
    {
        if (file is not null)
            FileActions.ShowInExplorer(file.Path);
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.SaveReportTitle,
            Filter = Strings.CsvFilter,
            FileName = string.Format(Strings.ReportFileName, DateTime.Now),
        };
        if (dialog.ShowDialog() != true)
            return;

        // Разделитель — как в региональных настройках: его же ждёт Excel («;» в России, «,» в США).
        var sep = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        var sb = new StringBuilder().AppendJoin(sep, Strings.CsvHeader.Split('|')).Append("\r\n");
        var n = 0;
        foreach (var g in Groups)
        {
            n++;
            foreach (var f in g.Files)
                sb.AppendJoin(sep, n, f.RootLabel, f.Entry.Size, f.LastWrite.ToString("yyyy-MM-dd HH:mm:ss"),
                    $"\"{f.Path.Replace("\"", "\"\"")}\"").Append("\r\n");
        }
        // UTF-8 с BOM — чтобы Excel открыл кириллицу без вопросов.
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Status = string.Format(Strings.ReportSaved, dialog.FileName);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cache?.Dispose();
    }
}
