namespace StartupToggle.Core.Services;

/// <summary>服务启动类型，取值与注册表 Start 一致。</summary>
public enum ServiceStartType
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
}

/// <summary>服务运行状态，来自 SCM 查询；查不到时为 Unknown。</summary>
public enum ServiceRunState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7,
}

public static class ServiceDisplay
{
    public static string StartType(ServiceStartType type, bool delayed) => type switch
    {
        ServiceStartType.Boot => "引导",
        ServiceStartType.System => "系统",
        ServiceStartType.Automatic => delayed ? "自动（延迟）" : "自动",
        ServiceStartType.Manual => "手动",
        ServiceStartType.Disabled => "禁用",
        _ => type.ToString(),
    };

    public static string RunState(ServiceRunState state) => state switch
    {
        ServiceRunState.Running => "正在运行",
        ServiceRunState.Stopped => "已停止",
        ServiceRunState.StartPending => "正在启动",
        ServiceRunState.StopPending => "正在停止",
        ServiceRunState.Paused => "已暂停",
        ServiceRunState.PausePending => "正在暂停",
        ServiceRunState.ContinuePending => "正在恢复",
        _ => "未知",
    };
}

/// <summary>一个 Windows 服务（含驱动类）。</summary>
public sealed class ServiceInfo
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required ServiceStartType StartType { get; init; }

    public bool Delayed { get; init; }

    public required int Type { get; init; }

    public ServiceRunState RunState { get; init; }

    /// <summary>本服务依赖的其它服务。</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>依赖本服务的其它服务——禁用前必须让人看见这个数字。</summary>
    public IReadOnlyList<string> Dependents { get; init; } = [];

    public bool IsDriver { get; init; }

    public bool IsLocked { get; init; }

    public string? LockReason { get; init; }

    public bool CanToggle => !IsLocked;

    public bool IsAuto => StartType == ServiceStartType.Automatic;

    public string DisplayStartType => ServiceDisplay.StartType(StartType, Delayed);

    public string DisplayRunState => ServiceDisplay.RunState(RunState);

    public string DisplayKind => IsDriver ? "驱动" : "服务";

    public string DisplayNameOrFallback => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    public ServiceInfo WithStartType(ServiceStartType startType, bool delayed) => new()
    {
        Name = Name,
        DisplayName = DisplayName,
        Description = Description,
        StartType = startType,
        Delayed = delayed,
        Type = Type,
        RunState = RunState,
        DependsOn = DependsOn,
        Dependents = Dependents,
        IsDriver = IsDriver,
        IsLocked = IsLocked,
        LockReason = LockReason,
    };
}
