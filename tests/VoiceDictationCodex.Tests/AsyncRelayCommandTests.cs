using VoiceDictationCodex.ViewModels;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class AsyncRelayCommandTests
{
    [Fact]
    public void Execute_RoutesAnExceptionToOnError_AndBecomesExecutableAgain()
    {
        var failure = new InvalidOperationException("boom");
        Exception? reported = null;
        var command = new AsyncRelayCommand(() => throw failure, onError: ex => reported = ex);

        command.Execute(null);

        Assert.Same(failure, reported);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenNoErrorHandlerIsGiven()
    {
        var command = new AsyncRelayCommand(() => Task.FromException(new InvalidOperationException("boom")));

        await command.ExecuteAsync();

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_IsNotReentrant_AndRaisesCanExecuteChangedAroundTheRun()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        var changes = 0;
        var command = new AsyncRelayCommand(() =>
        {
            runs++;
            return gate.Task;
        });
        command.CanExecuteChanged += (_, _) => changes++;

        var first = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        gate.SetResult();
        await first;

        Assert.Equal(1, runs);
        Assert.Equal(2, changes);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCanExecute()
    {
        var runs = 0;
        var command = new AsyncRelayCommand(
            () =>
            {
                runs++;
                return Task.CompletedTask;
            },
            () => false);

        await command.ExecuteAsync();

        Assert.Equal(0, runs);
    }
}
