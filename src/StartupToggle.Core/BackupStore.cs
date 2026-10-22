using System.Text.Json;

namespace StartupToggle.Core;

/// <summary>一次写入之前的原始状态，回滚的唯一依据。</summary>
public sealed class BackupRecord
{
    public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>StartupLocation 的枚举名。</summary>
    public string Location { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>写入前的 StartupApproved 原始字节；null 表示当时没有这个值。</summary>
    public byte[]? OriginalApproved { get; set; }

    /// <summary>当时的启动命令行，清理失效项时用于佐证。</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>disable / enable / remove。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>startup（启动项）或 service（Windows 服务）。旧备份文件没有这个字段，按启动项处理。</summary>
    public string Kind { get; set; } = "startup";

    /// <summary>服务写入前的 Start 原始值（仅 Kind=service 时有）。</summary>
    public int? OriginalStart { get; set; }

    public string? Owner { get; set; }
}

/// <summary>JSON 备份文件，记录每一次写入前的状态，支持“一键恢复”。</summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly object _gate = new();

    public BackupStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static BackupStore Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartupToggle",
        "backup.json"));

    public IReadOnlyList<BackupRecord> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                return Array.Empty<BackupRecord>();
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<BackupRecord>>(json, Options) ?? [];
            }
            catch (Exception)
            {
                return Array.Empty<BackupRecord>();
            }
        }
    }

    public void Record(BackupRecord record)
    {
        lock (_gate)
        {
            var all = Load().ToList();
            all.Add(record);
            Persist(all);
        }
    }

    /// <summary>
    /// 每个 (位置, 名称) 只取**最早**一条记录。
    /// 因为“恢复我改过的项”要回到我动手之前的状态：禁用→再启用之后，
    /// 最后一条记录里存的是“禁用”，按最后一条恢复会把用户退回到中间态。
    /// </summary>
    public IReadOnlyList<BackupRecord> OriginalPerEntry()
    {
        return Load()
            .GroupBy(record => (record.Location, record.Name), StringTupleComparer.Instance)
            .Select(group => group.First())
            .ToList();
    }

    public int Count => Load().Count;

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }

    public void KeepOnly(IEnumerable<BackupRecord> records)
    {
        lock (_gate)
        {
            var remaining = records.ToList();
            if (remaining.Count == 0)
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }

                return;
            }

            Persist(remaining);
        }
    }

    private void Persist(List<BackupRecord> records)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(records, Options));
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) => HashCode.Combine(
            obj.Item1.ToLowerInvariant(),
            obj.Item2.ToLowerInvariant());
    }
}
