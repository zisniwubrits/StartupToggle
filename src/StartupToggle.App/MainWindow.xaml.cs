using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StartupToggle.Core;

namespace StartupToggle.App;

public partial class MainWindow : Window
{
    private readonly StartupManager _manager = StartupManager.CreateDefault();
    private readonly ObservableCollection<StartupRow> _rows = [];
    private List<StartupRow> _all = [];

    public MainWindow()
    {
        InitializeComponent();
        ItemsGrid.ItemsSource = _rows;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowAccountWarning();
        Reload("已就绪。");
    }

    /// <summary>离屏渲染预览用：不走 Loaded，手动加载数据。</summary>
    public void LoadForPreview()
    {
        ShowAccountWarning();
        Reload("已就绪。");
        ItemsGrid.UpdateLayout();
    }

    private void ShowAccountWarning()
    {
        if (!AccountContext.IsMismatch)
        {
            MismatchBanner.Visibility = Visibility.Collapsed;
            return;
        }

        MismatchBanner.Visibility = Visibility.Visible;
        MismatchText.Text =
            $"当前进程以 “{AccountContext.CurrentUser}” 运行，而桌面登录账户是 “{AccountContext.InteractiveUser}”。" +
            "“当前用户”的启动项会写进前者；你自己账户的项以“只读”形式列出，只做展示。";
    }

    private void Reload(string? status = null)
    {
        var selected = _all.Where(row => row.IsSelected)
            .Select(row => (row.Name, row.Location))
            .ToHashSet();

        try
        {
            var items = _manager.Scan();
            _all = items.Select(item => new StartupRow(item)).ToList();
        }
        catch (Exception ex)
        {
            ShowError($"读取启动项失败：{ex.Message}");
            return;
        }

        foreach (var row in _all.Where(row => selected.Contains((row.Name, row.Location))))
        {
            row.IsSelected = true;
        }

        ApplyFilter();

        if (status is not null)
        {
            SetStatus(status);
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var enabledOnly = EnabledOnly.IsChecked == true;

        IEnumerable<StartupRow> filtered = _all;
        if (enabledOnly)
        {
            filtered = filtered.Where(row => row.Enabled);
        }

        if (query.Length > 0)
        {
            filtered = filtered.Where(row => row.SearchText.Contains(query, StringComparison.Ordinal));
        }

        _rows.Clear();
        foreach (var row in filtered)
        {
            _rows.Add(row);
        }

        UpdateCounters();
    }

    private void UpdateCounters()
    {
        var total = _all.Count;
        var enabled = _all.Count(row => row.Enabled);
        var invalid = _all.Count(row => row.IsInvalid);
        var locked = _all.Count(row => row.IsLocked);
        var readOnly = _all.Count(row => row.IsReadOnly);

        SubtitleText.Text = $"{total} 项 · 启用 {enabled} · 禁用 {total - enabled} · 失效 {invalid} · 受保护 {locked}" +
                            (readOnly > 0 ? $" · 只读 {readOnly}" : string.Empty);

        var backupCount = _manager.Backup.Count;
        FooterText.Text = $"备份记录 {backupCount} 条 · {AccountContext.CurrentUser}" +
                          (AccountContext.IsElevated ? "（管理员）" : "（未提权）");
        RestoreButton.IsEnabled = backupCount > 0;
        RestoreButton.ToolTip = backupCount > 0
            ? $"把本软件改过的 {backupCount} 条记录恢复到修改前"
            : "还没有任何改动记录";
    }

    private void RefreshRow(StartupRow row)
    {
        try
        {
            var refreshed = _manager.Scan().FirstOrDefault(item =>
                item.Location == row.Item.Location &&
                string.Equals(item.Name, row.Item.Name, StringComparison.OrdinalIgnoreCase));

            if (refreshed is not null)
            {
                row.Update(refreshed);
            }
        }
        catch
        {
            // 刷新失败不影响已经成功的写入，下次整体刷新时会纠正。
        }
    }

    // ================= 事件 =================

    private void OnToggleRow(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StartupRow row })
        {
            return;
        }

        var target = !row.Item.Enabled;
        var result = _manager.SetEnabled(row.Item, target);
        if (!result.Success)
        {
            ShowWarning(result.Message);
            return;
        }

        RefreshRow(row);
        UpdateCounters();
        SetStatus(result.Message);
    }

    private void OnBulkEnable(object sender, RoutedEventArgs e) => RunBulk(enabled: true);

    private void OnBulkDisable(object sender, RoutedEventArgs e) => RunBulk(enabled: false);

    private void RunBulk(bool enabled)
    {
        var targets = _all.Where(row => row.IsSelected && row.CanToggle).ToList();
        if (targets.Count == 0)
        {
            ShowWarning("先在左侧勾选要处理的项；受保护和只读的项不能勾选。");
            return;
        }

        var preview = string.Join("\n", targets.Take(12).Select(row => $"· {row.Name}（{row.Location}）"));
        if (targets.Count > 12)
        {
            preview += $"\n…… 共 {targets.Count} 项";
        }

        var confirmed = MessageBox.Show(
            this,
            $"即将{(enabled ? "启用" : "禁用")} {targets.Count} 个启动项：\n\n{preview}\n\n" +
            "只写 StartupApproved 状态位，Run 值与快捷方式都不会被改动，之后可用“恢复我改过的项”还原。",
            "确认批量操作",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        var result = _manager.SetEnabledMany(targets.Select(row => row.Item).ToList(), enabled);
        var failures = result.Results.Where(pair => !pair.Result.Success).ToList();

        Reload();
        _all.ForEach(row => row.IsSelected = false);
        ApplyFilter();

        SetStatus($"批量{(enabled ? "启用" : "禁用")}完成：成功 {result.Succeeded}，失败 {result.Failed}。");
        if (failures.Count > 0)
        {
            ShowWarning("以下项未能修改：\n\n" +
                        string.Join("\n", failures.Select(pair => $"· {pair.Item.Name}：{pair.Result.Message}")));
        }
    }

    private void OnCleanInvalid(object sender, RoutedEventArgs e)
    {
        var targets = _all.Where(row => row.IsInvalid && row.CanToggle).ToList();
        if (targets.Count == 0)
        {
            ShowInfo("没有发现失效的启动项。");
            return;
        }

        var preview = string.Join("\n", targets.Take(12).Select(row => $"· {row.Name}（{row.Location}）"));
        if (targets.Count > 12)
        {
            preview += $"\n…… 共 {targets.Count} 项";
        }

        var confirmed = MessageBox.Show(
            this,
            $"以下 {targets.Count} 个启动项指向的文件已经不存在，清理会把启动项本身删掉（不是禁用）：\n\n{preview}\n\n" +
            "删除前会把命令行记进备份文件，但“恢复我改过的项”只恢复启用状态，不会重建被删的启动项。要继续吗？",
            "确认清理失效项",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        var removed = 0;
        var errors = new List<string>();
        foreach (var row in targets)
        {
            var result = _manager.Remove(row.Item);
            if (result.Success)
            {
                removed++;
            }
            else
            {
                errors.Add($"· {row.Name}：{result.Message}");
            }
        }

        Reload();
        SetStatus($"清理完成：删除 {removed} 项，失败 {errors.Count} 项。");
        if (errors.Count > 0)
        {
            ShowWarning("以下项未能清理：\n\n" + string.Join("\n", errors));
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dialog = new AddItemWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = _manager.Add(dialog.EntryName, dialog.Command, dialog.Target);
        if (!result.Success)
        {
            ShowWarning(result.Message);
            return;
        }

        Reload(result.Message);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出启动项清单",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"startup-items-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            JsonExporter.WriteFile(_all.Select(row => row.Item), dialog.FileName);
            SetStatus($"已导出 {_all.Count} 项到 {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowError($"导出失败：{ex.Message}");
        }
    }

    private void OnRestoreAll(object sender, RoutedEventArgs e)
    {
        var pending = _manager.Backup.OriginalPerEntry();
        if (pending.Count == 0)
        {
            ShowInfo("没有需要恢复的记录。");
            return;
        }

        var preview = string.Join("\n", pending.Take(12).Select(record => $"· {record.Name}（{record.Location}）"));
        if (pending.Count > 12)
        {
            preview += $"\n…… 共 {pending.Count} 项";
        }

        var confirmed = MessageBox.Show(
            this,
            $"把以下 {pending.Count} 项恢复到本软件修改之前的状态：\n\n{preview}\n\n继续吗？",
            "确认恢复",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        var result = _manager.RestoreAll();
        Reload();

        SetStatus($"恢复完成：成功 {result.Restored}，失败 {result.Failed}。");
        if (result.Errors.Count > 0)
        {
            ShowWarning("以下项未能恢复：\n\n" + string.Join("\n", result.Errors));
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload("已刷新。");

    private void OnOpenServices(object sender, RoutedEventArgs e)
    {
        var window = new ServicesWindow { Owner = this };
        window.ShowDialog();

        // 服务窗口里也可能点“恢复”，把启动项一起还原了，所以回来必须重扫。
        Reload("已从服务视图返回。");
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    // ================= 提示 =================

    private void SetStatus(string message) => StatusText.Text = message;

    private void ShowInfo(string message) =>
        MessageBox.Show(this, message, "开机启动项管理", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
}
