using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace StartupToggle.SelfTest;

/// <summary>诊断用：把当前进程能做什么、不能做什么，以及具体的 Win32 错误码打出来。</summary>
internal static class IoProbe
{
    public static int Run()
    {
        var identity = WindowsIdentity.GetCurrent();
        Console.WriteLine($"账户：{identity.Name}");
        Console.WriteLine($"SID：{identity.User?.Value}");
        Console.WriteLine($"提权：{new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)}");
        Console.WriteLine($"令牌类型：{identity.ImpersonationLevel} / 集成：{identity.IsSystem}");
        Console.WriteLine($"令牌是否受限：{(identity.Token != IntPtr.Zero ? "见下方组列表" : "无令牌")}");

        try
        {
            var groups = identity.Groups?.Select(g =>
            {
                try
                {
                    return g.Translate(typeof(NTAccount)).Value;
                }
                catch
                {
                    return g.Value;
                }
            }) ?? [];
            Console.WriteLine($"组：{string.Join(" | ", groups)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"组读取失败：{ex.Message}");
        }

        Console.WriteLine($"LocalApplicationData：{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
        Console.WriteLine($"进程目录：{AppContext.BaseDirectory}");
        Console.WriteLine();

        ProbeRegistry("HKCU\\Software\\StartupToggleProbe (默认视图)", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey("Software\\StartupToggleProbe", writable: true);
            key.SetValue("probe", "1");
            return "写入成功";
        }, () => Registry.CurrentUser.DeleteSubKeyTree("Software\\StartupToggleProbe", throwOnMissingSubKey: false));

        ProbeRegistry("HKCU\\Software\\StartupToggleProbe64 (显式 64 位视图)", () =>
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = root.CreateSubKey("Software\\StartupToggleProbe64", writable: true);
            key.SetValue("probe", "1");
            return "写入成功";
        }, () =>
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            root.DeleteSubKeyTree("Software\\StartupToggleProbe64", throwOnMissingSubKey: false);
        });

        ProbeRegistry("StartupApproved\\Run 只写回同值", () =>
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            key.SetValue("BaiduYunDetect", new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            return "写入成功";
        }, () => { });

        ProbeFile("%LOCALAPPDATA 下的文件",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartupToggle", "probe.txt"));

        ProbeFile("工作区内的文件", Path.Combine(AppContext.BaseDirectory, "probe.txt"));

        return 0;
    }

    private static void ProbeRegistry(string description, Func<string> action, Action cleanup)
    {
        try
        {
            var result = action();
            Console.WriteLine($"  ✓ 注册表 {description}：{result}");
            try
            {
                cleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    清理失败：{ex.GetType().Name} {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            var code = ex is UnauthorizedAccessException ? $"HRESULT=0x{ex.HResult:X8}" : ex.Message;
            Console.WriteLine($"  ✗ 注册表 {description}：{ex.GetType().Name} {code}");
        }
    }

    private static void ProbeFile(string description, string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "probe");
            File.Delete(path);
            Console.WriteLine($"  ✓ 文件 {description}：可写（{path}）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 文件 {description}：{ex.GetType().Name} HRESULT=0x{ex.HResult:X8} {ex.Message}（{path}）");
        }
    }
}
