using CommunityToolkit.Mvvm.ComponentModel;

namespace NightInjection.UI.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    protected void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        HasError = isError;
    }
}
