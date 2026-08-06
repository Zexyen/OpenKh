using NSubstitute;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenKh.Tests.ModsManager;

[CollectionDefinition("MainViewModel", DisableParallelization = true)]
public sealed class MainViewModelCollection { }

[Collection("MainViewModel")]
public class MainViewModelShellTests
{
    [Fact]
    public void VisibilityPropertiesUseBooleanSemantics()
    {
        using var fixture = new Fixture();
        var viewModel = fixture.ViewModel;

        viewModel.PC = false;
        viewModel.PCSX2 = false;
        Assert.True(viewModel.ModLoader);
        Assert.True(viewModel.notPC);
        Assert.False(viewModel.isPC);
        Assert.False(viewModel.GameSelectVisible);

        viewModel.PC = true;
        viewModel.PanaceaInstalled = false;
        Assert.True(viewModel.PatchVisible);
        Assert.False(viewModel.ModLoader);
        Assert.True(viewModel.isPC);

        viewModel.PanaceaInstalled = true;
        Assert.False(viewModel.PatchVisible);
        Assert.True(viewModel.ModLoader);
        Assert.True(viewModel.PanaceaSettings);
    }

    [Fact]
    public async Task ShellCommandsRouteThroughInjectedAdapters()
    {
        using var fixture = new Fixture();
        fixture.Navigation.Requests.Clear();

        fixture.ViewModel.YamlGeneratorCommand.Execute(null);
        fixture.ViewModel.OpenPresetMenuCommand.Execute(null);
        fixture.ViewModel.OpenLinkCommand.Execute("https://openkh.dev/");
        fixture.ViewModel.ExitCommand.Execute(null);
        await Task.Delay(20);

        Assert.Contains(fixture.Navigation.Requests, x => x.Destination == NavigationDestination.YamlGenerator);
        Assert.Contains(fixture.Navigation.Requests, x => x.Destination == NavigationDestination.Presets && x.IsModal);
        Assert.Equal(new Uri("https://openkh.dev/"), fixture.Browser.LastUri);
        Assert.True(fixture.Lifetime.WasShutdown);
    }

    [Fact]
    public void DisposeIsIdempotentAndClosesDebugSession()
    {
        var fixture = new Fixture();
        fixture.ViewModel.BuildCommand.Execute(null);

        fixture.ViewModel.Dispose();
        fixture.ViewModel.Dispose();

        Assert.True(fixture.DebugSession.CloseCount <= 1);
        Assert.True(fixture.DebugSession.DisposeCount <= 1);
    }

    [Fact]
    public async Task BuildCommandRejectsReentrancyAndRestoresState()
    {
        var workflows = new BlockingWorkflows();
        using var fixture = new Fixture(workflows: workflows);
        var command = Assert.IsType<AsyncCommand>(fixture.ViewModel.BuildCommand);

        var first = command.ExecuteAsync();
        await workflows.Started.Task;
        Assert.True(fixture.ViewModel.IsBusy);
        Assert.False(command.CanExecute(null));

        await command.ExecuteAsync();
        Assert.Equal(1, workflows.BuildCalls);
        workflows.Release.SetResult();
        await first;

        Assert.False(fixture.ViewModel.IsBusy);
        Assert.False(fixture.ViewModel.IsBuilding);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task InitializeIsIdempotentAndSerializesUpdateFetch()
    {
        var workflows = new BlockingWorkflows(blockFetch: true);
        using var fixture = new Fixture(workflows: workflows);

        var first = fixture.ViewModel.InitializeAsync();
        var second = fixture.ViewModel.InitializeAsync();
        Assert.Same(first, second);
        await workflows.FetchStarted.Task;
        workflows.FetchRelease.SetResult();
        await first;

        Assert.Equal(1, workflows.FetchCalls);
    }

    [Fact]
    public async Task CloseAsyncStopsAndDisposesSessionWithoutDeadlock()
    {
        using var fixture = new Fixture();
        await Assert.IsType<AsyncCommand>(fixture.ViewModel.RunCommand).ExecuteAsync();
        Assert.True(fixture.ViewModel.IsRunning);

        await fixture.ViewModel.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, fixture.GameWorkflow.Session.StopCount);
        Assert.Equal(1, fixture.GameWorkflow.Session.DisposeCount);
        Assert.False(fixture.ViewModel.IsRunning);
    }

    [Fact]
    public async Task SessionCallbacksAreIgnoredAfterClose()
    {
        using var fixture = new Fixture();
        await Assert.IsType<AsyncCommand>(fixture.ViewModel.RunCommand).ExecuteAsync();
        await fixture.ViewModel.CloseAsync();

        fixture.GameWorkflow.Session.RaiseExited();

        Assert.False(fixture.ViewModel.IsRunning);
        Assert.Equal(1, fixture.GameWorkflow.Session.DisposeCount);
    }

    private sealed class Fixture : IDisposable
    {
        public RecordingNavigation Navigation { get; } = new();
        public RecordingBrowser Browser { get; } = new();
        public RecordingLifetime Lifetime { get; } = new();
        public RecordingDebugSession DebugSession { get; } = new();
        public FakeGameWorkflow GameWorkflow { get; } = new();
        public MainViewModel ViewModel { get; }

        public Fixture(IModWorkflowService? workflows = null)
        {
            var progress = Substitute.For<IProgressDialogService>();
            progress.RunAsync(Arg.Any<ProgressDialogRequest>(),
                    Arg.Any<Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task>>(), Arg.Any<CancellationToken>())
                .Returns(call => RunProgress(call.ArgAt<Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task>>(1)));
            var messages = Substitute.For<IMessageDialogService>();
            messages.ShowAsync(Arg.Any<MessageDialogRequest>(), Arg.Any<CancellationToken>())
                .Returns(MessageDialogResult.No);
            var dispatcher = new ImmediateDispatcher();
            var processes = Substitute.For<IShellProcessLauncher>();
            var debug = Substitute.For<IDebugLogService>();
            debug.Start(Arg.Any<DebugLogRequest>()).Returns(DebugSession);

            ViewModel = new MainViewModel(new MainViewModelDependencies(progress, messages, dispatcher,
                Navigation, Browser, Lifetime, processes, debug,
                (_, _) => null!, workflows ?? new BlockingWorkflows(), Substitute.For<IPresetService>(),
                Substitute.For<IApplicationUpdateChecker>(), Substitute.For<IApplicationUpdateExecutor>(),
                GameWorkflow, Substitute.For<IGamePatchService>()));
        }

        private static async Task<ProgressDialogResult> RunProgress(
            Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> operation)
        {
            await operation(new Progress<ProgressDialogUpdate>(), CancellationToken.None);
            return new ProgressDialogResult(false);
        }

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class FakeGameWorkflow : IGameWorkflowService
    {
        public FakeGameSession Session { get; } = new();
        public GameAvailability GetAvailability() => new(false, false, false, false, false);
        public string GetPreferredGameId(string gameId) => gameId;
        public Task<GameStartResult> StartAsync(GameStartRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GameStartResult(Session));
        public void UpdatePanaceaSettings(PanaceaSettings settings) { }
        public Task<PanaceaUpdateResult> ApplyPendingPanaceaUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PanaceaUpdateResult(false, false));
    }

    private sealed class FakeGameSession : IGameSession
    {
        public bool IsRunning { get; private set; } = true;
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public event EventHandler<GameOutputEventArgs>? OutputReceived;
        public event EventHandler? Exited;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
        public void RaiseExited() => Exited?.Invoke(this, EventArgs.Empty);
        public void RaiseOutput(string text) => OutputReceived?.Invoke(this, new GameOutputEventArgs(text));
    }

    private sealed class BlockingWorkflows : IModWorkflowService
    {
        private readonly bool _blockFetch;
        public BlockingWorkflows(bool blockFetch = false) => _blockFetch = blockFetch;
        public int BuildCalls { get; private set; }
        public int FetchCalls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FetchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FetchRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> BuildAsync(bool fastMode, CancellationToken cancellationToken)
        {
            BuildCalls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return true;
        }
        public async IAsyncEnumerable<ModUpdateModel> FetchUpdatesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            FetchCalls++;
            FetchStarted.TrySetResult();
            if (_blockFetch)
                await FetchRelease.Task.WaitAsync(cancellationToken);
            yield break;
        }
        public IReadOnlyList<ModModel> GetMods(IEnumerable<string>? names = null) => Array.Empty<ModModel>();
        public Task<ModInstallResult> InstallAsync(ModInstallRequest request, IProgress<ProgressDialogUpdate> progress,
            CancellationToken cancellationToken) => Task.FromResult(new ModInstallResult(false, request.Name));
        public Task RemoveAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(string source, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => Task.FromResult(action());
    }

    private sealed class RecordingNavigation : INavigationService
    {
        public List<NavigationRequest> Requests { get; } = new();
        public Task<NavigationResult> ShowAsync(NavigationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            NavigationResult result = request.Destination switch
            {
                NavigationDestination.InstallSelection => new InstallSelectionResult(false),
                NavigationDestination.SetupWizard => new SetupWizardResult(false, false),
                _ => new EmptyNavigationResult()
            };
            return Task.FromResult(result);
        }
        public Task<bool> CloseAsync(NavigationDestination destination, NavigationResult? result = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingBrowser : IBrowserService
    {
        public Uri? LastUri { get; private set; }
        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) { LastUri = uri; return Task.CompletedTask; }
    }

    private sealed class RecordingLifetime : IApplicationLifetime
    {
        public bool WasShutdown { get; private set; }
        public void Shutdown(int exitCode = 0) => WasShutdown = true;
    }

    private sealed class RecordingDebugSession : IDebugLogSession
    {
        public bool IsOpen => true;
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void Write(DebugLogEntry entry) { }
        public Task ShowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) { CloseCount++; return Task.CompletedTask; }
        public void Dispose() => DisposeCount++;
    }
}
