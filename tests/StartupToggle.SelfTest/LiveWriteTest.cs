using System.IO;
using Microsoft.Win32;
using StartupToggle.Core;

namespace StartupToggle.SelfTest;

/// <summary>
/// 真实写入验证：只动 HKCU 的 StartupApproved 状态位，Run 值与快捷方式不碰，
/// 结束时按开始时抓到的原始字节还原，并用独立于被测代码的读取路径复核。
/// </summary>
internal static class LiveWriteTest
{
    private const string ApprovedPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static int Run()
    {
        Console.WriteLine("=== 真实写入验证（HKCU，结束立即还原）===");

        var manager = new StartupManager(
            WindowsRegistryGateway.Instance,
            StartupEnvironment.Default,
            new BackupStore(Path.Combine(AppContext.BaseDirectory, "live-write-backup.json")));
        Console.WriteLine($"备份文件：{manager.Backup.FilePath}");
        var candidates = manager.Scan(includeOtherUsers: false)
            .Where(item => item.Location == StartupLocation.UserRun && !item.IsLocked && !item.IsReadOnly)
            .ToList();

        var target = candidates.FirstOrDefault(item => item.Enabled && !item.IsInvalid) ?? candidates.FirstOrDefault();
        if (target is null)
        {
            Console.WriteLine("找不到可测的 HKCU 启动项，跳过。");
            return 1;
        }

        var originalApproved = ReadApprovedRaw(target.Name);
        var originalRunValue = ReadRunValueRaw(target.Name);
        Console.WriteLine($"目标项：{target.Name}");
        Console.WriteLine($"  原始状态值：{(originalApproved is null ? "（不存在，系统默认启用）" : Convert.ToHexString(originalApproved))}");
        Console.WriteLine($"  原始 Run 值：{originalRunValue}");
        Console.WriteLine();

        var failures = 0;
        void Verify(string description, bool condition)
        {
            Console.WriteLine(condition ? $"  ✓ {description}" : $"  ✗ {description}");
            if (!condition)
            {
                failures++;
            }
        }

        try
        {
            var disable = manager.SetEnabled(target, enabled: false);
            Verify($"禁用返回成功（{disable.Message}）", disable.Success);

            var afterDisable = ReadApprovedRaw(target.Name);
            Verify($"禁用后首字节为 0x03（实际 {Describe(afterDisable)}）",
                afterDisable is { Length: 12 } && afterDisable[0] == 0x03);

            var rescan = manager.Scan(includeOtherUsers: false)
                .FirstOrDefault(item => item.Location == target.Location && item.Name == target.Name);
            Verify("重新扫描后显示为已禁用", rescan is { Enabled: false });

            var enable = manager.SetEnabled(rescan!, enabled: true);
            Verify($"启用返回成功（{enable.Message}）", enable.Success);

            var afterEnable = ReadApprovedRaw(target.Name);
            Verify($"启用后首字节为 0x02（实际 {Describe(afterEnable)}）",
                afterEnable is { Length: 12 } && afterEnable[0] == 0x02);

            Verify("Run 值全程未被改动",
                string.Equals(ReadRunValueRaw(target.Name), originalRunValue, StringComparison.Ordinal));

            var restore = manager.RestoreAll();
            Verify($"一键恢复无失败（恢复 {restore.Restored} 条）", restore.Failed == 0);

            var afterRestore = ReadApprovedRaw(target.Name);
            Verify($"恢复后状态值与开始时一致（实际 {Describe(afterRestore)}）",
                (afterRestore is null && originalApproved is null) ||
                (afterRestore is not null && originalApproved is not null &&
                 afterRestore.SequenceEqual(originalApproved)));

            Verify("备份文件已清空", manager.Backup.Count == 0);
        }
        finally
        {
            // 兜底：无论上面哪一步爆炸，都按原始字节写回；连还原都失败也要把话说清楚而不是抛出去。
            try
            {
                RestoreRaw(target.Name, originalApproved);
                var final = ReadApprovedRaw(target.Name);
                var ok = (final is null && originalApproved is null) ||
                         (final is not null && originalApproved is not null && final.SequenceEqual(originalApproved));
                Console.WriteLine();
                Console.WriteLine(ok
                    ? $"兜底还原确认：{target.Name} 已回到 {Describe(final)}"
                    : $"兜底还原失败：{target.Name} 现在 {Describe(final)}，应为 {Describe(originalApproved)}");
                if (!ok)
                {
                    failures++;
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine();
                Console.WriteLine($"兜底还原本身失败：{ex.GetType().Name} {ex.Message}");
                Console.WriteLine($"请手动把 {ApprovedPath}\\{target.Name} 恢复成 {Describe(originalApproved)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "真实写入验证：全部通过" : $"真实写入验证：{failures} 项失败");
        return failures == 0 ? 0 : 1;
    }

    private static string Describe(byte[]? value) => value is null ? "（无状态值）" : Convert.ToHexString(value);

    private static byte[]? ReadApprovedRaw(string name) => ReadBinary(ApprovedPath, name);

    private static string? ReadRunValueRaw(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(name) as string;
    }

    private static byte[]? ReadBinary(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) as byte[];
    }

    private static void RestoreRaw(string name, byte[]? original)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ApprovedPath, writable: true);
        if (original is null)
        {
            if (key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }

            return;
        }

        key.SetValue(name, original, RegistryValueKind.Binary);
    }
}
