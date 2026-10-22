using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StartupToggle.SelfTest;

/// <summary>
/// 在进程内跑一遍真实的启动流程，然后接管主题、构造窗口、完成布局并出图。
/// 这样既验证了 App.xaml 的资源装配，也验证了窗口 XAML 的绑定与模板。
/// </summary>
internal static class UiHarness
{
    private const double Width = 1060;
    private const double Height = 720;

    private static readonly (string Name, Color Expected)[] Themes =
    [
        ("Light", Color.FromRgb(0xF3, 0xF3, 0xF3)),
        ("Dark", Color.FromRgb(0x1F, 0x1F, 0x1F)),
    ];

    public static void Run(string outputDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunCore(outputDirectory);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException($"界面渲染失败：{failure.GetType().Name} {failure.Message}", failure);
        }
    }

    private static void RunCore(string outputDirectory)
    {
        var app = new StartupToggle.App.App();
        app.InitializeComponent();

        foreach (var key in new[]
                 {
                     "AppBackground", "SurfaceBackground", "TextPrimary", "TextSecondary", "BorderSoft",
                     "Accent", "Success", "SuccessSoft", "Danger", "DangerSoft", "WarnSoft",
                     "ToolbarButton", "PrimaryButton", "PillButton", "FieldBorder", "FieldBox",
                     "StatusPill", "StatusDot", "StatusLabel", "BadgeNeutral", "BadgeDanger",
                     "GridHeader", "GridRow", "GridCell", "BoolToVisibility", "InverseBoolToVisibility",
                 })
        {
            Program.Check($"资源键 {key} 存在", Application.Current.TryFindResource(key) is not null);
        }

        // 真实运行时由 Application.Run 触发 OnStartup；这里手动把排队的启动流程跑完，
        // 否则它会晚一步插进来，按系统主题改掉我们正在渲染的主题。
        Pump(DispatcherPriority.ApplicationIdle);

        Program.Check("启动流程创建了主窗口", Application.Current.MainWindow is not null);
        Program.Check("启动流程按系统设置选定主题",
            StartupToggle.App.ThemeManager.IsLight == StartupToggle.App.ThemeManager.DetectLight());

        Application.Current.MainWindow?.Hide();

        Directory.CreateDirectory(outputDirectory);

        foreach (var dark in new[] { false, true })
        {
            var expectedBackground = dark ? Themes[1].Expected : Themes[0].Expected;
            StartupToggle.App.ThemeManager.Apply(!dark);

            var window = new StartupToggle.App.MainWindow { ShowInTaskbar = false };
            window.LoadForPreview();

            if (window.Content is not FrameworkElement root)
            {
                Program.Fail("窗口内容为空");
                return;
            }

            root.Width = Width;
            root.Height = Height;
            root.Measure(new Size(Width, Height));
            root.Arrange(new Rect(0, 0, Width, Height));
            root.UpdateLayout();
            Pump(DispatcherPriority.Loaded);
            root.UpdateLayout();

            var themeName = dark ? "深色" : "浅色";

            Program.Check($"{themeName}主题下布局宽度为 {root.DesiredSize.Width:0}",
                Math.Abs(root.DesiredSize.Width - Width) < 1);

            var actualBackground = (Application.Current.Resources["AppBackground"] as SolidColorBrush)?.Color;
            Program.Check($"{themeName}主题资源颜色为 {actualBackground}", actualBackground == expectedBackground);

            var grid = FindDescendant<DataGrid>(root);
            Program.Check($"{themeName}主题下列表已绑定数据（{grid?.Items.Count} 项）", grid?.Items.Count > 0);
            Program.Check($"{themeName}主题下列表为 5 列", grid?.Columns.Count == 5);

            var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);

            var pixels = new byte[(int)Width * (int)Height * 4];
            bitmap.CopyPixels(pixels, (int)Width * 4, 0);
            var corner = Color.FromRgb(pixels[0], pixels[1], pixels[2]);
            Program.Check($"{themeName}主题截图左上角像素为 {corner}",
                Distance(corner, expectedBackground) < 6);

            var path = Path.Combine(outputDirectory, dark ? "preview-dark.png" : "preview-light.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            Program.Check($"{themeName}主题截图已生成（{new FileInfo(path).Length / 1024} KB）", File.Exists(path));
        }

        RenderServicesWindow(outputDirectory);
    }

    /// <summary>服务窗口单独渲染一张：它读的是真实服务列表（只读查询）。</summary>
    private static void RenderServicesWindow(string outputDirectory)
    {
        StartupToggle.App.ThemeManager.Apply(StartupToggle.App.ThemeManager.DetectLight());

        var window = new StartupToggle.App.ServicesWindow { ShowInTaskbar = false };
        window.LoadForPreview();

        if (window.Content is not FrameworkElement root)
        {
            Program.Fail("服务窗口内容为空");
            return;
        }

        root.Width = Width;
        root.Height = Height;
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));
        root.UpdateLayout();
        Pump(DispatcherPriority.Loaded);
        root.UpdateLayout();

        var grid = FindDescendant<DataGrid>(root);
        Program.Check($"服务列表已绑定数据（{grid?.Items.Count} 个用户态服务）", grid?.Items.Count > 0);
        Program.Check("服务列表为 5 列", grid?.Columns.Count == 5);

        var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var path = Path.Combine(outputDirectory, "preview-services.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        Program.Check($"服务窗口截图已生成（{new FileInfo(path).Length / 1024} KB）", File.Exists(path));
    }

    private static void Pump(DispatcherPriority priority) =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, priority);

    private static int Distance(Color left, Color right) =>
        Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
