using Avalonia.Controls;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Views;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class AvaloniaNavigationService : INavigationService
    {
        private readonly Dictionary<NavigationDestination, Window> _open = new();
        public async Task<NavigationResult> ShowAsync(NavigationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = Create(request);
            _open[request.Destination] = window;
            window.Closed += (_, _) => _open.Remove(request.Destination);
            if (request.IsModal)
            {
                var accepted = await window.ShowDialog<bool?>(AvaloniaFilePickerService.ActiveWindow()) == true;
                return ToResult(request.Destination, window, accepted);
            }
            window.Show(AvaloniaFilePickerService.ActiveWindow());
            return new EmptyNavigationResult();
        }

        public Task<bool> CloseAsync(NavigationDestination destination, NavigationResult result = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_open.TryGetValue(destination, out var window)) return Task.FromResult(false);
            window.Close(result?.Accepted);
            return Task.FromResult(true);
        }

        private static Window Create(NavigationRequest request) => request.Destination switch
        {
            NavigationDestination.InstallSelection => ApplyInstall(new InstallModView(), request.Parameter as InstallSelectionParameter),
            NavigationDestination.SetupWizard => new SetupWizardWindow(),
            NavigationDestination.CollectionSettings => ApplyDataContext(new CollectionSettingsView(), (request.Parameter as CollectionSettingsParameter)?.Context),
            NavigationDestination.Presets => ApplyDataContext(new PresetsWindow(), (request.Parameter as PresetsParameter)?.Context),
            NavigationDestination.YamlGenerator => ApplyDataContext(new YamlGeneratorWindow(), (request.Parameter as YamlGeneratorParameter)?.Context),
            NavigationDestination.ModSearch => ApplyDataContext(new ModSearchWindow(), (request.Parameter as ModSearchParameter)?.Context),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Destination), request.Destination, null)
        };
        private static InstallModView ApplyInstall(InstallModView view, InstallSelectionParameter parameter) { view.RepositoryName = parameter?.RepositoryName; view.BranchName = parameter?.BranchName; return view; }
        private static T ApplyDataContext<T>(T window, object viewModel) where T : Window { if (viewModel != null) window.DataContext = viewModel; return window; }
        private static NavigationResult ToResult(NavigationDestination destination, Window window, bool accepted) => destination switch
        {
            NavigationDestination.InstallSelection when window is InstallModView install => new InstallSelectionResult(accepted, install.RepositoryName, install.BranchName, install.IsZipFile, install.IsLuaFile),
            NavigationDestination.SetupWizard => new SetupWizardResult(accepted, accepted),
            NavigationDestination.CollectionSettings => new CollectionSettingsResult(accepted),
            _ => new EmptyNavigationResult(accepted)
        };
    }
}
