using StartupToggle.Core;
using StartupToggle.Core.Services;

namespace StartupToggle.SelfTest;

/// <summary>打印真实服务扫描结果的抽样，用来确认显示名解析、锁定规则和可操作范围。</summary>
internal static class ServiceProbe
{
    public static int Run()
    {
        Console.WriteLine("=== 真实服务扫描（只读）===");
        var scanner = new ServiceScanner(WindowsRegistryGateway.Instance);
        var services = scanner.Scan();

        var userMode = services.Where(service => !service.IsDriver).ToList();
        Console.WriteLine($"总数 {services.Count}：用户态 {userMode.Count}，驱动 {services.Count - userMode.Count}");
        Console.WriteLine($"受保护 {services.Count(service => service.IsLocked)}，其中用户态受保护 {userMode.Count(service => service.IsLocked)}");
        Console.WriteLine();

        Console.WriteLine("--- 雷电模拟器 ---");
        var ld = services.FirstOrDefault(service => service.Name.StartsWith("LDPlayer", StringComparison.OrdinalIgnoreCase));
        if (ld is null)
        {
            Console.WriteLine("  没找到 LDPlayer 相关服务");
        }
        else
        {
            Console.WriteLine($"  {ld.DisplayNameOrFallback}（{ld.Name}）启动类型={ld.DisplayStartType} 状态={ld.DisplayRunState} " +
                              $"锁定={ld.IsLocked} 可操作={ld.CanToggle}");
        }

        Console.WriteLine();
        Console.WriteLine("--- 受保护的关键服务抽样 ---");
        foreach (var service in userMode.Where(service => service.IsLocked).Take(8))
        {
            Console.WriteLine($"  {service.DisplayNameOrFallback}（{service.Name}）：{service.LockReason}");
        }

        Console.WriteLine();
        Console.WriteLine("--- 可操作的服务抽样 ---");
        foreach (var service in userMode.Where(service => service.CanToggle).Take(8))
        {
            Console.WriteLine($"  {service.DisplayNameOrFallback}（{service.Name}）启动类型={service.DisplayStartType} " +
                              $"状态={service.DisplayRunState} 被依赖={service.Dependents.Count}");
        }

        return 0;
    }
}
