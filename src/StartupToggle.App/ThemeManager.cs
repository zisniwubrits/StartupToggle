using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using StartupToggle.Core;

namespace StartupToggle.App;

/// <summary>主题跟随系统：读 AppsUseLightTheme，并监听 WM_SETTINGCHANGE 实时切换。</summary>
public static class ThemeManager
{
    private const int WmSettingChange = 0x001A;

    public static bool IsLight { get; private set; } = true;

    public static bool DetectLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPaths.PersonalizeKey);
            return key?.GetValue(RegistryPaths.AppLightThemeValue) is not int value || value != 0;
        }
        catch
        {
            // 读不到就按浅色处理，与系统默认一致。
            return true;
        }
    }

    public static void Apply(bool light)
    {
        var assembly = typeof(ThemeManager).Assembly.GetName().Name;
        var source = new Uri(
            $"pack://application:,,,/{assembly};component/Themes/{(light ? "Light" : "Dark")}.xaml",
            UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = source };

        if (Application.Current.Resources.MergedDictionaries.Count > 0)
        {
            Application.Current.Resources.MergedDictionaries[0] = dictionary;
        }
        else
        {
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
        }

        IsLight = light;
    }

    public static void Watch(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook(window);
            return;
        }

        window.SourceInitialized += (_, _) => Hook(window);
    }

    private static void Hook(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
        {
            return;
        }

        source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message != WmSettingChange)
            {
                return IntPtr.Zero;
            }

            var light = DetectLight();
            if (light != IsLight)
            {
                Apply(light);
            }

            return IntPtr.Zero;
        });
    }
}
