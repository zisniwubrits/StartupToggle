using System.Windows;
using Microsoft.Win32;
using StartupToggle.Core;

namespace StartupToggle.App;

public partial class AddItemWindow : Window
{
    public AddItemWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string EntryName => NameBox.Text.Trim();

    public string Command => CommandBox.Text.Trim();

    public StartupLocation Target =>
        TargetBox.SelectedIndex == 1 ? StartupLocation.MachineRun : StartupLocation.UserRun;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要随系统启动的程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        CommandBox.Text = CommandLineParser.Compose(dialog.FileName, null);

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var error = Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private string? Validate()
    {
        if (EntryName.Length == 0)
        {
            return "请填写名称。";
        }

        if (Command.Length == 0)
        {
            return "请填写启动命令，或点“浏览…”选一个程序。";
        }

        var parsed = CommandLineParser.Parse(Command);
        if (parsed.ExecutablePath is not null && !parsed.Exists)
        {
            return $"命令指向的文件不存在：{parsed.ExecutablePath}";
        }

        return null;
    }
}
