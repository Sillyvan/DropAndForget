using System;
using System.Windows.Input;

namespace DropAndForget.ViewModels;

/// <summary>
/// Wraps a synchronous action in an <see cref="ICommand"/>.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Creates a command.
    /// </summary>
    /// <param name="execute">Action to run.</param>
    /// <param name="canExecute">Optional availability check.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        _execute();
    }

    /// <summary>
    /// Notifies the UI that availability changed.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
