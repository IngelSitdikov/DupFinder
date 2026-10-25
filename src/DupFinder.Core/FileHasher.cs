using System.IO.Hashing;
using DupFinder.Core.Resources;

namespace DupFinder.Core;

public static class FileHasher
{
    /// <summary>Сколько читается с начала и с конца файла для быстрого отсева.</summary>
    public const int PartialChunk = 64 * 1024;

    private const int BufferSize = 1024 * 1024;

    /// <summary>Файлы не больше этого размера частичный хеш читает целиком, полный хеш для них не нужен.</summary>
    public static bool PartialCoversWholeFile(long size) => size <= 2L * PartialChunk;

    public static string ComputePartial(string path, long size, CancellationToken ct)
    {
        using var stream = Open(path, PartialChunk);
        var hasher = new XxHash128();
        var buffer = new byte[PartialChunk];

        ReadInto(stream, hasher, buffer, (int)Math.Min(size, PartialChunk), ct);
        if (size > PartialChunk)
        {
            var tail = (int)Math.Min(size - PartialChunk, PartialChunk);
            stream.Seek(size - tail, SeekOrigin.Begin);
            ReadInto(stream, hasher, buffer, tail, ct);
        }
        return Convert.ToHexString(hasher.GetCurrentHash());
    }

    public static string ComputeFull(string path, Action<long>? onBytes, CancellationToken ct)
    {
        using var stream = Open(path, BufferSize);
        var hasher = new XxHash128();
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hasher.Append(buffer.AsSpan(0, read));
            onBytes?.Invoke(read);
        }
        return Convert.ToHexString(hasher.GetCurrentHash());
    }

    private static FileStream Open(string path, int bufferSize) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize, FileOptions.SequentialScan);

    private static void ReadInto(FileStream stream, XxHash128 hasher, byte[] buffer, int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read == 0)
                throw new IOException(Strings.FileChangedWhileReading);
            total += read;
        }
        hasher.Append(buffer.AsSpan(0, count));
    }
}
