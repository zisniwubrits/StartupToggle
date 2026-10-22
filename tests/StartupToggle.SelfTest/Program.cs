using System.IO;
using System.Text;
using StartupToggle.Core;

namespace StartupToggle.SelfTest;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> Failures = [];

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(arg => string.Equals(arg, "--restore", StringComparison.OrdinalIgnoreCase)))
        {
            return RestoreMode.Run(dryRun: args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase)));
        }

        if (args.Any(arg => string.Equals(arg, "--probe-services", StringComparison.OrdinalIgnoreCase)))
        {
            return ServiceProbe.Run();
        }

        if (args.Any(arg => string.Equals(arg, "--probe-io", StringComparison.OrdinalIgnoreCase)))
        {
            return IoProbe.Run();
        }

        if (args.Any(arg => string.Equals(arg, "--live-write", StringComparison.OrdinalIgnoreCase)))
        {
            return LiveWriteTest.Run();
        }

        Console.WriteLine("=== StartupToggle 自测（除最后一项外不写入真实注册表）===");
        Console.WriteLine();

        Section("状态位编解码", TestStartupState);
        Section("命令行解析", TestCommandLineParser);
        Section("扫描与状态判定", TestScanner);
        Section("开关写入与备份", TestToggleAndBackup);
        Section("一键恢复", TestRestore);
        Section("清理失效项", TestRemove);
        Section("新增启动项", TestAdd);
        Section("导出 JSON", TestExport);
        Section("服务开关（Windows 服务）", ServicesTest.Run);
        Section("真实注册表只读扫描", TestLiveScan);
        Section("界面渲染（进程内离屏）", () => UiHarness.Run(Path.Combine(FindRepositoryRoot(), "docs")));

        Console.WriteLine();
        Console.WriteLine($"结果：{_passed} 通过，{_failed} 失败");
        foreach (var failure in Failures)
        {
            Console.WriteLine($"  失败：{failure}");
        }

        return _failed == 0 ? 0 : 1;
    }

    private static void Section(string name, Action test)
    {
        Console.WriteLine($"[{name}]");
        try
        {
            test();
        }
        catch (Exception ex)
        {
            Fail($"{name} 抛出异常：{ex.GetType().Name} {ex.Message}");
        }

        Console.WriteLine();
    }

    internal static void Check(string description, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  ✓ {description}");
        }
        else
        {
            Fail(description);
        }
    }

    internal static void Fail(string description)
    {
        _failed++;
        Failures.Add(description);
        Console.WriteLine($"  ✗ {description}");
    }

    /// <summary>从测试程序所在目录往上找 StartupToggle.sln，用它作为仓库根。</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StartupToggle.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static void TestStartupState()
    {
        Check("无状态值视为启用", StartupState.IsEnabled(null));
        Check("空字节视为启用", StartupState.IsEnabled([]));
        Check("0x02 为启用", StartupState.IsEnabled([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Check("0x06 为启用", StartupState.IsEnabled([0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Check("0x03 为禁用", !StartupState.IsEnabled([0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Check("0x13 为禁用", !StartupState.IsEnabled([0x13, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        var original = new byte[] { 0x06, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB };
        var disabled = StartupState.Compose(original, enabled: false);
        Check("禁用写入首字节 0x03", disabled[0] == 0x03);
        Check("禁用保留尾部字节", disabled.Skip(1).SequenceEqual(original.Skip(1)));
        Check("禁用后长度仍为 12", disabled.Length == StartupState.ApprovedLength);

        var enabled = StartupState.Compose(original, enabled: true);
        Check("启用写入首字节 0x02", enabled[0] == 0x02);
        Check("启用后仍判定为启用", StartupState.IsEnabled(enabled));

        var fromNull = StartupState.Compose(null, enabled: false);
        Check("无原值时禁用仍写出 12 字节", fromNull.Length == 12 && fromNull[0] == 0x03);
    }

    private static void TestCommandLineParser()
    {
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        if (!File.Exists(system32))
        {
            system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        }

        var quoted = CommandLineParser.Parse($"\"{system32}\" /a /b");
        Check("带引号命令解析出可执行文件", string.Equals(quoted.ExecutablePath, system32, StringComparison.OrdinalIgnoreCase));
        Check("带引号命令解析出参数", quoted.Arguments == "/a /b");
        Check("存在的可执行文件标记为存在", quoted.Exists);

        var bare = CommandLineParser.Parse(system32);
        Check("无参数命令解析正确", string.Equals(bare.ExecutablePath, system32, StringComparison.OrdinalIgnoreCase));

        var expanded = CommandLineParser.Parse(@"%SystemRoot%\System32\notepad.exe -x");
        Check("环境变量被展开", expanded.ExecutablePath is not null && !expanded.ExecutablePath.Contains('%'));

        var missing = CommandLineParser.Parse(@"C:\不存在\foo.exe -arg");
        Check("不存在的路径标记为失效", !missing.Exists);

        var tempRoot = Path.Combine(TempRoot(), "Program Files (x86)", "Demo App");
        Directory.CreateDirectory(tempRoot);
        var spaced = Path.Combine(tempRoot, "Demo App.exe");
        File.WriteAllText(spaced, "stub");

        var unquoted = CommandLineParser.Parse($"{spaced} --background");
        Check("未加引号且含空格的路径能解析出来", string.Equals(unquoted.ExecutablePath, spaced, StringComparison.OrdinalIgnoreCase));
        Check("未加引号路径的参数不被吞进路径", unquoted.Arguments == "--background");

        var composed = CommandLineParser.Compose(spaced, "--background");
        Check("含空格路径拼回时自动加引号", composed.StartsWith('"') && composed.Contains("\" --background"));

        var plainComposed = CommandLineParser.Compose(system32, null);
        Check("无空格路径不加引号", !plainComposed.StartsWith('"'));
    }

    private static void TestScanner()
    {
        var registry = new FakeRegistryGateway();
        var env = CreateTempEnvironment();
        var manager = new StartupManager(registry, env, new BackupStore(Path.Combine(env.UserStartupFolder, "..", "backup.json")));

        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "EnabledApp", @"C:\Windows\System32\cmd.exe /c echo hi");
        registry.SeedBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "EnabledApp",
            [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "DisabledApp", @"C:\Windows\System32\cmd.exe");
        registry.SeedBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "DisabledApp",
            [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "SecurityHealth", @"C:\Windows\System32\cmd.exe");
        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "GhostApp", @"C:\不存在\ghost.exe");
        registry.SeedString(Hive.LocalMachine, RegistryPaths.RunRoot32, "Broken32", @"D:\nope\x.exe");

        var statelss = Path.Combine(env.UserStartupFolder, "NoState.lnk");
        File.WriteAllText(statelss, "stub");

        var items = manager.Scan(includeOtherUsers: false);
        Check("扫描返回全部条目", items.Count == 6);

        var enabledApp = items.Single(i => i.Name == "EnabledApp");
        Check("0x02 条目显示为启用", enabledApp.Enabled);
        Check("命中注册表的条目未标记失效", !enabledApp.IsInvalid);

        var disabledApp = items.Single(i => i.Name == "DisabledApp");
        Check("0x03 条目显示为禁用", !disabledApp.Enabled);
        Check("禁用条目仍保留原始状态字节", disabledApp.ApprovedData is { Length: 12 });

        Check("SecurityHealth 落入保护名单", items.Single(i => i.Name == "SecurityHealth").IsLocked);
        Check("普通条目不在保护名单", !enabledApp.IsLocked);

        Check("指向不存在文件的条目标记为失效", items.Single(i => i.Name == "GhostApp").IsInvalid);
        Check("32 位路径指向不存在文件也标记失效", items.Single(i => i.Name == "Broken32").IsInvalid);
        Check("32 位条目的位置显示正确", items.Single(i => i.Name == "Broken32").DisplayLocation.Contains("32"));

        var folderItem = items.Single(i => i.Name == "NoState.lnk");
        Check("启动文件夹条目被扫描到", folderItem.Location == StartupLocation.UserStartupFolder);
        Check("启动文件夹条目无状态值时视为启用", folderItem.Enabled);
        Check("启动文件夹条目未标记失效", !folderItem.IsInvalid);
        Check("启动文件夹条目显示的是文件名", folderItem.DisplayCommand == "NoState.lnk");
    }

    private static void TestToggleAndBackup()
    {
        var registry = new FakeRegistryGateway();
        var env = CreateTempEnvironment();
        var backupPath = Path.Combine(TempRoot(), $"backup-{Guid.NewGuid():N}.json");
        var backup = new BackupStore(backupPath);
        var manager = new StartupManager(registry, env, backup);

        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "Docker", @"""C:\Program Files\Docker\Docker Desktop.exe""");
        var item = manager.Scan(includeOtherUsers: false).Single(i => i.Name == "Docker");
        Check("未写入状态前视为启用", item.Enabled);

        var disable = manager.SetEnabled(item, enabled: false);
        Check("禁用操作成功", disable.Success);
        var written = registry.GetBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "Docker");
        Check("禁用写入 0x03 状态", written is { Length: 12 } && written[0] == 0x03);
        Check("Run 值未被改动",
            registry.GetString(Hive.CurrentUser, RegistryPaths.RunRoot, "Docker") is not null);
        Check("备份记录已写入", backup.Count == 1);
        Check("备份记录的是“本来没有状态值”", backup.Load()[0].OriginalApproved is null);
        Check("备份记录了动作类型", backup.Load()[0].Action == "disable");

        var rescanned = manager.Scan(includeOtherUsers: false).Single(i => i.Name == "Docker");
        Check("重新扫描后显示为禁用", !rescanned.Enabled);

        var enable = manager.SetEnabled(rescanned, enabled: true);
        Check("启用操作成功", enable.Success);
        var enabledBytes = registry.GetBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "Docker");
        Check("启用写入 0x02 状态", enabledBytes is { Length: 12 } && enabledBytes[0] == 0x02);
        Check("两次操作共两条备份", backup.Count == 2);

        var lockedItem = new StartupItem
        {
            Name = "aTrustTray",
            Location = StartupLocation.UserRun,
            Command = @"C:\a.exe",
            Enabled = true,
            IsLocked = true,
            LockReason = "保护名单",
        };
        var lockedResult = manager.SetEnabled(lockedItem, enabled: false);
        Check("保护名单条目拒绝写入", !lockedResult.Success);
        Check("保护名单拒绝时提示原因", lockedResult.Message.Contains("保护名单"));

        var readOnlyItem = new StartupItem
        {
            Name = "Other",
            Location = StartupLocation.OtherUserRun,
            Command = @"C:\b.exe",
            Enabled = true,
            IsReadOnly = true,
            Owner = "OTHER\\user",
        };
        Check("其它用户条目拒绝写入", !manager.SetEnabled(readOnlyItem, enabled: false).Success);

        var bulkSource = manager.Scan(includeOtherUsers: false);
        var bulk = manager.SetEnabledMany(bulkSource, enabled: false);
        Check("批量禁用全部成功", bulk.Failed == 0 && bulk.Succeeded == bulkSource.Count);
    }

    private static void TestRestore()
    {
        var registry = new FakeRegistryGateway();
        var env = CreateTempEnvironment();
        var backup = new BackupStore(Path.Combine(TempRoot(), $"restore-{Guid.NewGuid():N}.json"));
        var manager = new StartupManager(registry, env, backup);

        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "WasEnabled", @"C:\Windows\System32\cmd.exe");
        registry.SeedBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "WasEnabled",
            [0x02, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "WasDisabled", @"C:\Windows\System32\cmd.exe");
        registry.SeedBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "WasDisabled",
            [0x03, 9, 9, 9, 0, 0, 0, 0, 0, 0, 0, 0]);

        var items = manager.Scan(includeOtherUsers: false);
        manager.SetEnabled(items.Single(i => i.Name == "WasEnabled"), enabled: false);
        manager.SetEnabled(items.Single(i => i.Name == "WasDisabled"), enabled: true);

        var after = manager.Scan(includeOtherUsers: false);
        Check("恢复前：WasEnabled 已禁用", !after.Single(i => i.Name == "WasEnabled").Enabled);
        Check("恢复前：WasDisabled 已启用", after.Single(i => i.Name == "WasDisabled").Enabled);

        var restore = manager.RestoreAll();
        Check("恢复过程无失败", restore.Failed == 0);
        Check("恢复了两条记录", restore.Restored == 2);

        var restored = manager.Scan(includeOtherUsers: false);
        Check("恢复后：WasEnabled 回到启用", restored.Single(i => i.Name == "WasEnabled").Enabled);
        Check("恢复后：WasDisabled 回到禁用", !restored.Single(i => i.Name == "WasDisabled").Enabled);
        Check("恢复后原始字节完整还原",
            registry.GetBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "WasDisabled")!
                .SequenceEqual(new byte[] { 0x03, 9, 9, 9, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Check("恢复后备份被清空", backup.Count == 0);
    }

    private static void TestRemove()
    {
        var registry = new FakeRegistryGateway();
        var env = CreateTempEnvironment();
        var backup = new BackupStore(Path.Combine(TempRoot(), $"remove-{Guid.NewGuid():N}.json"));
        var manager = new StartupManager(registry, env, backup);

        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "DeadEntry", @"C:\不存在\dead.exe");
        var lnk = Path.Combine(env.UserStartupFolder, "DeadLink.lnk");
        File.WriteAllText(lnk, "stub");

        var items = manager.Scan(includeOtherUsers: false);
        Check("失效的注册表条目被识别", items.Single(i => i.Name == "DeadEntry").IsInvalid);

        var removeRegistry = manager.Remove(items.Single(i => i.Name == "DeadEntry"));
        Check("清理注册表失效项成功", removeRegistry.Success);
        Check("清理后 Run 值已删除",
            registry.GetString(Hive.CurrentUser, RegistryPaths.RunRoot, "DeadEntry") is null);

        var removeFile = manager.Remove(items.Single(i => i.Name == "DeadLink.lnk"));
        Check("清理启动文件夹条目成功", removeFile.Success);
        Check("清理后 .lnk 文件已删除", !File.Exists(lnk));
    }

    private static void TestAdd()
    {
        var registry = new FakeRegistryGateway();
        var env = CreateTempEnvironment();
        var manager = new StartupManager(registry, env, new BackupStore(
            Path.Combine(TempRoot(), $"add-{Guid.NewGuid():N}.json")));

        var cmd = @"C:\Windows\System32\cmd.exe /c start";
        var add = manager.Add("MyTool", cmd, StartupLocation.UserRun);
        Check("新增启动项成功", add.Success);
        Check("Run 值写入正确", registry.GetString(Hive.CurrentUser, RegistryPaths.RunRoot, "MyTool") == cmd);

        var duplicate = manager.Add("MyTool", cmd, StartupLocation.UserRun);
        Check("重名时拒绝新增", !duplicate.Success);
        Check("重名提示提到已存在", duplicate.Message.Contains("已存在"));

        var overwrite = manager.Add("MyTool", @"C:\Windows\System32\notepad.exe", StartupLocation.UserRun, overwrite: true);
        Check("允许覆盖同名项", overwrite.Success);
        Check("覆盖后命令被替换",
            registry.GetString(Hive.CurrentUser, RegistryPaths.RunRoot, "MyTool") == @"C:\Windows\System32\notepad.exe");

        Check("空名称被拒绝", !manager.Add("  ", cmd, StartupLocation.UserRun).Success);
        Check("空命令被拒绝", !manager.Add("X", "  ", StartupLocation.UserRun).Success);
        Check("启动文件夹不是合法新增目标",
            !manager.Add("Y", cmd, StartupLocation.UserStartupFolder).Success);

        registry.SeedBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "Stale", [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        manager.Add("Stale", cmd, StartupLocation.UserRun);
        Check("新增同名项会清掉残留的禁用状态",
            registry.GetBinary(Hive.CurrentUser, RegistryPaths.ApprovedRoot, "Stale") is null);
    }

    private static void TestExport()
    {
        var registry = new FakeRegistryGateway();
        var env = CreateTempEnvironment();
        var manager = new StartupManager(registry, env, new BackupStore(
            Path.Combine(TempRoot(), $"export-{Guid.NewGuid():N}.json")));

        registry.SeedString(Hive.CurrentUser, RegistryPaths.RunRoot, "中文条目", @"C:\Windows\System32\cmd.exe");
        var items = manager.Scan(includeOtherUsers: false);

        var path = Path.Combine(TempRoot(), $"export-{Guid.NewGuid():N}.json");
        JsonExporter.WriteFile(items, path);
        Check("导出文件已生成", File.Exists(path));

        var json = File.ReadAllText(path);
        Check("JSON 里保留中文原文", json.Contains("中文条目"));
        Check("JSON 含 generatedAt", json.Contains("generatedAt"));
        Check("JSON 条目数与扫描一致", json.Contains("\"count\": 1"));
        Check("JSON 含启用状态字段", json.Contains("\"enabled\": true"));
    }

    private static void TestLiveScan()
    {
        var manager = StartupManager.CreateDefault();
        IReadOnlyList<StartupItem> items;
        try
        {
            items = manager.Scan();
        }
        catch (Exception ex)
        {
            Fail($"真实扫描抛异常：{ex.Message}");
            return;
        }

        Check($"真实注册表扫描到 {items.Count} 条启动项", items.Count > 0);

        var perLocation = items.GroupBy(i => i.Location)
            .OrderBy(g => g.Key)
            .Select(g => $"{LocationDisplay.Of(g.Key)}={g.Count()}");
        Console.WriteLine($"     分布：{string.Join("，", perLocation)}");
        Console.WriteLine($"     失效项：{items.Count(i => i.IsInvalid)}，锁定项：{items.Count(i => i.IsLocked)}，只读项：{items.Count(i => i.IsReadOnly)}");

        foreach (var item in items.Where(i => i.IsLocked || i.IsInvalid).Take(5))
        {
            Console.WriteLine($"     · {item.Name} [{item.DisplayLocation}] 锁定={item.IsLocked} 失效={item.IsInvalid}");
        }

        Check("扫描过程中没有条目抛出异常且名称非空", items.All(i => !string.IsNullOrWhiteSpace(i.Name)));
        Check("当前进程与桌面账户判定不抛异常", AccountContext.CurrentUser.Length > 0);
        Check("桌面账户能被探测到（token 或注册表回退）", AccountContext.InteractiveUser is not null);
        Check("账户一致性判定：DOMAIN\\user 与 .\\user 视为同一账户",
            AccountContext.SameAccount(@"DESKTOP-A\alice", @".\alice"));
        Check("账户一致性判定：不同账户名视为不同",
            !AccountContext.SameAccount(@"DESKTOP-A\alice", @"DESKTOP-A\bob"));
        Check("账户一致性判定：未知时不报警",
            !AccountContext.IsMismatch || AccountContext.InteractiveUser is not null);
        Console.WriteLine($"     当前进程账户：{AccountContext.CurrentUser}，提权：{AccountContext.IsElevated}，" +
                          $"桌面账户：{AccountContext.InteractiveUser ?? "(未知)"}，账户一致：{!AccountContext.IsMismatch}");
        foreach (var line in AccountContext.InteractiveLookupTrace)
        {
            Console.WriteLine($"     账户探测：{line}");
        }

        Console.WriteLine($"     备份文件：{manager.Backup.FilePath}（现有 {manager.Backup.Count} 条记录）");
    }

    internal static StartupEnvironment CreateTempEnvironment()
    {
        var root = Path.Combine(TempRoot(), Guid.NewGuid().ToString("N"));
        var user = Path.Combine(root, "UserStartup");
        var machine = Path.Combine(root, "MachineStartup");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(machine);

        return new StartupEnvironment
        {
            UserStartupFolder = user,
            MachineStartupFolder = machine,
        };
    }

    /// <summary>
    /// 临时文件放在仓库目录下而不是 %TEMP%：受限环境下 %TEMP% 可能不可写，
    /// 而工作区一定是可写的，这样自测在哪都能跑。
    /// </summary>
    internal static string TempRoot()
    {
        var root = Path.Combine(FindRepositoryRoot(), ".tmp");
        Directory.CreateDirectory(root);
        return root;
    }
}
