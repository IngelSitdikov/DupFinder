using System.Diagnostics;

namespace DupFinder.Core;

/// <summary>
/// Копит счётчики из нескольких потоков и отдаёт их наружу не чаще раза в 100 мс,
/// чтобы миллионы файлов не завалили UI-поток сообщениями.
/// </summary>
internal sealed class ProgressReporter(IProgress<ScanProgress>? target)
{
    private static readonly long Interval = Stopwatch.Frequency / 10;

    private ScanPhase _phase;
    private long _found, _processed, _total, _bytes, _bytesTotal, _lastTick;
    private string? _current;

    public void Begin(ScanPhase phase, long filesTotal, long bytesTotal)
    {
        _phase = phase;
        Interlocked.Exchange(ref _processed, 0);
        Interlocked.Exchange(ref _bytes, 0);
        _total = filesTotal;
        _bytesTotal = bytesTotal;
        Report(force: true);
    }

    public void Found(string path)
    {
        Interlocked.Increment(ref _found);
        _current = path;
        Report();
    }

    public void Current(string path) => _current = path;

    public void AddBytes(long bytes)
    {
        Interlocked.Add(ref _bytes, bytes);
        Report();
    }

    public void FileDone(long bytes)
    {
        Interlocked.Increment(ref _processed);
        if (bytes != 0)
            Interlocked.Add(ref _bytes, bytes);
        Report();
    }

    private void Report(bool force = false)
    {
        if (target is null)
            return;
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastTick);
        if (!force && (now - last < Interval || Interlocked.CompareExchange(ref _lastTick, now, last) != last))
            return;
        if (force)
            Interlocked.Exchange(ref _lastTick, now);

        target.Report(new ScanProgress(_phase, Interlocked.Read(ref _found), Interlocked.Read(ref _processed), _total,
            Interlocked.Read(ref _bytes), _bytesTotal, _phase == ScanPhase.Done ? null : _current));
    }
}
