using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StartupToggle.Core;
using StartupToggle.Core.Services;

namespace StartupToggle.App;

/// <summary>服务（高级）窗口：独立视图，和启动项窗口共用同一个备份文件与恢复引擎。</summary>
public partial class ServicesWindow : Window
{
    private readonly ServiceManager _manager = new(WindowsRegistryGateway.Instance, BackupStore.Default);
    private readonly RestoreEngine _restore = new(WindowsRegistryGateway.Instance, BackupStore.Default);
    private readonly ObservableCollection<ServiceRow> _rows = [];
    private List<ServiceRow> _all = [];

    public ServicesWindow()
    {
        InitializeComponent();
        ServicesGrid.ItemsSource = _rows;
        Loaded += (_, _) => LoadForPreview();
    }

    /// <summary>供自测离屏渲染调用：不依赖 Loaded。</summary>
    public void LoadForPreview()
    {
        Reload("拖动滚动条或用搜索框找目标服务。");
    }

    private void Reload(string? status = null)
    {
        try
        {
            _all = _manager.Scan().Select(info => new ServiceRow(info)).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取服务列表失败：{ex.Message}", "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ApplyFilter();

        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var autoOnly = AutoOnly.IsChecked == true;
        var drivers = ShowDrivers.IsChecked == true;

        IEnumerable<ServiceRow> filtered = _all;
        if (!drivers)
        {
            filtered = filtered.Where(row => !row.IsDriver);
        }

        if (autoOnly)
        {
            filtered = filtered.Where(row => row.IsAuto);
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
        var visible = _rows.Count;
        var auto = _all.Count(row => row.IsAuto && !row.IsDriver);
        var disabled = _all.Count(row => row.IsDisabled && !row.IsDriver);
        var locked = _all.Count(row => row.IsLocked && !row.IsDriver);
        var running = _all.Count(row => row.IsRunning && !row.IsDriver);

        SubtitleText.Text = $"显示 {visible} 个 · 用户态服务里 自动 {auto} · 禁用 {disabled} · 正在运行 {running} · 受保护 {locked}";

        var backupCount = _manager.Backup.Count;
        FooterText.Text = $"备份记录 {backupCount} 条 · 驱动 {_all.Count(row => row.IsDriver)} 个（只读）";
        RestoreButton.IsEnabled = backupCount > 0;
    }

    // ================= 事件 =================

    private void OnToggleRow(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ServiceRow row })
        {
            return;
        }

        var enable = !row.Item.IsAuto;

        if (!enable)
        {
            var dependents = row.Item.Dependents;
            var extra = dependents.Count == 0
                ? string.Empty
                : $"\n\n注意：有 {dependents.Count} 个服务依赖它，禁用后这些服务可能起不来：\n" +
                  string.Join("\n", dependents.Take(8).Select(name => "· " + name)) +
                  (dependents.Count > 8 ? $"\n…… 共 {dependents.Count} 个" : string.Empty);

            var confirmed = MessageBox.Show(
                this,
                $"把“{row.Title}”（{row.Name}）设为禁用？\n\n" +
                "会同时尝试停止它正在运行的实例；之后可以用“恢复我改过的项”还原。" + extra,
                "确认禁用服务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

            if (!confirmed)
            {
                return;
            }
        }

        var result = _manager.SetEnabled(row.Item, enable);
        Reload(result.Message);

        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRestoreAll(object sender, RoutedEventArgs e)
    {
        var pending = _manager.Backup.OriginalPerEntry();
        if (pending.Count == 0)
        {
            MessageBox.Show(this, "没有需要恢复的记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preview = string.Join("\n", pending.Take(12).Select(record => $"· {record.Name}（{record.Kind}）"));
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

        var result = _restore.RestoreAll();
        Reload($"恢复完成：成功 {result.Restored}，失败 {result.Failed}。");

        if (result.Errors.Count > 0)
        {
            MessageBox.Show(this, "以下项未能恢复：\n\n" + string.Join("\n", result.Errors), "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload("已刷新。");

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
}
