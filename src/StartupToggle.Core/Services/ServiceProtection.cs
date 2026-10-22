namespace StartupToggle.Core.Services;

/// <summary>
/// 服务侧的保护规则。两把尺子：
/// 1) 驱动类（内核驱动 / 文件系统驱动）一律只读——禁错一个驱动可能就是蓝屏或进不了系统；
/// 2) 用户态服务里，禁了会直接影响开机、登录、网络、安全的那几个锁死。
/// </summary>
public static class ServiceProtection
{
    private const int KernelDriver = 0x0001;
    private const int FileSystemDriver = 0x0002;
    private const int Adapter = 0x0004;
    private const int RecognizerDriver = 0x0008;

    /// <summary>驱动类型掩码：命中即只读。</summary>
    public const int DriverMask = KernelDriver | FileSystemDriver | Adapter | RecognizerDriver;

    private static readonly Dictionary<string, string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RpcSs"] = "RPC 核心，禁用后系统无法正常启动",
        ["DcomLaunch"] = "DCOM 服务控制，登录会失败",
        ["RpcEptMapper"] = "RPC 端点映射，依赖 RpcSs 的组件会全部失效",
        ["Power"] = "电源管理，禁用后可能无法关机或待机",
        ["ProfSvc"] = "用户配置文件，禁用后登录会出问题",
        ["SamSs"] = "安全账户管理器，登录凭据依赖它",
        ["LSM"] = "本地会话管理，登录依赖它",
        ["UserManager"] = "用户管理器，多用户场景必需",
        ["EventLog"] = "事件日志，大量组件依赖它写日志",
        ["Winmgmt"] = "WMI，任务管理器与系统信息依赖它",
        ["CryptSvc"] = "加密服务，证书与签名校验依赖它",
        ["Schedule"] = "任务计划程序，很多系统维护任务靠它",
        ["gpsvc"] = "组策略客户端，登录时会应用策略",
        ["mpssvc"] = "Windows Defender 防火墙",
        ["BFE"] = "基础筛选引擎，防火墙与 VPN（含 aTrust）依赖它",
        ["Dnscache"] = "DNS 客户端，禁用后上不了网",
        ["Dhcp"] = "DHCP 客户端，动态地址会失效",
        ["NlaSvc"] = "网络位置感知，网络分类与共享依赖它",
        ["netprofm"] = "网络列表服务",
        ["WinDefend"] = "Microsoft Defender 防病毒",
        ["SecurityHealthService"] = "Windows 安全中心",
        ["WdNisSvc"] = "Defender 网络检测",
        ["Sense"] = "Defender 高级威胁防护",
    };

    /// <summary>按前缀保护的厂商服务，与启动项侧 aTrustTray 的保护保持一致。</summary>
    private static readonly (string Prefix, string Reason)[] VendorLocks =
    [
        ("aTrust", "深信服零信任客户端，禁用可能影响公司内网 / VPN 准入"),
        ("Sangfor", "深信服零信任相关组件，禁用可能影响公司内网 / VPN 准入"),
    ];

    public static bool IsDriver(int type) => (type & DriverMask) != 0;

    public static bool IsLocked(string name, int type, ServiceStartType startType, out string? reason)
    {
        if (IsDriver(type))
        {
            reason = "驱动类服务一律只读，禁错会导致蓝屏或无法开机";
            return true;
        }

        if (startType is ServiceStartType.Boot or ServiceStartType.System)
        {
            reason = "引导期就要加载的关键服务，不能改";
            return true;
        }

        if (CriticalServices.TryGetValue(name, out var critical))
        {
            reason = critical;
            return true;
        }

        foreach (var (prefix, vendorReason) in VendorLocks)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = vendorReason;
                return true;
            }
        }

        reason = null;
        return false;
    }

    public static IReadOnlyDictionary<string, string> Critical => CriticalServices;
}
