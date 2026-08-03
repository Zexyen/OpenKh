using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenKh.Tests.ModsManager
{
    public class PlatformContractsTests
    {
        [Fact]
        public void InstallSelectionResult_PreservesTypedSelection()
        {
            NavigationResult result = new InstallSelectionResult(true, "owner/mod", "dev", true, false);
            var selection = Assert.IsType<InstallSelectionResult>(result);
            Assert.True(selection.Accepted);
            Assert.Equal("owner/mod", selection.RepositoryName);
            Assert.True(selection.IsArchive);
            Assert.False(selection.IsLua);
        }

        [Fact]
        public void ProgressUpdate_NullsMeanLeaveExistingContextUnchanged()
        {
            var update = new ProgressDialogUpdate(Message: "Downloading", Value: .5);
            Assert.Null(update.Title);
            Assert.Equal("Downloading", update.Message);
            Assert.Equal(.5, update.Value);
            Assert.Null(update.IsIndeterminate);
            Assert.Null(update.IsCancellable);
        }

        [Fact]
        public async Task NavigationOrchestration_UsesTypedDestinationAndResult()
        {
            var navigation = new FakeNavigationService(new SetupWizardResult(true, true));
            var result = await navigation.ShowAsync(new NavigationRequest(NavigationDestination.SetupWizard, IsModal: true));
            Assert.True(Assert.IsType<SetupWizardResult>(result).Completed);
            Assert.Equal(NavigationDestination.SetupWizard, navigation.LastRequest.Destination);
            Assert.True(navigation.LastRequest.IsModal);
        }

        private sealed class FakeNavigationService : INavigationService
        {
            private readonly NavigationResult _result;
            public FakeNavigationService(NavigationResult result) => _result = result;
            public NavigationRequest LastRequest { get; private set; }
            public Task<NavigationResult> ShowAsync(NavigationRequest request, CancellationToken cancellationToken = default) { LastRequest = request; return Task.FromResult(_result); }
            public Task<bool> CloseAsync(NavigationDestination destination, NavigationResult result = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        }
    }
}
