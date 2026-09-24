using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Street_Rod_AC.ViewModels;

/// <summary>
/// The INotifyPropertyChanged plumbing every view model needs, written once: screens, dialogs and the small
/// item view models all derive from it instead of carrying their own copy.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Sets the field and raises PropertyChanged when the value actually changed; false when it did not</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
