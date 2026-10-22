using System.Runtime.InteropServices;
using System.Text;

namespace StartupToggle.Core.Services;

/// <summary>枚举 HKLM\SYSTEM\CurrentControlSet\Services 下的全部服务与驱动。</summary>
public sealed class ServiceScanner
{
    public const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    private readonly IRegistryGateway _registry;
    private readonly IServiceControl _control;

    public ServiceScanner(IRegistryGateway registry, IServiceControl? control = null)
    {
        _registry = registry;
        _control = control ?? Win32ServiceControl.Instance;
    }

    public IReadOnlyList<ServiceInfo> Scan(bool queryRunState = true)
    {
        var names = _registry.GetSubKeyNames(Hive.LocalMachine, ServicesRoot);

        var raw = new List<RawService>(names.Count);
        foreach (var name in names)
        {
            var path = $@"{ServicesRoot}\{name}";
            var type = _registry.GetInt32(Hive.LocalMachine, path, "Type");
            var start = _registry.GetInt32(Hive.LocalMachine, path, "Start");
            if (type is null || start is null)
            {
                // 不是服务（Services 键下偶尔有别的子键），跳过。
                continue;
            }

            raw.Add(new RawService(
                name,
                ResolveDisplayName(_registry.GetString(Hive.LocalMachine, path, "DisplayName"), name),
                _registry.GetString(Hive.LocalMachine, path, "Description"),
                (ServiceStartType)start.Value,
                _registry.GetInt32(Hive.LocalMachine, path, "DelayedAutoStart") == 1,
                type.Value,
                _registry.GetMultiString(Hive.LocalMachine, path, "DependOnService")));
        }

        // 反向依赖表：谁依赖我。禁用前要能说清“会牵连几个服务”。
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in raw)
        {
            foreach (var dependency in service.DependsOn)
            {
                if (!dependents.TryGetValue(dependency, out var list))
                {
                    list = [];
                    dependents[dependency] = list;
                }

                list.Add(service.Name);
            }
        }

        var result = new List<ServiceInfo>(raw.Count);
        foreach (var service in raw)
        {
            var isDriver = ServiceProtection.IsDriver(service.Type);
            var locked = ServiceProtection.IsLocked(service.Name, service.Type, service.StartType, out var reason);

            // 驱动一律不查运行态：OpenService 对几百个驱动太慢，而且驱动状态对用户没意义。
            var state = queryRunState && !isDriver
                ? _control.QueryState(service.Name)
                : ServiceRunState.Unknown;

            result.Add(new ServiceInfo
            {
                Name = service.Name,
                DisplayName = service.DisplayName,
                Description = service.Description,
                StartType = service.StartType,
                Delayed = service.Delayed,
                Type = service.Type,
                RunState = state,
                DependsOn = service.DependsOn,
                Dependents = dependents.TryGetValue(service.Name, out var list) ? list : [],
                IsDriver = isDriver,
                IsLocked = locked,
                LockReason = locked ? reason : null,
            });
        }

        return result
            .OrderBy(service => service.DisplayNameOrFallback, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>服务显示名可能是 "@oem55.inf,%name%;aTrustXtun" 这种间接字符串，尽量解析成人话。</summary>
    private static string ResolveDisplayName(string? displayName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return fallback;
        }

        var text = displayName.Trim();
        if (!text.StartsWith('@'))
        {
            return text;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            if (SHLoadIndirectString(CompleteIndirectPath(text), buffer, buffer.Capacity, IntPtr.Zero) == 0)
            {
                var resolved = buffer.ToString().Trim();
                if (resolved.Length > 0 && !resolved.StartsWith('@'))
                {
                    return resolved;
                }
            }
        }
        catch
        {
            // shlwapi 不可用时走下面的兜底。
        }

        // 解析不出来就退回服务名：把 "@WaaSMedicSvcImpl.dll,-100" 直接显示给用户没有意义。
        return fallback;
    }

    /// <summary>
    /// 间接字符串里的文件名常常是相对的，SHLoadIndirectString 需要完整路径：
    /// "@WaaSMedicSvcImpl.dll,-100" 补成 System32 下的 dll，
    /// "@oem45.inf,%NAME%;Svc" 补成 INF 目录下的 inf。
    /// </summary>
    private static string CompleteIndirectPath(string text)
    {
        var expanded = Environment.ExpandEnvironmentVariables(text);
        if (!expanded.StartsWith('@'))
        {
            return expanded;
        }

        var body = expanded[1..];
        var comma = body.IndexOf(',');
        if (comma <= 0)
        {
            return expanded;
        }

        var file = body[..comma];
        if (file.Contains('\\') || file.Contains('/'))
        {
            return expanded;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var directory = file.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(windows, "INF")
            : Path.Combine(windows, "System32");

        return "@" + Path.Combine(directory, file) + body[comma..];
    }

    private sealed record RawService(
        string Name,
        string DisplayName,
        string? Description,
        ServiceStartType StartType,
        bool Delayed,
        int Type,
        IReadOnlyList<string> DependsOn);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string source, StringBuilder buffer, int bufferSize, IntPtr reserved);
}
