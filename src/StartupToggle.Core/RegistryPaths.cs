namespace StartupToggle.Core;

/// <summary>所有硬编码的注册表位置集中在这里，方便测试时核对。</summary>
public static class RegistryPaths
{
    public const string RunRoot = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunRoot32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    public const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ApprovedRoot32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string ApprovedStartupFolderUser =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public const string ApprovedStartupFolderMachine =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public static (Hive Hive, string RunPath, string ApprovedPath) For(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => (Hive.CurrentUser, RunRoot, ApprovedRoot),
        StartupLocation.MachineRun => (Hive.LocalMachine, RunRoot, ApprovedRoot),
        StartupLocation.MachineRun32 => (Hive.LocalMachine, RunRoot32, ApprovedRoot32),
        StartupLocation.UserStartupFolder => (Hive.CurrentUser, string.Empty, ApprovedStartupFolderUser),
        StartupLocation.MachineStartupFolder => (Hive.LocalMachine, string.Empty, ApprovedStartupFolderMachine),
        StartupLocation.OtherUserRun => (Hive.Users, RunRoot, ApprovedRoot),
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    public const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public const string AppLightThemeValue = "AppsUseLightTheme";
}
