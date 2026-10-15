using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from CommunityToolkit.Mvvm so the sample stays dependency-free and
/// every line is visible. The gallery only needs <c>SetProperty</c> and change notifications.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for one property.</summary>
    /// <param name="propertyName">Property name; filled in by the compiler.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns a field and notifies when the value actually changed.</summary>
    /// <typeparam name="T">Field type.</typeparam>
    /// <param name="field">Backing field.</param>
    /// <param name="value">New value.</param>
    /// <param name="propertyName">Property name; filled in by the compiler.</param>
    /// <returns>True when the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Command that runs an async handler, ignores re-entrant clicks and re-enables itself afterwards.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Creates a command around an async handler.</summary>
    /// <param name="execute">Handler.</param>
    /// <param name="canExecute">Optional extra guard.</param>
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>Creates a command around a synchronous handler.</summary>
    /// <param name="execute">Handler.</param>
    /// <param name="canExecute">Optional extra guard.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(() =>
        {
            ArgumentNullException.ThrowIfNull(execute);
            execute();
            return Task.CompletedTask;
        }, canExecute)
    {
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        _ = RunAsync();
    }

    /// <summary>Re-queries every button bound to this command.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private async Task RunAsync()
    {
        try
        {
            // No ConfigureAwait(false): the handler updates bound properties and must stay on the
            // UI thread. Failures are already reported by the handler itself.
            await _execute();
        }
        catch (Exception)
        {
            // Deliberately swallowed: every gallery operation renders its own error text.
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }
}
