namespace StartupToggle.Core.Services;

/// <summary>
/// 服务开关的写入口。和启动项一样：先备份、再改 Start，改完才尝试停服务，
/// 任何一步失败都不会留下“说要禁用但其实还开着”的假状态。
/// </summary>
public sealed class ServiceManager
{
    public const string ServicesRoot = ServiceScanner.ServicesRoot;

    private readonly IRegistryGateway _registry;
    private readonly BackupStore _backup;
    private readonly IServiceControl _control;
    private readonly ServiceScanner _scanner;

    public ServiceManager(
        IRegistryGateway registry,
        BackupStore backup,
        IServiceControl? control = null,
        ServiceScanner? scanner = null)
    {
        _registry = registry;
        _backup = backup;
        _control = control ?? Win32ServiceControl.Instance;
        _scanner = scanner ?? new ServiceScanner(registry, _control);
    }

    public BackupStore Backup => _backup;

    public IReadOnlyList<ServiceInfo> Scan(bool queryRunState = true) => _scanner.Scan(queryRunState);

    /// <summary>禁用 = Start 写成 4 并停止正在运行的实例；启用 = Start 写成 2（不立即启动）。</summary>
    public OperationResult SetEnabled(ServiceInfo service, bool enabled, TimeSpan? stopTimeout = null)
    {
        if (service.IsLocked)
        {
            return OperationResult.Fail($"“{service.DisplayNameOrFallback}”在保护名单里：{service.LockReason}");
        }

        if (service.IsDriver)
        {
            return OperationResult.Fail("驱动类服务不允许改动。");
        }

        var path = $@"{ServicesRoot}\{service.Name}";

        try
        {
            _backup.Record(new BackupRecord
            {
                Kind = "service",
                Location = "Service",
                Name = service.Name,
                Command = service.DisplayNameOrFallback,
                Action = enabled ? "enable" : "disable",
                OriginalStart = (int)service.StartType,
            });

            _registry.SetInt32(Hive.LocalMachine, path, "Start", enabled ? 2 : 4);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"“{service.DisplayNameOrFallback}”写入失败：{ex.Message}");
        }

        if (enabled)
        {
            return OperationResult.Ok($"已把“{service.DisplayNameOrFallback}”设为自动启动，下次开机生效（现在没有替你启动它）。");
        }

        var stopped = _control.TryStop(service.Name, stopTimeout ?? TimeSpan.FromSeconds(8), out var error);
        return stopped
            ? OperationResult.Ok($"已禁用“{service.DisplayNameOrFallback}”，并已停止正在运行的实例。")
            : OperationResult.Ok($"已禁用“{service.DisplayNameOrFallback}”，但没能立即停止：{error}（下次开机会是停止状态）。");
    }
}
