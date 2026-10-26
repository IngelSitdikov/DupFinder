using DupFinder.Core;

namespace DupFinder.Tests;

public sealed class CacheAndResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dupfinder-tests", Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_root, "cache.db");
    private string Data => Path.Combine(_root, "data");

    public CacheAndResolverTests() => Directory.CreateDirectory(Data);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(Data, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Bytes(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static FileEntry Entry(string path)
    {
        var info = new FileInfo(path);
        return new FileEntry(info.FullName, info.Length, info.LastWriteTimeUtc, 0);
    }

    // --- Кэш ---

    [Fact]
    public void Cache_SurvivesReopen()
    {
        var f = Entry(Write("a.bin", Bytes(100, 1)));
        using (var cache = new SqliteHashCache(DbPath))
        {
            cache.SetPartial(f, "P");
            cache.SetFull(f, "F");
        }

        using var reopened = new SqliteHashCache(DbPath);
        Assert.Equal("P", reopened.GetPartial(f));
        Assert.Equal("F", reopened.GetFull(f));
        Assert.Equal(1, reopened.Count);
    }

    [Fact]
    public void Cache_MissesWhenSizeOrDateChanged()
    {
        var f = Entry(Write("a.bin", Bytes(100, 1)));
        using var cache = new SqliteHashCache(DbPath);
        cache.SetFull(f, "F");

        Assert.Null(cache.GetFull(f with { Size = 101 }));
        Assert.Null(cache.GetFull(f with { LastWriteUtc = f.LastWriteUtc.AddSeconds(1) }));
        Assert.Equal("F", cache.GetFull(f));
    }

    [Fact]
    public void Scan_WithCache_GivesSameResultAndFillsCache()
    {
        var big = Bytes(300_000, 1);
        Write("1.bin", big);
        Write("2.bin", big);
        Write("3.bin", Bytes(300_000, 2));

        using var cache = new SqliteHashCache(DbPath);
        var first = DuplicateFinder.Find(new ScanOptions { Roots = [Data], Cache = cache });
        Assert.Equal(3, cache.Count);
        var second = DuplicateFinder.Find(new ScanOptions { Roots = [Data], Cache = cache });

        Assert.Equal(first.Groups.Single().Hash, second.Groups.Single().Hash);
        Assert.Equal(2, second.Groups.Single().Files.Count);
    }

    [Fact]
    public void Cache_Clear_RemovesEverything()
    {
        using var cache = new SqliteHashCache(DbPath);
        cache.SetFull(Entry(Write("a.bin", Bytes(10, 1))), "F");
        cache.Flush();
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    // --- Сверка и жёсткие ссылки ---

    [Fact]
    public void Verify_DetectsDifferentContentWithSameSize()
    {
        var a = Write("a.bin", Bytes(10_000, 1));
        var b = Write("b.bin", Bytes(10_000, 2));
        Assert.NotNull(DuplicateResolver.VerifyDuplicate(Entry(a), Entry(b)));

        var c = Write("c.bin", File.ReadAllBytes(a));
        Assert.Null(DuplicateResolver.VerifyDuplicate(Entry(a), Entry(c)));
    }

    [Fact]
    public void Verify_RefusesWhenFileChangedAfterScan()
    {
        var a = Write("a.bin", Bytes(1000, 1));
        var b = Write("b.bin", File.ReadAllBytes(a));
        var scanned = Entry(a);
        File.SetLastWriteTimeUtc(a, scanned.LastWriteUtc.AddMinutes(-5));

        Assert.NotNull(DuplicateResolver.VerifyDuplicate(scanned, Entry(b)));
    }

    [Fact]
    public void CheckBoth_RefusesWhenKeptCopyIsGone()
    {
        // Устаревший список: оставляемую копию уже удалили где-то ещё — удалять «дубль» нельзя.
        var a = Write("a.bin", Bytes(1000, 1));
        var b = Write("b.bin", File.ReadAllBytes(a));
        var keep = Entry(b);
        Assert.Null(DuplicateResolver.CheckBothUnchanged(Entry(a), keep));

        File.Delete(b);
        Assert.NotNull(DuplicateResolver.CheckBothUnchanged(Entry(a), keep));
    }

    [Fact]
    public void ReplaceWithHardLink_FreesSpaceAndKeepsContent()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;
        var content = Bytes(50_000, 1);
        var keep = Entry(Write("keep.bin", content));
        var target = Entry(Write("dup.bin", content));

        Assert.Null(DuplicateResolver.ReplaceWithHardLink(target, keep));

        Assert.Equal(content, File.ReadAllBytes(target.Path));
        Assert.Equal(FileIdentity.TryGet(keep.Path), FileIdentity.TryGet(target.Path));
        Assert.Empty(Directory.GetFiles(Data, "*.dflink"));
        // После замены сканер их больше дублями не считает.
        Assert.Empty(DuplicateFinder.Find(new ScanOptions { Roots = [Data] }).Groups);
    }

    [Fact]
    public void ReplaceWithHardLink_LeavesTargetIntactWhenContentDiffers()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;
        var keep = Entry(Write("keep.bin", Bytes(5000, 1)));
        var targetContent = Bytes(5000, 2);
        var target = Entry(Write("dup.bin", targetContent));

        Assert.NotNull(DuplicateResolver.ReplaceWithHardLink(target, keep));
        Assert.Equal(targetContent, File.ReadAllBytes(target.Path));
        Assert.NotEqual(FileIdentity.TryGet(keep.Path), FileIdentity.TryGet(target.Path));
    }
}
