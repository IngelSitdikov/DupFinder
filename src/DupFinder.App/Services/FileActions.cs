using System.Diagnostics;
using System.IO;
using DupFinder.Core;
using Microsoft.VisualBasic.FileIO;

namespace DupFinder.App.Services;

public static class FileActions
{
    /// <summary>
    /// Отправляет файл в корзину. Перед этим проверяет, что он не изменился с момента сканирования,
    /// а при <paramref name="verify"/> — ещё и что побайтно совпадает с оставляемой копией.
    /// Возвращает текст ошибки или null при успехе.
    /// </summary>
    public static string? MoveToRecycleBin(FileEntry target, FileEntry keep, bool verify, CancellationToken ct)
    {
        try
        {
            var reason = verify
                ? DuplicateResolver.VerifyDuplicate(target, keep, ct)
                : DuplicateResolver.CheckUnchanged(target);
            if (reason is not null)
                return reason;

            FileSystem.DeleteFile(target.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return ex.Message;
        }
    }

    /// <summary>На съёмных носителях корзины нет — файл будет удалён насовсем.</summary>
    public static bool IsOnRemovableDrive(string path)
    {
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

    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
}
