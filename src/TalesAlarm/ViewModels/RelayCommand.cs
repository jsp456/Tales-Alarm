using System.Windows.Input;

namespace TalesAlarm.ViewModels;

public sealed class RelayCommand(
    Action execute,
    Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onException = null) : ICommand
{
    private bool isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting
    {
        get => isExecuting;
        private set
        {
            if (isExecuting == value)
            {
                return;
            }

            isExecuting = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) =>
        !IsExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        IsExecuting = true;
        try
        {
            await execute().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (onException is null)
            {
                throw;
            }

            onException(exception);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
