namespace StartupToggle.Core;

/// <summary>
/// 恢复引擎：把备份里每条记录的**最早**原始状态写回去。
/// 启动项写 StartupApproved，服务写 Start，两者共用同一个备份文件，
/// 所以主窗口和服务窗口点“恢复”时行为一致。
/// </summary>
public sealed class RestoreEngine
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    private readonly IRegistryGateway _registry;
    private readonly BackupStore _backup;

    public RestoreEngine(IRegistryGateway registry, BackupStore backup)
    {
        _registry = registry;
        _backup = backup;
    }

    public RestoreResult RestoreAll()
    {
        var records = _backup.OriginalPerEntry();
        var restored = 0;
        var errors = new List<string>();
        var keep = new List<BackupRecord>();

        foreach (var record in records)
        {
            try
            {
                if (string.Equals(record.Kind, "service", StringComparison.OrdinalIgnoreCase))
                {
                    if (record.OriginalStart is null)
                    {
                        errors.Add($"{record.Name}：备份里没有原始启动类型，已跳过。");
                        keep.Add(record);
                        continue;
                    }

                    _registry.SetInt32(Hive.LocalMachine, $@"{ServicesRoot}\{record.Name}", "Start", record.OriginalStart.Value);
                    restored++;
                    continue;
                }

                if (!Enum.TryParse<StartupLocation>(record.Location, out var location) ||
                    location == StartupLocation.OtherUserRun)
                {
                    errors.Add($"{record.Location} / {record.Name}：位置无法识别，已跳过。");
                    keep.Add(record);
                    continue;
                }

                var (hive, _, approvedPath) = RegistryPaths.For(location);
                if (record.OriginalApproved is null || record.OriginalApproved.Length == 0)
                {
                    _registry.DeleteValue(hive, approvedPath, record.Name);
                }
                else
                {
                    _registry.SetBinary(hive, approvedPath, record.Name, record.OriginalApproved);
                }

                restored++;
            }
            catch (Exception ex)
            {
                errors.Add($"{record.Name}：{ex.Message}");
                keep.Add(record);
            }
        }

        _backup.KeepOnly(keep);
        return new RestoreResult(restored, keep.Count, errors);
    }
}
