using System.ComponentModel;
using System.Runtime.InteropServices;
using DupFinder.Core.Resources;

namespace DupFinder.Core;

/// <summary>
/// Проверки и действия перед тем, как избавиться от копии. Хеш (особенно из кэша) — это повод,
/// а не доказательство: перед необратимым действием содержимое сверяется побайтно.
/// </summary>
public static partial class DuplicateResolver
{
    private const int CompareBuffer = 4 * 1024 * 1024;

    /// <summary>Файл на месте и не менялся с момента сканирования? Иначе — текст причины.</summary>
    public static string? CheckUnchanged(FileEntry file)
    {
        var info = new FileInfo(file.Path);
        if (!info.Exists)
            return Strings.FileGone;
        if (info.Length != file.Size || info.LastWriteTimeUtc != file.LastWriteUtc)
            return Strings.FileChangedSinceScan;
        return null;
    }

    public static bool AreIdentical(string a, string b, CancellationToken ct = default)
    {
        using var sa = OpenRead(a);
        using var sb = OpenRead(b);
        if (sa.Length != sb.Length)
            return false;

        var ba = new byte[CompareBuffer];
        var bb = new byte[CompareBuffer];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ra = sa.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            var rb = sb.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            if (ra != rb || !ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb)))
                return false;
            if (ra == 0)
                return true;
        }
    }

    /// <summary>
    /// Проверяет, что от <paramref name="target"/> можно избавиться, оставив <paramref name="keep"/>:
    /// оба на месте, не менялись и совпадают побайтно. Возвращает текст причины отказа или null.
    /// </summary>
    public static string? VerifyDuplicate(FileEntry target, FileEntry keep, CancellationToken ct = default)
    {
        if (string.Equals(target.Path, keep.Path, StringComparison.OrdinalIgnoreCase))
            return Strings.SameFile;
        var reason = CheckUnchanged(target) ?? CheckUnchanged(keep)?.Insert(0, Strings.KeptCopyPrefix);
        if (reason is not null)
            return reason;
        return AreIdentical(target.Path, keep.Path, ct) ? null : Strings.ContentMismatch;
    }

    public static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Заменяет <paramref name="target"/> жёсткой ссылкой на <paramref name="keep"/> (только NTFS, один том).
    /// Сначала ссылка создаётся под временным именем, потом атомарно встаёт на место файла —
    /// при сбое на любом шаге исходный файл остаётся нетронутым. Возвращает текст ошибки или null.
    /// </summary>
    public static string? ReplaceWithHardLink(FileEntry target, FileEntry keep, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return Strings.HardLinksWindowsOnly;
        if (!SameVolume(target.Path, keep.Path))
            return Strings.HardLinkOtherVolume;

        try
        {
            var reason = VerifyDuplicate(target, keep, ct);
            if (reason is not null)
                return reason;
            if (FileIdentity.TryGet(target.Path) is { } t && FileIdentity.TryGet(keep.Path) == t)
                return null; // уже ссылка на тот же файл

            var temp = $"{target.Path}.{Guid.NewGuid():N}.dflink";
            if (!CreateHardLink(LongPath(temp), LongPath(keep.Path), IntPtr.Zero))
                return new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            try
            {
                File.Move(temp, target.Path, overwrite: true);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, CompareBuffer, FileOptions.SequentialScan);

    /// <summary>Сырому WinAPI длинные пути нужно отдавать с префиксом \\?\ (.NET-методы делают это сами).</summary>
    private static string LongPath(string path) =>
        path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
        : @"\\?\" + path;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newFile, string existingFile, IntPtr securityAttributes);
}
