using OpenKh.Common;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Xceed.Wpf.Toolkit;

namespace OpenKh.Tools.ModsManager.Views
{
    /// <summary>
    /// Interaction logic for SetupWizardWindow.xaml
    /// </summary>
    public partial class SetupWizardWindow : Window
    {
        private readonly SetupWizardViewModel _vm;
        private readonly Dictionary<SetupWizardStep, WizardPage> _pages;
        private readonly Dictionary<WizardPage, SetupWizardStep> _steps;
        private readonly List<SetupWizardStep> _history = new List<SetupWizardStep>();

        public SetupWizardWindow() : this(WpfPlatformComposition.CreateSetupWizardViewModel())
        {
        }

        public SetupWizardWindow(SetupWizardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = _vm = viewModel;

            _pages = new Dictionary<SetupWizardStep, WizardPage>
            {
                [SetupWizardStep.Intro] = IntroPage,
                [SetupWizardStep.GameEdition] = PageGameEdition,
                [SetupWizardStep.IsoSelection] = PageIsoSelection,
                [SetupWizardStep.PanaceaInstall] = PageEosInstall,
                [SetupWizardStep.LuaBackendInstall] = PageLuaBackendInstall,
                [SetupWizardStep.SteamApiTrick] = PageSteamAPITrick,
                [SetupWizardStep.GameData] = PageGameData,
                [SetupWizardStep.Region] = PageRegion,
                [SetupWizardStep.Finish] = LastPage,
            };
            _steps = new Dictionary<WizardPage, SetupWizardStep>();
            foreach (var page in _pages)
                _steps.Add(page.Value, page.Key);

            _vm.PropertyChanged += (_, _) => ApplyRoutes();
            RecordPage(_steps[wizard.CurrentPage]);
            ApplyRoutes();

            Closed += (sender, e) => _vm.Dispose();
            _ = _vm.InitializeAsync();
        }

        private void Wizard_Finish(object sender, Xceed.Wpf.Toolkit.Core.CancelRoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Wizard_PageChanged(object sender, RoutedEventArgs e)
        {
            if (_vm is null || _steps is null)
                return;

            RecordPage(_steps[((Wizard)sender).CurrentPage]);
            ApplyRoutes();
        }

        private void RecordPage(SetupWizardStep step)
        {
            var found = _history.IndexOf(step);
            if (found >= 0)
                _history.RemoveRange(found + 1, _history.Count - found - 1);
            else
                _history.Add(step);
        }

        private void ApplyRoutes()
        {
            if (_pages is null)
                return;

            foreach (var page in _pages)
            {
                var next = SetupWizardRouteCalculator.GetNextStep(page.Key, _vm.RouteState);
                page.Value.NextPage = next.HasValue ? _pages[next.Value] : null;
            }

            var back = _history.Count > 1 ? _pages[_history[_history.Count - 2]] : null;
            wizard.CurrentPage.PreviousPage = back;
        }

        private void NavigateURL(object sender, RequestNavigateEventArgs e) =>
            new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = e.Uri.AbsoluteUri
                }
            }.Using(x => x.Start());
    }
}
