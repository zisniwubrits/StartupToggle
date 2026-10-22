using System.Text.Json;

namespace StartupToggle.Core;

/// <summary>把扫描结果导出成可读的 JSON，便于事后核对。</summary>
public static class JsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(IEnumerable<StartupItem> items, DateTimeOffset? generatedAt = null)
    {
        var payload = new
        {
            generatedAt = (generatedAt ?? DateTimeOffset.Now).ToString("O"),
            machine = Environment.MachineName,
            user = AccountContext.CurrentUser,
            elevated = AccountContext.IsElevated,
            count = items.Count(),
            items = items.Select(item => new
            {
                name = item.Name,
                location = item.DisplayLocation,
                command = item.DisplayCommand,
                enabled = item.Enabled,
                locked = item.IsLocked,
                lockReason = item.LockReason,
                readOnly = item.IsReadOnly,
                executable = item.ExecutablePath,
                executableExists = item.ExecutableExists,
                invalid = item.IsInvalid,
                approvedState = StartupState.Describe(item.ApprovedData),
            }).ToList(),
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    public static string WriteFile(IEnumerable<StartupItem> items, string path)
    {
        var json = ToJson(items);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
        return path;
    }
}
