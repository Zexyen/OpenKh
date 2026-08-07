using OpenKh.Tools.ModsManager.Avalonia.Infrastructure;
using OpenKh.Tools.ModsManager.Infrastructure;
using Xunit;

namespace OpenKh.Tests.ModsManager;

public class CommandPrimitiveTests
{
    [Fact]
    public void RelayCommand_PassesParameter_AndRaisesExplicitInvalidation()
    {
        object received = null;
        var canExecute = false;
        var changed = 0;
        var command = new RelayCommand(parameter => received = parameter, _ => canExecute);
        command.CanExecuteChanged += (_, _) => changed++;

        Assert.False(command.CanExecute("before"));
        canExecute = true;
        command.RaiseCanExecuteChanged();
        command.Execute("value");

        Assert.True(command.CanExecute("after"));
        Assert.Equal("value", received);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task AsyncCommand_SuppressesReentrancy_AndRestoresStateAfterFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var command = new AsyncCommand(async parameter =>
        {
            invocations++;
            Assert.Equal("parameter", parameter);
            entered.SetResult();
            await release.Task;
            throw new InvalidOperationException("failure");
        });

        var execution = command.ExecuteAsync("parameter");
        await entered.Task;
        Assert.False(command.CanExecute(null));

        await command.ExecuteAsync("ignored");
        Assert.Equal(1, invocations);

        release.SetResult();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        Assert.Equal("failure", exception.Message);
        Assert.True(command.CanExecute(null));
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public async Task AvaloniaAsyncCommand_TracksTask_HandlesFailure_AndInvalidatesSelection()
    {
        Task trackedTask = null;
        Exception handled = null;
        var selected = false;
        var changed = 0;
        var command = new AvaloniaAsyncCommand(
            _ => Task.FromException(new InvalidOperationException("frontend failure")),
            task => trackedTask = task,
            exception =>
            {
                handled = exception;
                return Task.CompletedTask;
            })
        {
            IsEnabled = false,
        };
        command.CanExecuteChanged += (_, _) => changed++;

        Assert.False(command.CanExecute(null));
        selected = true;
        command.IsEnabled = selected;
        await command.ExecuteAsync("selection");

        Assert.NotNull(trackedTask);
        Assert.Equal("frontend failure", handled?.Message);
        Assert.True(command.CanExecute(null));
        Assert.False(command.IsExecuting);
        Assert.True(changed >= 3); // selection, execution start, execution completion
    }
}
