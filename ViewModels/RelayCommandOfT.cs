using System;
using System.Windows.Input;

namespace DropAndForget.ViewModels;

/// <summary>
/// Wraps a synchronous parameterized action in an <see cref="ICommand"/>.
/// </summary>
/// <typeparam name="T">Parameter type.</typeparam>
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    /// <summary>
    /// Creates a command.
    /// </summary>
    /// <param name="execute">Action to run.</param>
    /// <param name="canExecute">Optional availability check.</param>
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
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
        return _canExecute?.Invoke(ConvertParameter(parameter)) ?? true;
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        _execute(ConvertParameter(parameter));
    }

    /// <summary>
    /// Notifies the UI that availability changed.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T? ConvertParameter(object? parameter)
    {
        if (parameter is null)
        {
            return default;
        }

        return parameter is T value ? value : default;
    }
}
