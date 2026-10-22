namespace StartupToggle.Core;

/// <summary>注册表根键的抽象，便于在没有提权的自测里替换成内存实现。</summary>
public enum Hive
{
    CurrentUser,
    LocalMachine,
    Users,
}

/// <summary>一个启动项可能来自的五个位置（另加“其它用户”，只读）。</summary>
public enum StartupLocation
{
    UserRun,
    MachineRun,
    MachineRun32,
    UserStartupFolder,
    MachineStartupFolder,
    OtherUserRun,
}

/// <summary>被扫描出来的一条启动项。</summary>
public sealed class StartupItem
{
    public required string Name { get; init; }

    public required StartupLocation Location { get; init; }

    /// <summary>注册表值的数据，或启动文件夹里 .lnk 的完整路径。</summary>
    public required string Command { get; init; }

    public required bool Enabled { get; init; }

    /// <summary>StartupApproved 里的原始 12 字节；null 表示从未被写过状态（视为启用）。</summary>
    public byte[]? ApprovedData { get; init; }

    /// <summary>从命令里解析出的可执行文件路径，解析失败为 null。</summary>
    public string? ExecutablePath { get; init; }

    public bool ExecutableExists { get; init; }

    public bool IsReadOnly { get; init; }

    public bool IsLocked { get; init; }

    public string? LockReason { get; init; }

    /// <summary>其它用户的启动项会带上账户名。</summary>
    public string? Owner { get; init; }

    public bool IsFolderEntry => Location is StartupLocation.UserStartupFolder or StartupLocation.MachineStartupFolder;

    /// <summary>失效项：命令为空，或解析出的可执行文件已不存在。</summary>
    public bool IsInvalid => IsFolderEntry ? !File.Exists(Command) : string.IsNullOrWhiteSpace(Command) || !ExecutableExists;

    public string DisplayLocation =>
        Location == StartupLocation.OtherUserRun && !string.IsNullOrWhiteSpace(Owner)
            ? $"其它用户 · {Owner}"
            : LocationDisplay.Of(Location);

    public string DisplayCommand => IsFolderEntry ? Path.GetFileName(Command) : Command;

    public StartupItem WithEnabled(bool enabled) => new()
    {
        Name = Name,
        Location = Location,
        Command = Command,
        Enabled = enabled,
        ApprovedData = ApprovedData,
        ExecutablePath = ExecutablePath,
        ExecutableExists = ExecutableExists,
        IsReadOnly = IsReadOnly,
        IsLocked = IsLocked,
        LockReason = LockReason,
        Owner = Owner,
    };
}

public static class LocationDisplay
{
    public static string Of(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => "当前用户 · Run",
        StartupLocation.MachineRun => "所有用户 · Run",
        StartupLocation.MachineRun32 => "所有用户 · Run(32 位)",
        StartupLocation.UserStartupFolder => "当前用户 · 启动文件夹",
        StartupLocation.MachineStartupFolder => "公共 · 启动文件夹",
        StartupLocation.OtherUserRun => "其它用户 · Run(只读)",
        _ => location.ToString(),
    };
}

/// <summary>
/// StartupApproved 状态位的编解码。首字节最低位为 1 表示禁用：
/// 0x02 / 0x06 为启用，0x03 / 0x13 为禁用；键值不存在时系统按启用处理。
/// </summary>
public static class StartupState
{
    public const int ApprovedLength = 12;

    public static bool IsEnabled(byte[]? approved) =>
        approved is null || approved.Length == 0 || (approved[0] & 0x01) == 0;

    /// <summary>保留尾部原有字节（任务管理器写入的时间戳），只改首字节，让差异最小。</summary>
    public static byte[] Compose(byte[]? original, bool enabled)
    {
        var buffer = new byte[ApprovedLength];
        if (original is { Length: ApprovedLength })
        {
            Array.Copy(original, buffer, ApprovedLength);
        }

        buffer[0] = (byte)(enabled ? 0x02 : 0x03);
        return buffer;
    }

    public static string Describe(byte[]? approved) =>
        approved is null ? "无记录" : Convert.ToHexString(approved);
}
