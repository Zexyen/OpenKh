using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class WpfNavigationService : INavigationService
    {
        private readonly Dictionary<NavigationDestination, Window> _open = new();

        public Task<NavigationResult> ShowAsync(NavigationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = Create(request);
            window.Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? Application.Current?.MainWindow;
            _open[request.Destination] = window;
            window.Closed += (_, _) => _open.Remove(request.Destination);
            if (request.IsModal)
            {
                var accepted = window.ShowDialog() == true;
                return Task.FromResult(ToResult(request.Destination, window, accepted));
            }
            window.Show();
            return Task.FromResult<NavigationResult>(new EmptyNavigationResult());
        }

        public Task<bool> CloseAsync(NavigationDestination destination, NavigationResult result = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_open.TryGetValue(destination, out var window)) return Task.FromResult(false);
            if (result != null && window.IsVisible) window.DialogResult = result.Accepted;
            window.Close();
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

        private static InstallModView ApplyInstall(InstallModView view, InstallSelectionParameter parameter)
        { view.RepositoryName = parameter?.RepositoryName; view.BranchName = parameter?.BranchName; return view; }
        private static T ApplyDataContext<T>(T window, object viewModel) where T : Window
        { if (viewModel != null) window.DataContext = viewModel; return window; }
        private static NavigationResult ToResult(NavigationDestination destination, Window window, bool accepted) => destination switch
        {
            NavigationDestination.InstallSelection when window is InstallModView install => new InstallSelectionResult(accepted, install.RepositoryName, install.BranchName, install.IsZipFile, install.IsLuaFile),
            NavigationDestination.SetupWizard => new SetupWizardResult(accepted, accepted),
            NavigationDestination.CollectionSettings => new CollectionSettingsResult(accepted),
            _ => new EmptyNavigationResult(accepted)
        };
    }
}
