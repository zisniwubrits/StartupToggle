using System.IO;
using StartupToggle.Core;
using StartupToggle.Core.Services;

namespace StartupToggle.SelfTest;

/// <summary>内存版服务控制器：记录被停止的服务名，可按需返回运行态。</summary>
internal sealed class FakeServiceControl : IServiceControl
{
    private readonly Dictionary<string, ServiceRunState> _states = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Stopped { get; } = [];

    public List<string> Started { get; } = [];

    public string? StopError { get; set; }

    public void SetState(string name, ServiceRunState state) => _states[name] = state;

    public ServiceRunState QueryState(string name) =>
        _states.TryGetValue(name, out var state) ? state : ServiceRunState.Stopped;

    public bool TryStop(string serviceName, TimeSpan timeout, out string? error)
    {
        error = StopError;
        if (StopError is not null)
        {
            return false;
        }

        Stopped.Add(serviceName);
        _states[serviceName] = ServiceRunState.Stopped;
        return true;
    }

    public bool TryStart(string serviceName, out string? error)
    {
        error = null;
        Started.Add(serviceName);
        _states[serviceName] = ServiceRunState.Running;
        return true;
    }
}

internal static class ServicesTest
{
    private const string Root = ServiceScanner.ServicesRoot;

    public static void Run()
    {
        var registry = new FakeRegistryGateway();
        var control = new FakeServiceControl();
        var backup = new BackupStore(Path.Combine(Program.TempRoot(), $"service-backup-{Guid.NewGuid():N}.json"));
        var manager = new ServiceManager(registry, backup, control);

        SeedService(registry, "LDPlayerSvr", "LeiDian", start: 2, type: 0x110);
        SeedService(registry, "aTrustService", "aTrust 服务", start: 2, type: 0x10);
        SeedService(registry, "aTrustNetflt_0", "aTrustNetflt_0", start: 2, type: 0x1);
        SeedService(registry, "RpcSs", "Remote Procedure Call", start: 2, type: 0x20);
        SeedService(registry, "SomeDriver", "某驱动", start: 1, type: 0x2);
        SeedService(registry, "DelayedThing", "延迟启动的东西", start: 2, type: 0x10, delayed: 1);
        SeedService(registry, "DependentSvc", "依赖者", start: 3, type: 0x10, dependsOn: ["LDPlayerSvr"]);
        SeedService(registry, "NoDisplay", start: 3, type: 0x10, displayName: null);

        // Services 键下的非服务子键要能跳过
        registry.SeedString(Hive.LocalMachine, $@"{Root}\SomeService\Parameters", "x", "y");

        control.SetState("LDPlayerSvr", ServiceRunState.Running);

        var services = manager.Scan();

        Program.Check($"扫描出 {services.Count} 个服务（跳过非服务子键）", services.Count == 8);

        var ld = services.Single(s => s.Name == "LDPlayerSvr");
        Program.Check("LDPlayerSvr 可操作", ld.CanToggle && !ld.IsDriver);
        Program.Check("LDPlayerSvr 显示为运行中", ld.RunState == ServiceRunState.Running);
        Program.Check("LDPlayerSvr 显示名解析为 LeiDian", ld.DisplayName == "LeiDian");
        Program.Check("LDPlayerSvr 反向依赖里含 DependentSvc", ld.Dependents.Contains("DependentSvc"));

        var atrustSvc = services.Single(s => s.Name == "aTrustService");
        Program.Check("aTrustService 被厂家前缀锁定", atrustSvc.IsLocked);
        Program.Check("aTrustService 锁定原因提到准入", atrustSvc.LockReason?.Contains("准入") == true);

        var atrustDriver = services.Single(s => s.Name == "aTrustNetflt_0");
        Program.Check("aTrust 驱动被识别为驱动", atrustDriver.IsDriver);
        Program.Check("驱动被锁定", atrustDriver.IsLocked);

        Program.Check("RpcSs 被关键服务名单锁定", services.Single(s => s.Name == "RpcSs").IsLocked);
        Program.Check("System 启动类型的驱动被锁定", services.Single(s => s.Name == "SomeDriver").IsLocked);

        var delayed = services.Single(s => s.Name == "DelayedThing");
        Program.Check("延迟启动显示为“自动（延迟）”", delayed.DisplayStartType == "自动（延迟）");

        Program.Check("没有显示名的服务退回服务名",
            services.Single(s => s.Name == "NoDisplay").DisplayNameOrFallback == "NoDisplay");

        // ---- 禁用 ----
        var disable = manager.SetEnabled(ld, enabled: false);
        Program.Check($"禁用 LDPlayerSvr 成功（{disable.Message}）", disable.Success);
        Program.Check("Start 被写成 4",
            registry.GetInt32(Hive.LocalMachine, $@"{Root}\LDPlayerSvr", "Start") == 4);
        Program.Check("确实调用了停止", control.Stopped.Contains("LDPlayerSvr"));
        Program.Check("备份记录了服务类型", backup.Load().Last().Kind == "service");
        Program.Check("备份记录了原始 Start=2", backup.Load().Last().OriginalStart == 2);
        Program.Check("其它服务不受影响",
            registry.GetInt32(Hive.LocalMachine, $@"{Root}\RpcSs", "Start") == 2);

        // ---- 启用 ----
        var rescan = manager.Scan().Single(s => s.Name == "LDPlayerSvr");
        Program.Check("重新扫描后显示为禁用", rescan.StartType == ServiceStartType.Disabled);
        var enable = manager.SetEnabled(rescan, enabled: true);
        Program.Check($"启用 LDPlayerSvr 成功（{enable.Message}）", enable.Success);
        Program.Check("Start 被写成 2",
            registry.GetInt32(Hive.LocalMachine, $@"{Root}\LDPlayerSvr", "Start") == 2);
        Program.Check("启用不会顺手启动服务", control.Started.Count == 0);

        // ---- 锁定项拒绝 ----
        Program.Check("锁定服务拒绝写入", !manager.SetEnabled(atrustSvc, enabled: false).Success);
        Program.Check("拒绝时 Start 未变",
            registry.GetInt32(Hive.LocalMachine, $@"{Root}\aTrustService", "Start") == 2);

        var driverInfo = services.Single(s => s.Name == "aTrustNetflt_0");
        Program.Check("驱动拒绝写入", !manager.SetEnabled(driverInfo, enabled: false).Success);

        // ---- 停止失败也要落盘禁用 ----
        control.StopError = "服务拒绝停止";
        var stopFail = manager.SetEnabled(services.Single(s => s.Name == "DependentSvc"), enabled: false);
        Program.Check("停止失败时仍返回成功（已写注册表）", stopFail.Success);
        Program.Check("停止失败时提示里说明没停掉", stopFail.Message.Contains("没能立即停止"));
        control.StopError = null;

        // ---- 一键恢复覆盖服务 ----
        var startupManager = new StartupManager(registry, Program.CreateTempEnvironment(), backup);
        var restore = startupManager.RestoreAll();
        Program.Check($"恢复无失败（恢复了 {restore.Restored} 条）", restore.Failed == 0);
        Program.Check("LDPlayerSvr 的 Start 回到 2",
            registry.GetInt32(Hive.LocalMachine, $@"{Root}\LDPlayerSvr", "Start") == 2);
        Program.Check("DependentSvc 的 Start 回到 3",
            registry.GetInt32(Hive.LocalMachine, $@"{Root}\DependentSvc", "Start") == 3);
        Program.Check("恢复后备份清空", backup.Count == 0);
    }

    private static void SeedService(
        FakeRegistryGateway registry,
        string name,
        string? displayName,
        int start,
        int type,
        int delayed = 0,
        string[]? dependsOn = null)
    {
        var path = $@"{Root}\{name}";
        registry.SeedInt32(Hive.LocalMachine, path, "Start", start);
        registry.SeedInt32(Hive.LocalMachine, path, "Type", type);
        if (displayName is not null)
        {
            registry.SeedString(Hive.LocalMachine, path, "DisplayName", displayName);
        }

        if (delayed != 0)
        {
            registry.SeedInt32(Hive.LocalMachine, path, "DelayedAutoStart", delayed);
        }

        if (dependsOn is { Length: > 0 })
        {
            registry.SeedMultiString(Hive.LocalMachine, path, "DependOnService", dependsOn);
        }
    }
}
