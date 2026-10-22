using System.Security.Principal;

namespace StartupToggle.Core;

/// <summary>枚举五个位置（外加其它用户的 Run）里的全部启动项，只读，不做任何写入。</summary>
public sealed class StartupScanner
{
    private static readonly string[] ShortcutExtensions = [".lnk", ".url"];

    private readonly IRegistryGateway _registry;
    private readonly StartupEnvironment _environment;
    private readonly ProtectionPolicy _policy;

    public StartupScanner(
        IRegistryGateway registry,
        StartupEnvironment environment,
        ProtectionPolicy? policy = null)
    {
        _registry = registry;
        _environment = environment;
        _policy = policy ?? new ProtectionPolicy();
    }

    public IReadOnlyList<StartupItem> Scan(bool includeOtherUsers = true)
    {
        var items = new List<StartupItem>();

        ScanRegistry(items, StartupLocation.UserRun, Hive.CurrentUser);
        ScanRegistry(items, StartupLocation.MachineRun, Hive.LocalMachine);
        ScanRegistry(items, StartupLocation.MachineRun32, Hive.LocalMachine);
        ScanFolder(items, StartupLocation.UserStartupFolder);
        ScanFolder(items, StartupLocation.MachineStartupFolder);

        if (includeOtherUsers)
        {
            ScanOtherUsers(items);
        }

        return items
            .OrderBy(item => item.Location)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void ScanRegistry(List<StartupItem> sink, StartupLocation location, Hive hive)
    {
        var (_, runPath, approvedPath) = RegistryPaths.For(location);

        foreach (var name in _registry.GetValueNames(hive, runPath))
        {
            var command = _registry.GetString(hive, runPath, name) ?? string.Empty;
            var approved = _registry.GetBinary(hive, approvedPath, name);
            sink.Add(Build(name, location, command, approved, description: command, owner: null, readOnly: false));
        }
    }

    private void ScanFolder(List<StartupItem> sink, StartupLocation location)
    {
        var (hive, _, approvedPath) = RegistryPaths.For(location);
        var folder = _environment.FolderFor(location);
        if (!Directory.Exists(folder))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder)
                .Where(path => ShortcutExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                               || string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var approved = _registry.GetBinary(hive, approvedPath, fileName);
            var parsed = new CommandLineParser.ParsedCommand(file, string.Empty, File.Exists(file));
            sink.Add(Build(fileName, location, file, approved, parsed, owner: null, readOnly: false));
        }
    }

    private void ScanOtherUsers(List<StartupItem> sink)
    {
        IReadOnlyList<string> sids;
        try
        {
            sids = _registry.GetSubKeyNames(Hive.Users, string.Empty);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var sid in sids)
        {
            if (sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sid, AccountContext.CurrentUserSid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var runPath = $@"{sid}\{RegistryPaths.RunRoot}";
            var approvedPath = $@"{sid}\{RegistryPaths.ApprovedRoot}";

            IReadOnlyList<string> names;
            try
            {
                names = _registry.GetValueNames(Hive.Users, runPath);
            }
            catch (Exception)
            {
                continue;
            }

            if (names.Count == 0)
            {
                continue;
            }

            var owner = DescribeSid(sid);
            foreach (var name in names)
            {
                var command = _registry.GetString(Hive.Users, runPath, name) ?? string.Empty;
                var approved = _registry.GetBinary(Hive.Users, approvedPath, name);
                sink.Add(Build(name, StartupLocation.OtherUserRun, command, approved, command, owner, readOnly: true));
            }
        }
    }

    private StartupItem Build(
        string name,
        StartupLocation location,
        string command,
        byte[]? approved,
        string? description,
        string? owner,
        bool readOnly)
    {
        var parsed = CommandLineParser.Parse(description);
        var locked = _policy.IsLocked(name, out var reason);

        return new StartupItem
        {
            Name = name,
            Location = location,
            Command = command,
            Enabled = StartupState.IsEnabled(approved),
            ApprovedData = approved,
            ExecutablePath = parsed.ExecutablePath,
            ExecutableExists = parsed.Exists,
            IsReadOnly = readOnly,
            IsLocked = locked,
            LockReason = locked ? reason : null,
            Owner = owner,
        };
    }

    private StartupItem Build(
        string name,
        StartupLocation location,
        string command,
        byte[]? approved,
        CommandLineParser.ParsedCommand parsed,
        string? owner,
        bool readOnly)
    {
        var locked = _policy.IsLocked(name, out var reason);

        return new StartupItem
        {
            Name = name,
            Location = location,
            Command = command,
            Enabled = StartupState.IsEnabled(approved),
            ApprovedData = approved,
            ExecutablePath = parsed.ExecutablePath,
            ExecutableExists = parsed.Exists,
            IsReadOnly = readOnly,
            IsLocked = locked,
            LockReason = locked ? reason : null,
            Owner = owner,
        };
    }

    private static string DescribeSid(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch
        {
            return sid;
        }
    }
}
