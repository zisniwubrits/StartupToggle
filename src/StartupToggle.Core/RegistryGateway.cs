using Microsoft.Win32;

namespace StartupToggle.Core;

/// <summary>注册表访问的最小接口，写入路径全部经过它，方便自测替换成内存实现。</summary>
public interface IRegistryGateway
{
    bool KeyExists(Hive hive, string path);

    IReadOnlyList<string> GetValueNames(Hive hive, string path);

    IReadOnlyList<string> GetSubKeyNames(Hive hive, string path);

    string? GetString(Hive hive, string path, string name);

    byte[]? GetBinary(Hive hive, string path, string name);

    int? GetInt32(Hive hive, string path, string name);

    IReadOnlyList<string> GetMultiString(Hive hive, string path, string name);

    void SetString(Hive hive, string path, string name, string value);

    void SetInt32(Hive hive, string path, string name, int value);

    void SetBinary(Hive hive, string path, string name, byte[] value);

    bool DeleteValue(Hive hive, string path, string name);
}

/// <summary>真实注册表实现，固定使用 64 位视图（32 位启动项走 WOW6432Node 显式路径）。</summary>
public sealed class WindowsRegistryGateway : IRegistryGateway
{
    public static WindowsRegistryGateway Instance { get; } = new();

    public bool KeyExists(Hive hive, string path)
    {
        using var key = Open(hive, path, writable: false);
        return key is not null;
    }

    public IReadOnlyList<string> GetValueNames(Hive hive, string path)
    {
        using var key = Open(hive, path, writable: false);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(Hive hive, string path)
    {
        using var key = Open(hive, path, writable: false);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public string? GetString(Hive hive, string path, string name)
    {
        using var key = Open(hive, path, writable: false);
        return key?.GetValue(name) as string;
    }

    public byte[]? GetBinary(Hive hive, string path, string name)
    {
        using var key = Open(hive, path, writable: false);
        return key?.GetValue(name) switch
        {
            byte[] bytes => bytes,
            string text when text.Length > 0 => Convert.FromHexString(text.Replace(" ", string.Empty)),
            _ => null,
        };
    }

    public void SetString(Hive hive, string path, string name, string value)
    {
        using var key = Open(hive, path, writable: true)
                        ?? throw new InvalidOperationException($"无法打开注册表键 {hive}\\{path}");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public int? GetInt32(Hive hive, string path, string name)
    {
        using var key = Open(hive, path, writable: false);
        return key?.GetValue(name) switch
        {
            int value => value,
            long value => (int)value,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    public IReadOnlyList<string> GetMultiString(Hive hive, string path, string name)
    {
        using var key = Open(hive, path, writable: false);
        return key?.GetValue(name) switch
        {
            string[] values => values,
            string single when single.Length > 0 => [single],
            _ => Array.Empty<string>(),
        };
    }

    public void SetInt32(Hive hive, string path, string name, int value)
    {
        using var key = Open(hive, path, writable: true)
                        ?? throw new InvalidOperationException($"无法打开注册表键 {hive}\\{path}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void SetBinary(Hive hive, string path, string name, byte[] value)
    {
        using var key = Open(hive, path, writable: true)
                        ?? throw new InvalidOperationException($"无法打开注册表键 {hive}\\{path}");
        key.SetValue(name, value, RegistryValueKind.Binary);
    }

    public bool DeleteValue(Hive hive, string path, string name)
    {
        using var key = Open(hive, path, writable: true);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        key.DeleteValue(name, throwOnMissingValue: false);
        return true;
    }

    private static RegistryKey? Open(Hive hive, string path, bool writable)
    {
        var root = RegistryKey.OpenBaseKey(
            hive switch
            {
                Hive.CurrentUser => RegistryHive.CurrentUser,
                Hive.LocalMachine => RegistryHive.LocalMachine,
                Hive.Users => RegistryHive.Users,
                _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, null),
            },
            RegistryView.Registry64);

        if (!writable)
        {
            var readable = root.OpenSubKey(path, writable: false);
            root.Dispose();
            return readable;
        }

        try
        {
            return root.CreateSubKey(path, writable: true);
        }
        finally
        {
            root.Dispose();
        }
    }
}
