using System.ComponentModel;
using System.Runtime.CompilerServices;
using StartupToggle.Core;

namespace StartupToggle.App;

/// <summary>列表里的一行，包住不可变的 StartupItem，负责通知界面刷新。</summary>
public sealed class StartupRow : INotifyPropertyChanged
{
    private StartupItem _item;
    private bool _isSelected;

    public StartupRow(StartupItem item)
    {
        _item = item;
        SearchText = $"{item.Name} {item.Command} {item.DisplayLocation}".ToLowerInvariant();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StartupItem Item => _item;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            Notify();
        }
    }

    public string Name => _item.Name;

    public string Location => _item.DisplayLocation;

    public string Command => _item.DisplayCommand;

    public string FullCommand => _item.Command;

    public bool Enabled => _item.Enabled;

    public bool IsInvalid => _item.IsInvalid;

    public bool IsLocked => _item.IsLocked;

    public string? LockReason => _item.LockReason;

    public bool IsReadOnly => _item.IsReadOnly;

    public bool CanToggle => !_item.IsLocked && !_item.IsReadOnly;

    public string ActionText => _item.Enabled ? "禁用" : "启用";

    public string StatusText => _item.Enabled ? "已启用" : "已禁用";

    public string StatusToolTip => _item.IsInvalid
        ? "启动命令指向的文件已不存在"
        : _item.Enabled ? "登录 Windows 时会随系统启动" : "登录 Windows 时不会启动";

    public string SearchText { get; private set; } = string.Empty;

    public void Update(StartupItem item)
    {
        _item = item;
        SearchText = $"{item.Name} {item.Command} {item.DisplayLocation}".ToLowerInvariant();
        Notify(string.Empty);
    }

    private void Notify([CallerMemberName] string? propertyName = null)
    {
        if (propertyName is null or "")
        {
            foreach (var name in new[]
                     {
                         nameof(Name), nameof(Location), nameof(Command), nameof(FullCommand),
                         nameof(Enabled), nameof(IsInvalid), nameof(IsLocked), nameof(LockReason),
                         nameof(IsReadOnly), nameof(CanToggle), nameof(ActionText), nameof(StatusText),
                         nameof(StatusToolTip), nameof(IsSelected),
                     })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
