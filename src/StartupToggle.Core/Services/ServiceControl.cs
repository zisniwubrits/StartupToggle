using System.Runtime.InteropServices;

namespace StartupToggle.Core.Services;

/// <summary>服务的运行态查询与控制。抽成接口是为了让自测不必真的去碰 SCM。</summary>
public interface IServiceControl
{
    ServiceRunState QueryState(string serviceName);

    /// <summary>停止服务并等待其真正停下；返回是否停成功。</summary>
    bool TryStop(string serviceName, TimeSpan timeout, out string? error);

    bool TryStart(string serviceName, out string? error);
}

/// <summary>基于 advapi32 的真实实现，不依赖任何 NuGet 包。</summary>
public sealed class Win32ServiceControl : IServiceControl
{
    public static Win32ServiceControl Instance { get; } = new();

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceControlStop = 0x00000001;

    public ServiceRunState QueryState(string serviceName)
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return ServiceRunState.Unknown;
        }

        try
        {
            var service = OpenServiceW(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                return ServiceRunState.Unknown;
            }

            try
            {
                var status = new ServiceStatus();
                if (!QueryServiceStatus(service, ref status))
                {
                    return ServiceRunState.Unknown;
                }

                return Enum.IsDefined(typeof(ServiceRunState), (int)status.CurrentState)
                    ? (ServiceRunState)status.CurrentState
                    : ServiceRunState.Unknown;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public bool TryStop(string serviceName, TimeSpan timeout, out string? error)
    {
        error = null;
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            error = "打不开服务控制管理器（需要管理员权限）";
            return false;
        }

        try
        {
            var service = OpenServiceW(manager, serviceName, ServiceQueryStatus | ServiceStop);
            if (service == IntPtr.Zero)
            {
                error = $"打不开服务 {serviceName}：{Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                var status = new ServiceStatus();
                if (!QueryServiceStatus(service, ref status))
                {
                    error = "查询服务状态失败";
                    return false;
                }

                if (status.CurrentState == 1)
                {
                    return true;
                }

                if (!ControlService(service, ServiceControlStop, ref status))
                {
                    error = $"发送停止指令失败：{Marshal.GetLastWin32Error()}";
                    return false;
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (QueryState(serviceName) == ServiceRunState.Stopped)
            {
                return true;
            }

            Thread.Sleep(200);
        }

        error = $"等待停止超时（{timeout.TotalSeconds:0} 秒），它会在下次关机时彻底停下";
        return false;
    }

    public bool TryStart(string serviceName, out string? error)
    {
        error = null;
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            error = "打不开服务控制管理器（需要管理员权限）";
            return false;
        }

        try
        {
            var service = OpenServiceW(manager, serviceName, ServiceStart | ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                error = $"打不开服务 {serviceName}：{Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                if (!StartServiceW(service, 0, IntPtr.Zero))
                {
                    var code = Marshal.GetLastWin32Error();
                    // 1056 = ERROR_SERVICE_ALREADY_RUNNING
                    if (code != 1056)
                    {
                        error = $"启动失败：{code}";
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, ref ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, ref ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceW(IntPtr service, uint argumentCount, IntPtr arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
