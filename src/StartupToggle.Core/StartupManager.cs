namespace StartupToggle.Core;

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);

    public static OperationResult Fail(string message) => new(false, message);
}

public sealed record BulkResult(IReadOnlyList<(StartupItem Item, OperationResult Result)> Results)
{
    public int Succeeded => Results.Count(r => r.Result.Success);

    public int Failed => Results.Count - Succeeded;
}

public sealed record RestoreResult(int Restored, int Failed, IReadOnlyList<string> Errors);

/// <summary>
/// 所有写入的唯一入口：先备份、再写 StartupApproved，Run 值与 .lnk 永不改动。
/// </summary>
public sealed class StartupManager
{
    private readonly IRegistryGateway _registry;
    private readonly StartupEnvironment _environment;
    private readonly BackupStore _backup;
    private readonly ProtectionPolicy _policy;

    public StartupManager(
        IRegistryGateway registry,
        StartupEnvironment environment,
        BackupStore backup,
        ProtectionPolicy? policy = null)
    {
        _registry = registry;
        _environment = environment;
        _backup = backup;
        _policy = policy ?? new ProtectionPolicy();
    }

    public static StartupManager CreateDefault() => new(
        WindowsRegistryGateway.Instance,
        StartupEnvironment.Default,
        BackupStore.Default);

    public BackupStore Backup => _backup;

    public IReadOnlyList<StartupItem> Scan(bool includeOtherUsers = true) =>
        new StartupScanner(_registry, _environment, _policy).Scan(includeOtherUsers);

    public OperationResult SetEnabled(StartupItem item, bool enabled)
    {
        if (item.IsReadOnly)
        {
            return OperationResult.Fail($"“{item.Name}”属于其它用户，只能查看。");
        }

        if (item.IsLocked)
        {
            return OperationResult.Fail($"“{item.Name}”在保护名单里：{item.LockReason}");
        }

        if (item.Location == StartupLocation.OtherUserRun)
        {
            return OperationResult.Fail("不支持修改其它用户的启动项。");
        }

        try
        {
            var (hive, _, approvedPath) = RegistryPaths.For(item.Location);
            var newValue = StartupState.Compose(item.ApprovedData, enabled);

            _backup.Record(new BackupRecord
            {
                Location = item.Location.ToString(),
                Name = item.Name,
                OriginalApproved = item.ApprovedData,
                Command = item.Command,
                Action = enabled ? "enable" : "disable",
                Owner = item.Owner,
            });

            _registry.SetBinary(hive, approvedPath, item.Name, newValue);

            return OperationResult.Ok(enabled
                ? $"已启用“{item.Name}”。"
                : $"已禁用“{item.Name}”（Run 值与快捷方式均未改动，可随时恢复）。");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"“{item.Name}”写入失败：{ex.Message}");
        }
    }

    public BulkResult SetEnabledMany(IReadOnlyList<StartupItem> items, bool enabled)
    {
        var results = items.Select(item => (item, SetEnabled(item, enabled))).ToList();
        return new BulkResult(results);
    }

    /// <summary>把本软件改过的所有项（含服务）恢复到原始状态。</summary>
    public RestoreResult RestoreAll() => new RestoreEngine(_registry, _backup).RestoreAll();

    /// <summary>删除启动项本身（清理失效项用），删除前记录命令行。</summary>
    public OperationResult Remove(StartupItem item)
    {
        if (item.IsReadOnly || item.IsLocked)
        {
            return OperationResult.Fail($"“{item.Name}”不能被删除。");
        }

        try
        {
            _backup.Record(new BackupRecord
            {
                Location = item.Location.ToString(),
                Name = item.Name,
                OriginalApproved = item.ApprovedData,
                Command = item.Command,
                Action = "remove",
                Owner = item.Owner,
            });

            var (hive, runPath, approvedPath) = RegistryPaths.For(item.Location);

            if (item.IsFolderEntry)
            {
                File.Delete(item.Command);
            }
            else
            {
                _registry.DeleteValue(hive, runPath, item.Name);
            }

            _registry.DeleteValue(hive, approvedPath, item.Name);
            return OperationResult.Ok($"已清理“{item.Name}”。");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"“{item.Name}”清理失败：{ex.Message}");
        }
    }

    public OperationResult Add(string name, string command, StartupLocation target, bool overwrite = false)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedCommand = command?.Trim() ?? string.Empty;

        if (trimmedName.Length == 0)
        {
            return OperationResult.Fail("名称不能为空。");
        }

        if (trimmedName.Contains('\\') && !trimmedName.Contains(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // 允许带反斜杠的名称（Windows 是允许的），这里只做提示性放行，不阻断。
        }

        if (trimmedCommand.Length == 0)
        {
            return OperationResult.Fail("启动命令不能为空。");
        }

        if (target is not (StartupLocation.UserRun or StartupLocation.MachineRun))
        {
            return OperationResult.Fail("新增只支持写入当前用户或所有用户的 Run 键。");
        }

        try
        {
            var (hive, runPath, approvedPath) = RegistryPaths.For(target);
            var existing = _registry.GetValueNames(hive, runPath)
                .FirstOrDefault(n => string.Equals(n, trimmedName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null && !overwrite)
            {
                return OperationResult.Fail($"“{existing}”已存在，换个名字或勾选覆盖。");
            }

            _backup.Record(new BackupRecord
            {
                Location = target.ToString(),
                Name = existing ?? trimmedName,
                OriginalApproved = _registry.GetBinary(hive, approvedPath, existing ?? trimmedName),
                Command = _registry.GetString(hive, runPath, existing ?? trimmedName) ?? string.Empty,
                Action = "add",
            });

            _registry.SetString(hive, runPath, trimmedName, trimmedCommand);

            // 名字里可能残留旧的禁用状态，清掉让它处于启用。
            _registry.DeleteValue(hive, approvedPath, trimmedName);

            return OperationResult.Ok($"已添加“{trimmedName}”，下次登录时随系统启动。");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"添加失败：{ex.Message}");
        }
    }
}
