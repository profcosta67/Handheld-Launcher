using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Plain INotifyPropertyChanged base, hand-rolled instead of pulling in
/// CommunityToolkit.Mvvm — one less NuGet package for a UI this size.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
