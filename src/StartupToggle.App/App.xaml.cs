using System.Windows;

namespace StartupToggle.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"出现未处理的错误：\n\n{args.Exception.Message}",
                "开机启动项管理",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        ThemeManager.Apply(ThemeManager.DetectLight());

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        ThemeManager.Watch(window);
    }
}
