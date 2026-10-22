using System.ComponentModel;
using System.Runtime.CompilerServices;
using StartupToggle.Core.Services;

namespace StartupToggle.App;

/// <summary>服务列表的一行。</summary>
public sealed class ServiceRow : INotifyPropertyChanged
{
    private ServiceInfo _item;

    public ServiceRow(ServiceInfo item)
    {
        _item = item;
        SearchText = $"{item.Name} {item.DisplayNameOrFallback} {item.Description}".ToLowerInvariant();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ServiceInfo Item => _item;

    public string SearchText { get; private set; }

    public string Title => _item.DisplayNameOrFallback;

    public string Name => _item.Name;

    public string StartTypeText => _item.DisplayStartType;

    public string RunStateText => _item.DisplayRunState;

    public bool IsAuto => _item.IsAuto;

    public bool IsDisabled => _item.StartType == ServiceStartType.Disabled;

    public bool IsRunning => _item.RunState == ServiceRunState.Running;

    public bool IsLocked => _item.IsLocked;

    public bool IsDriver => _item.IsDriver;

    public string? LockReason => _item.LockReason;

    public bool CanToggle => _item.CanToggle;

    public string ActionText => _item.IsAuto ? "禁用" : "启用";

    public string DependentsText => _item.Dependents.Count == 0 ? "—" : $"{_item.Dependents.Count} 个";

    public string DependentsToolTip => _item.Dependents.Count == 0
        ? "没有服务依赖它"
        : "依赖它的服务：\n" + string.Join("\n", _item.Dependents);

    public string DescriptionText =>
        string.IsNullOrWhiteSpace(_item.Description) ? _item.Name : _item.Description!;

    public void Update(ServiceInfo item)
    {
        _item = item;
        SearchText = $"{item.Name} {item.DisplayNameOrFallback} {item.Description}".ToLowerInvariant();
        foreach (var name in new[]
                 {
                     nameof(Title), nameof(Name), nameof(StartTypeText), nameof(RunStateText),
                     nameof(IsAuto), nameof(IsDisabled), nameof(IsRunning), nameof(IsLocked),
                     nameof(IsDriver), nameof(LockReason), nameof(CanToggle), nameof(ActionText),
                     nameof(DependentsText), nameof(DependentsToolTip), nameof(DescriptionText),
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
