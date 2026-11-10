using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace VoiceDictationCodex.ViewModels;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _onError = onError;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    /// <summary>ICommand entry point. It is async void, so <see cref="ExecuteAsync"/> never lets an exception escape.</summary>
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _executeAsync();
        }
        catch (Exception ex)
        {
            // An exception leaving an async void method would end the process.
            if (_onError is null)
            {
                Trace.TraceError("Command failed: {0}", ex);
            }
            else
            {
                _onError(ex);
            }
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
