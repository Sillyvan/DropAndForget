using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DropAndForget.ViewModels;

/// <summary>
/// Wraps an asynchronous parameterized action in an <see cref="ICommand"/> and blocks reentry while running.
/// </summary>
/// <typeparam name="T">Parameter type.</typeparam>
public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _executeAsync;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onException;
    private bool _isExecuting;

    /// <summary>
    /// Creates a command.
    /// </summary>
    /// <param name="executeAsync">Action to run.</param>
    /// <param name="canExecute">Optional availability check.</param>
    /// <param name="onException">Optional exception handler.</param>
    public AsyncRelayCommand(Func<T?, Task> executeAsync, Func<T?, bool>? canExecute = null, Action<Exception>? onException = null)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);
        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _onException = onException;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke(ConvertParameter(parameter)) ?? true);
    }

    /// <inheritdoc/>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync(ConvertParameter(parameter));
        }
        catch (Exception ex)
        {
            _onException?.Invoke(ex);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
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
