using NSubstitute;
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
    public class SetupWizardViewModelTests
    {
        [Fact]
        public void ConstructorExposesBooleanVisibilityAndTypedRouteState()
        {
            using var vm = CreateViewModel();

            Assert.IsType<bool>(vm.IsGameRecognizedVisible);
            Assert.IsType<bool>(vm.IsGameDataFoundVisible);
            Assert.IsType<bool>(vm.IsProgressBarVisible);
            Assert.IsType<SetupWizardRouteState>(vm.RouteState);
            Assert.True(vm.IsNotExtracting);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task PickerCancellationPreservesConfiguredPath()
        {
            var files = Substitute.For<IFilePickerService>();
            files.OpenFolderAsync(Arg.Any<OpenFolderRequest>(), Arg.Any<CancellationToken>()).Returns((string)null);
            using var vm = CreateViewModel(files: files);
            var original = vm.GameDataLocation;

            await vm.SelectGameDataLocationCommand.ExecuteAsync();

            Assert.Equal(original, vm.GameDataLocation);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task DiscoveryPersistsReturnedInstallAndShowsOutcome()
        {
            var discovery = new FakeDiscoveryService(Task.FromResult(
                new GameInstallDiscoveryResult(OperationOutcome.Success(), new[]
                {
                    new DiscoveredGameInstall(PcGameCollection.KingdomHearts1525, "C:\\Games\\KH", PcLauncher.Steam)
                })));
            var messages = Substitute.For<IMessageDialogService>();
            messages.ShowAsync(Arg.Any<MessageDialogRequest>(), Arg.Any<CancellationToken>()).Returns(MessageDialogResult.Ok);
            using var vm = CreateViewModel(discovery: discovery, messages: messages);

            await vm.DetectInstallsCommand.ExecuteAsync();

            Assert.Equal("C:\\Games\\KH", vm.PcReleaseLocation);
            await messages.Received(1).ShowAsync(Arg.Is<MessageDialogRequest>(x => x.Title == "Success"), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AsyncCommandRejectsReentrancyAndInvalidatesBusyState()
        {
            var gate = new TaskCompletionSource<GameInstallDiscoveryResult>();
            var discovery = new FakeDiscoveryService(gate.Task);
            using var vm = CreateViewModel(discovery: discovery);

            var first = vm.DetectInstallsCommand.ExecuteAsync();
            await Task.Yield();
            var second = vm.DetectInstallsCommand.ExecuteAsync();
            Assert.True(vm.IsBusy);
            Assert.False(vm.DetectInstallsCommand.CanExecute(null));
            gate.SetResult(new GameInstallDiscoveryResult(OperationOutcome.Failure(OperationFailureKind.NotFound, "none"), Array.Empty<DiscoveredGameInstall>()));
            await Task.WhenAll(first, second);

            Assert.False(vm.IsBusy);
            Assert.Equal(1, discovery.CallCount);
        }

        [Fact]
        public async Task ExtractionProgressAndSuccessUpdateNeutralState()
        {
            var extraction = Substitute.For<IGameDataExtractionOperations>();
            extraction.ExtractAsync(Arg.Any<GameDataExtractionRequest>(), Arg.Any<IProgress<GameDataExtractionProgress>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    call.Arg<IProgress<GameDataExtractionProgress>>().Report(new GameDataExtractionProgress(0.5f));
                    return new GameDataExtractionResult(OperationOutcome.Success(true));
                });
            using var vm = CreateViewModel(extraction: extraction);
            vm.GameEdition = SetupWizardViewModel.PC;
            vm.OverrideGameDataFound = true;

            await vm.ExtractGameDataCommand.ExecuteAsync();
            await Task.Yield();

            Assert.True(vm.IsNotExtracting);
            Assert.Equal(1f, vm.ExtractionProgress);
            Assert.True(vm.IsExtractionCompleteVisible);
        }

        [Fact]
        public void DisposeCancelsLifetimeAndDisablesCommandsIdempotently()
        {
            var vm = CreateViewModel();

            vm.SetAborted();
            vm.SetAborted();
            vm.Dispose();
            vm.Dispose();

            Assert.False(vm.DetectInstallsCommand.CanExecute(null));
        }

        private static SetupWizardViewModel CreateViewModel(
            IFilePickerService files = null,
            IMessageDialogService messages = null,
            IGameInstallDiscoveryService discovery = null,
            IGameDataExtractionOperations extraction = null)
        {
            var dispatcher = Substitute.For<IUiDispatcher>();
            dispatcher.CheckAccess().Returns(true);
            files ??= Substitute.For<IFilePickerService>();
            messages ??= Substitute.For<IMessageDialogService>();
            messages.ShowAsync(Arg.Any<MessageDialogRequest>(), Arg.Any<CancellationToken>()).Returns(MessageDialogResult.Ok);
            discovery ??= new FakeDiscoveryService(Task.FromResult(
                new GameInstallDiscoveryResult(OperationOutcome.Failure(OperationFailureKind.NotFound, "none"), Array.Empty<DiscoveredGameInstall>())));
            extraction ??= Substitute.For<IGameDataExtractionOperations>();
            var loader = Substitute.For<ISetupWizardModLoaderService>();
            loader.GetPanaceaStatusAsync(Arg.Any<PanaceaStatusRequest>(), Arg.Any<CancellationToken>()).Returns(
                new PanaceaStatusResult(OperationOutcome.Success(), false));
            loader.GetLuaBackendStatusAsync(Arg.Any<CollectionOperationRequest>(), Arg.Any<CancellationToken>()).Returns(
                new LuaBackendStatusResult(OperationOutcome.Success(), false));
            loader.GetSteamAppIdStatusAsync(Arg.Any<CollectionOperationRequest>(), Arg.Any<CancellationToken>()).Returns(
                new SteamAppIdStatusResult(OperationOutcome.Success(), false, false));
            return new SetupWizardViewModel(new SetupWizardDependencies(dispatcher, messages, files, discovery, loader, extraction));
        }

        private sealed class FakeDiscoveryService : IGameInstallDiscoveryService
        {
            private readonly Task<GameInstallDiscoveryResult> _result;
            public FakeDiscoveryService(Task<GameInstallDiscoveryResult> result) => _result = result;
            public int CallCount { get; private set; }
            public Task<GameInstallDiscoveryResult> DiscoverAsync(GameInstallDiscoveryRequest request, CancellationToken cancellationToken = default)
            {
                CallCount++;
                return _result;
            }
        }
    }
}
