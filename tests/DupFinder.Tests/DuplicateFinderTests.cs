using System.Runtime.InteropServices;
using DupFinder.Core;

namespace DupFinder.Tests;

public sealed class DuplicateFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dupfinder-tests", Guid.NewGuid().ToString("N"));
    private string A => Path.Combine(_root, "A");
    private string B => Path.Combine(_root, "B");

    public DuplicateFinderTests()
    {
        Directory.CreateDirectory(A);
        Directory.CreateDirectory(B);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Write(string dir, string name, byte[] content)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Bytes(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Fact]
    public void SingleRoot_FindsDuplicatesInside()
    {
        var content = Bytes(1000, 1);
        Write(A, "x.bin", content);
        Write(A, "sub/y.bin", content);
        Write(A, "other.bin", Bytes(1000, 2));

        var result = DuplicateFinder.Find(new ScanOptions { Roots = [A] });

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(1000, group.WastedBytes);
    }

    [Fact]
    public void TwoRoots_CrossOnly_IgnoresDuplicatesWithinOneSide()
    {
        var shared = Bytes(5000, 1);
        Write(A, "s.bin", shared);
        Write(B, "s-copy.bin", shared);
        var onlyA = Bytes(5000, 2);
        Write(A, "a1.bin", onlyA);
        Write(A, "a2.bin", onlyA);

        var cross = DuplicateFinder.Find(new ScanOptions { Roots = [A, B], CrossRootsOnly = true });
        var all = DuplicateFinder.Find(new ScanOptions { Roots = [A, B], CrossRootsOnly = false });

        var g = Assert.Single(cross.Groups);
        Assert.Equal([0, 1], g.Files.Select(f => f.RootIndex));
        Assert.Equal(2, all.Groups.Count);
    }

    [Fact]
    public void LargeFiles_SameHeadAndTail_DifferentMiddle_AreNotDuplicates()
    {
        var size = 1024 * 1024;
        var first = Bytes(size, 1);
        var second = (byte[])first.Clone();
        second[size / 2] ^= 0xFF;
        Write(A, "1.bin", first);
        Write(A, "2.bin", second);
        Write(A, "3.bin", first);

        var result = DuplicateFinder.Find(new ScanOptions { Roots = [A] });

        var group = Assert.Single(result.Groups);
        Assert.Equal(["1.bin", "3.bin"], group.Files.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void EmptyAndSmallFiles_AreSkipped()
    {
        Write(A, "e1", []);
        Write(A, "e2", []);
        Write(A, "s1", [1, 2, 3]);
        Write(A, "s2", [1, 2, 3]);

        // Пустые не берутся даже при MinSize = 0: одинаковы все, удалять их смысла нет.
        var group = Assert.Single(DuplicateFinder.Find(new ScanOptions { Roots = [A], MinSize = 0 }).Groups);
        Assert.Equal(3, group.Size);
        Assert.Empty(DuplicateFinder.Find(new ScanOptions { Roots = [A], MinSize = 4 }).Groups);
    }

    [Fact]
    public void HardLinks_AreNotReportedAsDuplicates()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var original = Write(A, "orig.bin", Bytes(2000, 1));
        Assert.True(CreateHardLink(Path.Combine(A, "link.bin"), original, IntPtr.Zero));

        Assert.Empty(DuplicateFinder.Find(new ScanOptions { Roots = [A] }).Groups);

        // Настоящая копия рядом — группа есть, но жёсткая ссылка в ней не считается второй копией.
        File.Copy(original, Path.Combine(A, "copy.bin"));
        var group = Assert.Single(DuplicateFinder.Find(new ScanOptions { Roots = [A] }).Groups);
        Assert.Equal(2, group.Files.Count);
    }

    [Fact]
    public void NestedRoots_DoNotCountSameFileTwice()
    {
        Write(A, "sub/only.bin", Bytes(3000, 1));

        var result = DuplicateFinder.Find(new ScanOptions
        {
            Roots = [A, Path.Combine(A, "sub")],
            CrossRootsOnly = false,
        });

        Assert.Empty(result.Groups);
    }

    [Fact]
    public void ExcludedDirectories_AreNotEntered()
    {
        var content = Bytes(1000, 1);
        Write(A, "x.bin", content);
        Write(A, "$RECYCLE.BIN/x.bin", content);

        Assert.Empty(DuplicateFinder.Find(new ScanOptions { Roots = [A] }).Groups);
    }

    [Fact]
    public void Cancellation_ThrowsOperationCanceled()
    {
        Write(A, "x.bin", Bytes(1000, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            DuplicateFinder.Find(new ScanOptions { Roots = [A] }, ct: cts.Token));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr securityAttributes);
}
