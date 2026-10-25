using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DupFinder.Core;

/// <summary>
/// Идентификатор файла на томе (серийный номер тома + индекс файла NTFS).
/// Два пути с одинаковым идентификатором — жёсткие ссылки на один файл, а не дубли:
/// удаление «копии» ничего не освободит.
/// </summary>
public static partial class FileIdentity
{
    public static (uint Volume, ulong Index)? TryGet(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var info))
                return null;
            return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        // FILETIME — два DWORD с выравниванием 4, поэтому не long.
        public uint CreationTimeLow, CreationTimeHigh;
        public uint LastAccessTimeLow, LastAccessTimeHigh;
        public uint LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);
}
