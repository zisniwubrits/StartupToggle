using StartupToggle.Core;

namespace StartupToggle.SelfTest;

/// <summary>命令行恢复：读取备份文件，把每个目标恢复到本软件动手之前的状态。</summary>
internal static class RestoreMode
{
    public static int Run(bool dryRun)
    {
        var store = BackupStore.Default;
        var records = store.OriginalPerEntry();

        Console.WriteLine($"备份文件：{store.FilePath}");
        Console.WriteLine($"记录 {store.Count} 条，涉及 {records.Count} 个目标：");

        foreach (var record in records)
        {
            var original = string.Equals(record.Kind, "service", StringComparison.OrdinalIgnoreCase)
                ? $"Start={record.OriginalStart}"
                : record.OriginalApproved is null ? "（无状态值 → 启用）" : Convert.ToHexString(record.OriginalApproved);

            Console.WriteLine($"  · [{record.Kind}] {record.Location} / {record.Name}  原始={original}  最近动作={record.Action}");
        }

        if (records.Count == 0)
        {
            Console.WriteLine("没有需要恢复的记录。");
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("--dry-run：只列出，不写注册表。去掉该参数才会真正恢复。");
            return 0;
        }

        Console.WriteLine();
        var result = new RestoreEngine(WindowsRegistryGateway.Instance, store).RestoreAll();
        Console.WriteLine($"恢复完成：成功 {result.Restored}，失败 {result.Failed}");

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"  ✗ {error}");
        }

        return result.Failed == 0 ? 0 : 1;
    }
}
