using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DupFinder.Core;

/// <summary>
/// Идентификатор файла на томе: в Windows — серийный номер тома + индекс файла NTFS,
/// на macOS — номер устройства + inode. Два пути с одинаковым идентификатором — жёсткие ссылки
/// на один файл, а не дубли: удаление «копии» ничего не освободит.
/// </summary>
public static partial class FileIdentity
{
    public static (uint Volume, ulong Index)? TryGet(string path)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return null;
        try
        {
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return OperatingSystem.IsWindows() ? FromWindows(handle) : FromMac(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (uint, ulong)? FromWindows(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            return null;
        return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    /// <summary>
    /// struct stat в macOS (64-битные inode) начинается так: dev_t st_dev (4 байта), mode_t st_mode (2),
    /// nlink_t st_nlink (2), ino_t st_ino (8). Всю структуру (144 байта) не описываем — берём буфер
    /// с запасом и читаем первые 16 байт.
    /// </summary>
    private static unsafe (uint, ulong)? FromMac(SafeFileHandle handle)
    {
        var buffer = stackalloc byte[256];
        var fd = (int)handle.DangerousGetHandle();
        // На Intel обычный fstat — старый, с 32-битным inode; 64-битный вариант называется fstat$INODE64.
        var rc = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? FStatInode64(fd, buffer)
            : FStat(fd, buffer);
        if (rc != 0)
            return null;
        return (*(uint*)buffer, *(ulong*)(buffer + 8));
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

    [LibraryImport("libc", EntryPoint = "fstat")]
    private static unsafe partial int FStat(int fd, byte* stat);

    [LibraryImport("libc", EntryPoint = "fstat$INODE64")]
    private static unsafe partial int FStatInode64(int fd, byte* stat);
}
