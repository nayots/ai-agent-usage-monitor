using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base. Hand-rolled rather than taken from a
/// package: this is the whole of what the application needs from an MVVM framework, and
/// dependencies have to be justified by clear product value (PRD §22).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
