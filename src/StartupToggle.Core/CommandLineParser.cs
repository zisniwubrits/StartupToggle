using System.Text;

namespace StartupToggle.Core;

/// <summary>把注册表里的启动命令行拆成“可执行文件路径 + 参数”，并判断文件是否还在。</summary>
public static class CommandLineParser
{
    public sealed record ParsedCommand(string? ExecutablePath, string Arguments, bool Exists);

    public static ParsedCommand Parse(string? rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return new ParsedCommand(null, string.Empty, false);
        }

        var command = Environment.ExpandEnvironmentVariables(rawCommand.Trim());

        if (command.StartsWith('"'))
        {
            var closing = command.IndexOf('"', 1);
            if (closing > 1)
            {
                var quoted = command[1..closing];
                var args = command[(closing + 1)..].Trim();
                return new ParsedCommand(quoted, args, CheckExists(quoted));
            }

            var rest = command.Trim('"');
            return new ParsedCommand(rest, string.Empty, CheckExists(rest));
        }

        var separators = new[] { ' ', '\t' };
        var spaceIndex = command.IndexOfAny(separators);
        if (spaceIndex < 0)
        {
            return new ParsedCommand(command, string.Empty, CheckExists(command));
        }

        // 未加引号的路径也可能含空格（Docker Desktop 就是这样），从最长的前缀往短试。
        var end = command.Length;
        while (end > 0)
        {
            var candidate = command[..end].TrimEnd();
            if (CheckExists(candidate))
            {
                return new ParsedCommand(candidate, command[end..].Trim(), true);
            }

            end = command.LastIndexOfAny(separators, end - 1);
        }

        var firstToken = command[..spaceIndex];
        return new ParsedCommand(firstToken, command[spaceIndex..].Trim(), CheckExists(firstToken));
    }

    /// <summary>把路径和参数拼回一条合法的命令行（含空格时自动加引号）。</summary>
    public static string Compose(string executablePath, string? arguments)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var path = executablePath.Trim().Trim('"');
            builder.Append(path.Contains(' ') ? $"\"{path}\"" : path);
        }

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(arguments.Trim());
        }

        return builder.ToString();
    }

    private static bool CheckExists(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
