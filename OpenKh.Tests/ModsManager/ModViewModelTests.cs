using OpenKh.Patcher;
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
    public class ModViewModelTests
    {
        [Fact]
        public void VisibilityState_IsFrameworkNeutralAndReflectsModel()
        {
            var hosted = CreateModel("owner/repo", isCollection: false);
            var viewModel = CreateViewModel(hosted);

            Assert.True(viewModel.SourceVisibility);
            Assert.False(viewModel.LocalVisibility);
            Assert.False(viewModel.CollectionSettingsVisibility);
            Assert.False(viewModel.PreviewImageVisibility);
            Assert.True(viewModel.IsModUnselectedMessageVisible);

            viewModel.UpdateCount = 2;
            Assert.True(viewModel.UpdateVisibility);
        }

        [Fact]
        public void Enabled_NotifiesCollectionOwnerOnlyWhenValueChanges()
        {
            var changes = new FakeChangeState();
            var viewModel = CreateViewModel(CreateModel("local", false), changes);

            viewModel.Enabled = true;
            viewModel.Enabled = true;

            Assert.Equal(1, changes.Count);
        }

        [Fact]
        public async Task UpdateCommand_PreventsReentrancyAndRestoresState()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var viewModel = CreateViewModel(CreateModel("owner/repo", false), update: async (_, _, _) =>
            {
                calls++;
                started.TrySetResult(true);
                await finish.Task;
            });
            var command = Assert.IsType<AsyncCommand>(viewModel.UpdateCommand);

            var first = command.ExecuteAsync();
            await started.Task;
            Assert.True(viewModel.IsUpdating);
            Assert.False(command.CanExecute(null));
            await command.ExecuteAsync();
            Assert.Equal(1, calls);

            finish.SetResult(true);
            await first;
            Assert.False(viewModel.IsUpdating);
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public async Task FailedUpdate_ShowsNeutralErrorAndRestoresCommand()
        {
            var messages = new FakeMessages();
            var viewModel = CreateViewModel(CreateModel("owner/repo", false), messages: messages,
                update: (_, _, _) => throw new InvalidOperationException("offline"));

            await Assert.IsType<AsyncCommand>(viewModel.UpdateCommand).ExecuteAsync();

            Assert.False(viewModel.IsUpdating);
            Assert.True(viewModel.UpdateCommand.CanExecute(null));
            Assert.Single(messages.Requests);
            Assert.Equal(MessageDialogKind.Error, messages.Requests[0].Kind);
            Assert.Contains("offline", messages.Requests[0].Message);
        }

        [Fact]
        public async Task CollectionSettings_UsesTypedModalNavigation()
        {
            var navigation = new FakeNavigation();
            var viewModel = CreateViewModel(CreateModel("owner/collection", true), navigation: navigation,
                collectionMods: _ => Array.Empty<CollectionModModel>());

            await Assert.IsType<AsyncCommand>(viewModel.CollectionSettingsCommand).ExecuteAsync();

            Assert.Equal(NavigationDestination.CollectionSettings, navigation.Request.Destination);
            Assert.True(navigation.Request.IsModal);
            Assert.Same(viewModel, Assert.IsType<CollectionSettingsParameter>(navigation.Request.Parameter).Context);
        }

        [Fact]
        public async Task CollectionSettingsCommand_PreventsReentrancyAndRestoresState()
        {
            var navigation = new FakeNavigation { WaitForClose = true };
            var viewModel = CreateViewModel(CreateModel("owner/collection", true), navigation: navigation,
                collectionMods: _ => Array.Empty<CollectionModModel>());
            var command = Assert.IsType<AsyncCommand>(viewModel.CollectionSettingsCommand);
            var notifications = new List<string>();
            viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

            var first = command.ExecuteAsync();
            await navigation.Started.Task;
            Assert.True(viewModel.IsOpeningCollectionSettings);
            Assert.False(command.CanExecute(null));
            await command.ExecuteAsync();
            Assert.Equal(1, navigation.CallCount);

            navigation.Finish.SetResult(true);
            await first;
            Assert.False(viewModel.IsOpeningCollectionSettings);
            Assert.True(command.CanExecute(null));
            Assert.Equal(2, notifications.FindAll(x => x == nameof(ModViewModel.IsOpeningCollectionSettings)).Count);
        }

        private static ModModel CreateModel(string name, bool isCollection) => new()
        {
            Name = name,
            Metadata = new Metadata { IsCollection = isCollection, Assets = new List<AssetFile>() },
            CollectionOptionalEnabledAssets = new Dictionary<string, bool>()
        };

        private static ModViewModel CreateViewModel(ModModel model, FakeChangeState changes = null,
            FakeMessages messages = null, FakeNavigation navigation = null,
            Func<string, Action<string>, Action<float>, Task> update = null,
            Func<ModModel, IEnumerable<CollectionModModel>> collectionMods = null) =>
            new(model, changes ?? new FakeChangeState(), new FakeProgress(), messages ?? new FakeMessages(),
                new ImmediateDispatcher(), navigation ?? new FakeNavigation(), new NullImages(),
                update ?? ((_, _, _) => Task.CompletedTask), collectionMods ?? (_ => Array.Empty<CollectionModModel>()));

        private sealed class FakeChangeState : IChangeModEnableState
        { public int Count { get; private set; } public void ModEnableStateChanged() => Count++; }

        private sealed class FakeProgress : IProgressDialogService
        {
            public async Task<ProgressDialogResult> RunAsync(ProgressDialogRequest request,
                Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> operation,
                CancellationToken cancellationToken = default)
            {
                await operation(new Progress<ProgressDialogUpdate>(), cancellationToken);
                return new ProgressDialogResult(false);
            }
        }

        private sealed class FakeMessages : IMessageDialogService
        {
            public List<MessageDialogRequest> Requests { get; } = new();
            public Task<MessageDialogResult> ShowAsync(MessageDialogRequest request, CancellationToken cancellationToken = default)
            { Requests.Add(request); return Task.FromResult(MessageDialogResult.Ok); }
        }

        private sealed class FakeNavigation : INavigationService
        {
            public NavigationRequest Request { get; private set; }
            public bool WaitForClose { get; set; }
            public int CallCount { get; private set; }
            public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task<NavigationResult> ShowAsync(NavigationRequest request, CancellationToken cancellationToken = default)
            {
                Request = request;
                CallCount++;
                Started.TrySetResult(true);
                if (WaitForClose)
                    await Finish.Task;
                return new CollectionSettingsResult(false);
            }
            public Task<bool> CloseAsync(NavigationDestination destination, NavigationResult result = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        }

        private sealed class ImmediateDispatcher : IUiDispatcher
        {
            public bool CheckAccess() => true;
            public void Post(Action action) => action();
            public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { action(); return Task.CompletedTask; }
            public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => Task.FromResult(action());
        }

        private sealed class NullImages : IImageService
        { public Task<ImageData> LoadAsync(ImageRequest request, CancellationToken cancellationToken = default) => Task.FromResult<ImageData>(null); }
    }
}
