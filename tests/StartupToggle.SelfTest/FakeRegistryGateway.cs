using StartupToggle.Core;

namespace StartupToggle.SelfTest;

/// <summary>内存版注册表，让写入路径可以在没有提权的情况下被完整验证。</summary>
internal sealed class FakeRegistryGateway : IRegistryGateway
{
    private readonly Dictionary<(Hive, string), Dictionary<string, object>> _keys = new(TupleComparer.Instance);

    public HashSet<(Hive, string)> ReadDenied { get; } = new(TupleComparer.Instance);

    public void SeedString(Hive hive, string path, string name, string value) =>
        Ensure(hive, path)[name] = value;

    public void SeedBinary(Hive hive, string path, string name, byte[] value) =>
        Ensure(hive, path)[name] = value;

    public void SeedSubKey(Hive hive, string path, string subKey) => Ensure(hive, path);

    public bool KeyExists(Hive hive, string path) => _keys.ContainsKey((hive, path));

    public IReadOnlyList<string> GetValueNames(Hive hive, string path) =>
        _keys.TryGetValue((hive, path), out var values) ? values.Keys.ToList() : [];

    public IReadOnlyList<string> GetSubKeyNames(Hive hive, string path) =>
        _keys.Keys.Where(k => k.Item1 == hive)
            .Select(k => k.Item2)
            .Where(p => p.StartsWith(path.Length == 0 ? string.Empty : path + "\\", StringComparison.OrdinalIgnoreCase) &&
                        p.Length > path.Length &&
                        !p[(path.Length == 0 ? 0 : path.Length + 1)..].Contains('\\'))
            .Select(p => p[(path.Length == 0 ? 0 : path.Length + 1)..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public string? GetString(Hive hive, string path, string name) =>
        _keys.TryGetValue((hive, path), out var values) && values.TryGetValue(name, out var raw)
            ? raw as string
            : null;

    public byte[]? GetBinary(Hive hive, string path, string name) =>
        _keys.TryGetValue((hive, path), out var values) && values.TryGetValue(name, out var raw)
            ? raw as byte[]
            : null;

    public int? GetInt32(Hive hive, string path, string name)
    {
        if (!_keys.TryGetValue((hive, path), out var values) || !values.TryGetValue(name, out var raw))
        {
            return null;
        }

        return raw switch
        {
            int value => value,
            long value => (int)value,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    public IReadOnlyList<string> GetMultiString(Hive hive, string path, string name)
    {
        if (!_keys.TryGetValue((hive, path), out var values) || !values.TryGetValue(name, out var raw))
        {
            return [];
        }

        return raw switch
        {
            string[] array => array,
            string single when single.Length > 0 => [single],
            _ => [],
        };
    }

    public void SeedInt32(Hive hive, string path, string name, int value) =>
        Ensure(hive, path)[name] = value;

    public void SeedMultiString(Hive hive, string path, string name, params string[] values) =>
        Ensure(hive, path)[name] = values;

    public void SetInt32(Hive hive, string path, string name, int value) =>
        Ensure(hive, path)[name] = value;

    public void SetString(Hive hive, string path, string name, string value) =>
        Ensure(hive, path)[name] = value;

    public void SetBinary(Hive hive, string path, string name, byte[] value) =>
        Ensure(hive, path)[name] = value;

    public bool DeleteValue(Hive hive, string path, string name) =>
        _keys.TryGetValue((hive, path), out var values) && values.Remove(name);

    private Dictionary<string, object> Ensure(Hive hive, string path)
    {
        if (!_keys.TryGetValue((hive, path), out var values))
        {
            values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _keys[(hive, path)] = values;
        }

        return values;
    }

    private sealed class TupleComparer : IEqualityComparer<(Hive, string)>
    {
        public static TupleComparer Instance { get; } = new();

        public bool Equals((Hive, string) x, (Hive, string) y) =>
            x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Hive, string) obj) =>
            HashCode.Combine(obj.Item1, obj.Item2.ToLowerInvariant());
    }
}
