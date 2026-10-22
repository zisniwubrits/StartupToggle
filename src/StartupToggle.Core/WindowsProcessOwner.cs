using System.Runtime.InteropServices;
using System.Security.Principal;

namespace StartupToggle.Core;

/// <summary>读取进程所属账户，用于“提权账户 ≠ 桌面账户”的检测。</summary>
internal static class WindowsProcessOwner
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;

    public static string? TryGetOwner(int processId) => TryGetOwner(processId, out _);

    public static string? TryGetOwner(int processId, out string? error)
    {
        error = null;

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            error = $"OpenProcess(pid={processId}) 失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
            return null;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token) || token == IntPtr.Zero)
            {
                error = $"OpenProcessToken(pid={processId}) 失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                return null;
            }

            try
            {
                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var size);
                if (size == 0)
                {
                    error = $"GetTokenInformation 取长度失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                    return null;
                }

                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (!GetTokenInformation(token, TokenUser, buffer, size, out _))
                    {
                        error = $"GetTokenInformation 取 TokenUser 失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                        return null;
                    }

                    var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                    try
                    {
                        return sid.Translate(typeof(NTAccount)).Value;
                    }
                    catch (Exception ex)
                    {
                        // 账户已被删除时翻译会失败，退回 SID 文本，仍然可以用于比对。
                        error = $"SID 翻译账户名失败（{ex.GetType().Name}），改用 SID";
                        return sid.Value;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token, int informationClass, IntPtr information, uint length, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
