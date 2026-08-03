using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenKh.Tests.ModsManager
{
    public class DownloadableModViewModelTests
    {
        [Fact]
        public async Task InstallCommand_PreventsReentrancy_AndRestoresCanExecute()
        {
            var installStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var installCalls = 0;
            var viewModel = CreateViewModel(installMod: async (_, _, _) =>
            {
                Interlocked.Increment(ref installCalls);
                installStarted.SetResult(true);
                await finishInstall.Task;
            });
            var command = Assert.IsType<AsyncCommand>(viewModel.InstallCommand);

            var firstExecution = command.ExecuteAsync();
            await installStarted.Task;

            Assert.True(viewModel.IsInstalling);
            Assert.False(command.CanExecute(null));
            await command.ExecuteAsync();
            Assert.Equal(1, installCalls);

            finishInstall.SetResult(true);
            await firstExecution;

            Assert.False(viewModel.IsInstalling);
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public async Task SuccessfulInstall_RunsProgressLifecycle_ReportsUpdates_AndRaisesEventThroughDispatcher()
        {
            var progressDialogs = new FakeProgressDialogService();
            var dispatcher = new FakeUiDispatcher();
            var viewModel = CreateViewModel(progressDialogs, dispatcher: dispatcher,
                installMod: (_, reportText, reportNumber) =>
                {
                    reportText("Downloading");
                    reportNumber(.5f);
                    return Task.CompletedTask;
                });
            DownloadableModViewModel installed = null;
            viewModel.ModInstalled += mod => installed = mod;

            await Assert.IsType<AsyncCommand>(viewModel.InstallCommand).ExecuteAsync();

            Assert.Equal(1, progressDialogs.RunCount);
            Assert.Equal("Initializing", progressDialogs.LastRequest.Message);
            Assert.Contains(progressDialogs.Updates, update => update.Message == "Downloading");
            Assert.Contains(progressDialogs.Updates, update => update.Value == .5 && update.IsIndeterminate == false);
            Assert.Same(viewModel, installed);
            Assert.Equal(1, dispatcher.InvokeCount);
            Assert.False(viewModel.IsInstalling);
        }

        [Fact]
        public async Task FailedInstall_ShowsError_RecoversState_AndDoesNotRaiseInstalledEvent()
        {
            var messages = new FakeMessageDialogService();
            var viewModel = CreateViewModel(messages: messages,
                installMod: (_, _, _) => throw new InvalidOperationException("network unavailable"));
            var installed = false;
            viewModel.ModInstalled += _ => installed = true;

            await Assert.IsType<AsyncCommand>(viewModel.InstallCommand).ExecuteAsync();

            Assert.False(installed);
            Assert.False(viewModel.IsInstalling);
            Assert.True(viewModel.InstallCommand.CanExecute(null));
            Assert.Single(messages.Requests);
            Assert.Equal(MessageDialogKind.Error, messages.Requests[0].Kind);
            Assert.Contains("network unavailable", messages.Requests[0].Message);
            Assert.Contains("Example Mod", messages.Requests[0].Message);
        }

        private static DownloadableModViewModel CreateViewModel(
            FakeProgressDialogService progressDialogs = null,
            FakeMessageDialogService messages = null,
            FakeUiDispatcher dispatcher = null,
            Func<string, Action<string>, Action<float>, Task> installMod = null) =>
            new(new DownloadableModModel { Repo = "owner/repo", Title = "Example Mod" },
                progressDialogs ?? new FakeProgressDialogService(),
                messages ?? new FakeMessageDialogService(),
                dispatcher ?? new FakeUiDispatcher(),
                installMod ?? ((_, _, _) => Task.CompletedTask));

        private sealed class FakeProgressDialogService : IProgressDialogService
        {
            public int RunCount { get; private set; }
            public ProgressDialogRequest LastRequest { get; private set; }
            public List<ProgressDialogUpdate> Updates { get; } = new();

            public async Task<ProgressDialogResult> RunAsync(ProgressDialogRequest request,
                Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> operation,
                CancellationToken cancellationToken = default)
            {
                RunCount++;
                LastRequest = request;
                await operation(new ImmediateProgress<ProgressDialogUpdate>(Updates.Add), cancellationToken);
                return new ProgressDialogResult(false);
            }
        }

        private sealed class FakeMessageDialogService : IMessageDialogService
        {
            public List<MessageDialogRequest> Requests { get; } = new();

            public Task<MessageDialogResult> ShowAsync(MessageDialogRequest request, CancellationToken cancellationToken = default)
            {
                Requests.Add(request);
                return Task.FromResult(MessageDialogResult.Ok);
            }
        }

        private sealed class FakeUiDispatcher : IUiDispatcher
        {
            public int InvokeCount { get; private set; }
            public bool CheckAccess() => false;
            public void Post(Action action) => action();
            public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
            {
                InvokeCount++;
                action();
                return Task.CompletedTask;
            }
            public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
            {
                InvokeCount++;
                return Task.FromResult(action());
            }
        }

        private sealed class ImmediateProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;
            public ImmediateProgress(Action<T> report) => _report = report;
            public void Report(T value) => _report(value);
        }
    }
}
