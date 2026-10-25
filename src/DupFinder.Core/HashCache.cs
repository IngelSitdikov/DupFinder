using Microsoft.Data.Sqlite;

namespace DupFinder.Core;

/// <summary>Кэш хешей между запусками. Запись годна, пока у файла те же путь, размер и дата изменения.</summary>
public interface IHashCache
{
    string? GetPartial(FileEntry file);
    string? GetFull(FileEntry file);
    void SetPartial(FileEntry file, string hash);
    void SetFull(FileEntry file, string hash);
    void Flush();
}

/// <summary>
/// Кэш в SQLite. Потокобезопасен: одно соединение под замком, записи копятся в памяти
/// и уходят в базу пачками в одной транзакции.
/// </summary>
public sealed class SqliteHashCache : IHashCache, IDisposable
{
    /// <summary>Записи о файлах, которые не встречались столько дней, удаляются при открытии.</summary>
    public const int KeepDays = 180;

    private const int FlushThreshold = 5000;

    private readonly object _lock = new();
    private readonly SqliteConnection _db;
    private readonly SqliteCommand _select;
    private readonly Dictionary<string, Row> _pending = new(StringComparer.Ordinal);
    private readonly long _today = DateTime.UtcNow.Ticks / TimeSpan.TicksPerDay;

    private readonly record struct Row(long Size, long Mtime, string? Partial, string? Full);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupFinder", "hashcache.db");

    public SqliteHashCache(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS files (
                path    TEXT PRIMARY KEY,
                size    INTEGER NOT NULL,
                mtime   INTEGER NOT NULL,
                partial TEXT,
                full    TEXT,
                seen    INTEGER NOT NULL
            ) WITHOUT ROWID;
            """);
        Execute($"DELETE FROM files WHERE seen < {_today - KeepDays}");

        _select = _db.CreateCommand();
        _select.CommandText = "SELECT size, mtime, partial, full FROM files WHERE path = $path";
        _select.Parameters.Add("$path", SqliteType.Text);
    }

    public long Count
    {
        get
        {
            lock (_lock)
            {
                FlushLocked();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM files";
                return (long)cmd.ExecuteScalar()!;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pending.Clear();
            Execute("DELETE FROM files; VACUUM;");
        }
    }

    public string? GetPartial(FileEntry file) => Lookup(file)?.Partial;

    public string? GetFull(FileEntry file) => Lookup(file)?.Full;

    public void SetPartial(FileEntry file, string hash) => Update(file, r => r with { Partial = hash });

    public void SetFull(FileEntry file, string hash) => Update(file, r => r with { Full = hash });

    private Row? Lookup(FileEntry file)
    {
        lock (_lock)
        {
            var row = LookupLocked(file);
            // Отмечаем, что файл ещё жив, чтобы запись не ушла при чистке старых.
            if (row is { } r && !_pending.ContainsKey(file.Path))
                Stage(file.Path, r);
            return row;
        }
    }

    private void Update(FileEntry file, Func<Row, Row> change)
    {
        lock (_lock)
        {
            var current = LookupLocked(file) ?? new Row(file.Size, file.LastWriteUtc.Ticks, null, null);
            Stage(file.Path, change(current));
        }
    }

    private Row? LookupLocked(FileEntry file)
    {
        if (!_pending.TryGetValue(file.Path, out var row))
        {
            _select.Parameters["$path"].Value = file.Path;
            using var reader = _select.ExecuteReader();
            if (!reader.Read())
                return null;
            row = new Row(reader.GetInt64(0), reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }
        // Файл с тем же путём, но другим размером или датой — уже другой файл, старые хеши не годятся.
        return row.Size == file.Size && row.Mtime == file.LastWriteUtc.Ticks ? row : null;
    }

    private void Stage(string path, Row row)
    {
        _pending[path] = row;
        if (_pending.Count >= FlushThreshold)
            FlushLocked();
    }

    public void Flush()
    {
        lock (_lock)
            FlushLocked();
    }

    private void FlushLocked()
    {
        if (_pending.Count == 0)
            return;
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO files (path, size, mtime, partial, full, seen)
            VALUES ($path, $size, $mtime, $partial, $full, $seen)
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size, mtime = excluded.mtime,
                partial = excluded.partial, full = excluded.full, seen = excluded.seen
            """;
        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
        var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
        var pMtime = cmd.Parameters.Add("$mtime", SqliteType.Integer);
        var pPartial = cmd.Parameters.Add("$partial", SqliteType.Text);
        var pFull = cmd.Parameters.Add("$full", SqliteType.Text);
        cmd.Parameters.AddWithValue("$seen", _today);

        foreach (var (path, row) in _pending)
        {
            pPath.Value = path;
            pSize.Value = row.Size;
            pMtime.Value = row.Mtime;
            pPartial.Value = (object?)row.Partial ?? DBNull.Value;
            pFull.Value = (object?)row.Full ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        _pending.Clear();
    }

    private void Execute(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            FlushLocked();
            _select.Dispose();
            _db.Dispose();
        }
    }
}
