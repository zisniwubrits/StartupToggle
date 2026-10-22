using System.Security.Principal;

namespace StartupToggle.Core;

/// <summary>内置保护名单：这些项在界面上置灰，任何写入都会被拒绝。</summary>
public sealed class ProtectionPolicy
{
    private static readonly Dictionary<string, string> Locked = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SecurityHealth"] = "Windows 安全中心托盘，禁用会让安全状态不再提示",
        ["SecurityHealthSystray"] = "Windows 安全中心托盘，禁用会让安全状态不再提示",
        ["aTrustTray"] = "深信服零信任客户端，禁用可能影响公司内网 / VPN 准入",
    };

    public bool IsLocked(string name, out string? reason) => Locked.TryGetValue(name, out reason);

    public IReadOnlyDictionary<string, string> Entries => Locked;
}

/// <summary>当前进程账户与桌面登录账户的对照，用于决定是否显示黄条。</summary>
public static class AccountContext
{
    private static readonly List<string> LookupTrace = [];

    public static string CurrentUser { get; } = SafeGet(() =>
        WindowsIdentity.GetCurrent().Name, "未知");

    public static bool IsElevated { get; } = SafeGet(() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), false);

    /// <summary>当前进程 SID，用于在 HKEY_USERS 下识别“自己”。</summary>
    public static string? CurrentUserSid { get; } = SafeGet(() =>
        WindowsIdentity.GetCurrent().User?.Value, (string?)null);

    private static readonly Lazy<string?> LazyInteractiveUser = new(FindInteractiveUser);

    /// <summary>拥有桌面会话的账户，即真正会执行启动项的账户；取不到时为 null。</summary>
    public static string? InteractiveUser => LazyInteractiveUser.Value;

    /// <summary>探测过程日志，出问题时能直接看到卡在哪一步。</summary>
    public static IReadOnlyList<string> InteractiveLookupTrace
    {
        get
        {
            _ = LazyInteractiveUser.Value;
            return LookupTrace;
        }
    }

    public static bool IsMismatch => !SameAccount(CurrentUser, InteractiveUser);

    /// <summary>
    /// 同一账户可能写成 DOMAIN\user、.\user 或 MACHINE\user，比较时只看最后一段账户名，
    /// 否则“本机管理员 vs 本地账户”会被误判成不一致，黄条就永远挂着。
    /// </summary>
    public static bool SameAccount(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(AccountName(left), AccountName(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string AccountName(string value)
    {
        var separator = value.LastIndexOf('\\');
        return (separator >= 0 ? value[(separator + 1)..] : value).Trim();
    }

    private static string? FindInteractiveUser()
    {
        foreach (var process in SafeGet(() => System.Diagnostics.Process.GetProcessesByName("explorer"),
                     Array.Empty<System.Diagnostics.Process>()))
        {
            try
            {
                var owner = WindowsProcessOwner.TryGetOwner(process.Id, out var error);
                if (owner is not null)
                {
                    LookupTrace.Add($"explorer.exe(pid={process.Id}) 归属账户：{owner}");
                    return owner;
                }

                LookupTrace.Add(error ?? $"explorer.exe(pid={process.Id}) 未取到账户");
            }
            catch (Exception ex)
            {
                LookupTrace.Add($"explorer.exe(pid={process.Id}) 探测异常：{ex.GetType().Name}");
            }
        }

        if (LookupTrace.Count == 0)
        {
            LookupTrace.Add("没有找到 explorer.exe 进程");
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            if (key?.GetValue("LastLoggedOnUser") is string lastUser && lastUser.Length > 0)
            {
                LookupTrace.Add($"改用注册表 LastLoggedOnUser：{lastUser}");
                return lastUser;
            }

            LookupTrace.Add("注册表 LastLoggedOnUser 不可读");
        }
        catch (Exception ex)
        {
            LookupTrace.Add($"读取 LastLoggedOnUser 异常：{ex.GetType().Name}");
        }

        return null;
    }

    private static T SafeGet<T>(Func<T> factory, T fallback)
    {
        try
        {
            return factory();
        }
        catch
        {
            return fallback;
        }
    }
}
