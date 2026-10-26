using System.Diagnostics;
using System.IO;
using DupFinder.Core;
using Microsoft.VisualBasic.FileIO;

namespace DupFinder.Presentation.Services;

public static class FileActions
{
    /// <summary>
    /// Отправляет файл в корзину. Перед этим проверяет, что он и оставляемая копия не изменились
    /// с момента сканирования, а при <paramref name="verify"/> — ещё и что они совпадают побайтно.
    /// Возвращает текст ошибки или null при успехе.
    /// </summary>
    public static string? MoveToTrash(FileEntry target, FileEntry keep, bool verify, CancellationToken ct)
    {
        try
        {
            var reason = verify
                ? DuplicateResolver.VerifyDuplicate(target, keep, ct)
                : DuplicateResolver.CheckBothUnchanged(target, keep);
            if (reason is not null)
                return reason;

            if (OperatingSystem.IsWindows())
            {
                FileSystem.DeleteFile(target.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return null;
            }
            if (OperatingSystem.IsMacOS())
                return MacTrash.Move(target.Path);
            return Strings.TrashUnsupported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// На съёмных носителях в Windows корзины нет — файл будет удалён насовсем.
    /// В macOS у внешних томов своя корзина (.Trashes), так что там это не страшно.
    /// </summary>
    public static bool IsOnRemovableDrive(string path)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            var root = System.IO.Path.GetPathRoot(path);
            return root is not null && new DriveInfo(root).DriveType == DriveType.Removable;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Открыть папку с файлом и выделить его: Проводник в Windows, Finder в macOS.</summary>
    public static void ShowInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", ["-R", path]);
        else
            Process.Start("xdg-open", [System.IO.Path.GetDirectoryName(path) ?? path]);
    }
}
