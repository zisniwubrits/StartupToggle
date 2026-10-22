namespace StartupToggle.Core;

/// <summary>启动文件夹的位置，可替换以便自测指向临时目录。</summary>
public sealed class StartupEnvironment
{
    public required string UserStartupFolder { get; init; }

    public required string MachineStartupFolder { get; init; }

    public static StartupEnvironment Default { get; } = new()
    {
        UserStartupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup"),
        MachineStartupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup"),
    };

    public string FolderFor(StartupLocation location) => location switch
    {
        StartupLocation.UserStartupFolder => UserStartupFolder,
        StartupLocation.MachineStartupFolder => MachineStartupFolder,
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, "不是启动文件夹位置"),
    };
}
