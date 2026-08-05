using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenKh.Tools.Common.Avalonia;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;
using System.Collections.Generic;

namespace OpenKh.Tools.ModsManager.Views
{
    /// <summary>
    /// Avalonia port of the setup wizard. The Xceed Wizard control is
    /// replaced by a simple page host: all pages live in a Panel and only the
    /// current one is visible. This frontend owns its controls and visited-page
    /// history while Core supplies only typed, pure route decisions.
    /// </summary>
    public partial class SetupWizardWindow : DialogWindowBase
    {
        private sealed record PageInfo(
            string Title,
            string Description,
            System.Func<bool> CanNext,
            System.Func<bool> CanBackAndCancel);

        private readonly SetupWizardViewModel _vm;
        private readonly Dictionary<SetupWizardStep, Control> _controls;
        private readonly Dictionary<SetupWizardStep, PageInfo> _pages;
        private readonly List<SetupWizardStep> _history = new List<SetupWizardStep>();
        private SetupWizardStep _currentStep;

        public SetupWizardWindow() : this(AvaloniaPlatformComposition.CreateSetupWizardViewModel())
        {
        }

        public SetupWizardWindow(SetupWizardViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;

            _controls = new Dictionary<SetupWizardStep, Control>
            {
                [SetupWizardStep.GameEdition] = PageGameEdition,
                [SetupWizardStep.IsoSelection] = PageIsoSelection,
                [SetupWizardStep.PanaceaInstall] = PageEosInstall,
                [SetupWizardStep.LuaBackendInstall] = PageLuaBackendInstall,
                [SetupWizardStep.SteamApiTrick] = PageSteamAPITrick,
                [SetupWizardStep.GameData] = PageGameData,
                [SetupWizardStep.Region] = PageRegion,
                [SetupWizardStep.Finish] = LastPage,
            };
            _pages = new Dictionary<SetupWizardStep, PageInfo>
            {
                [SetupWizardStep.GameEdition] = new PageInfo(
                    "Game edition",
                    "Selected the preferred edition to launch the game",
                    () => _vm.IsGameSelected,
                    () => true),
                [SetupWizardStep.IsoSelection] = new PageInfo(
                    "Configure the game you want to mod",
                    "Do not worry, you can change this option later",
                    () => true,
                    () => true),
                [SetupWizardStep.GameData] = new PageInfo(
                    "Set Game Data Location",
                    "It might be necessary to extract game's data.",
                    () => _vm.IsGameDataFound,
                    () => _vm.IsNotExtracting),
                [SetupWizardStep.Region] = new PageInfo(
                    "Set your preferred region",
                    "This will instruct the game to force to load specific languages",
                    () => _vm.IsGameDataFound,
                    () => true),
                [SetupWizardStep.PanaceaInstall] = new PageInfo(
                    "Install OpenKH Panacea (Optional and Experimental)",
                    "Install automatic mod loading support into the game's folder.",
                    () => true,
                    () => true),
                [SetupWizardStep.LuaBackendInstall] = new PageInfo(
                    "Install Lua Backend",
                    "Lua Backend allows you to use Lua Scripts with the PC version of Kingdom Hearts.",
                    () => true,
                    () => true),
                [SetupWizardStep.SteamApiTrick] = new PageInfo(
                    "Launch Games Directly (Steam)",
                    "Steam allows you to launch the exes directly through a one line text file located in the games install folder.",
                    () => true,
                    () => true),
                [SetupWizardStep.Finish] = new PageInfo(
                    "You're set!",
                    "You successfully configured OpenKH Mods Manager.",
                    () => false,
                    () => true),
            };

            _vm.PropertyChanged += (_, _) => UpdateButtons();
            NavigateTo(SetupWizardStep.GameEdition);

            Closed += (sender, e) => _vm.Dispose();
            _ = _vm.InitializeAsync();
        }

        private void NavigateTo(SetupWizardStep step)
        {
            if (!_controls.TryGetValue(step, out var page))
                return;

            if (_history.Count > 0)
                _controls[_currentStep].IsVisible = false;
            _currentStep = step;
            page.IsVisible = true;

            var found = _history.IndexOf(step);
            if (found >= 0)
                _history.RemoveRange(found + 1, _history.Count - found - 1);
            else
                _history.Add(step);

            var info = _pages[step];
            HeaderTitle.Text = info.Title;
            HeaderDescription.Text = info.Description;

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (_history.Count == 0)
                return;

            var info = _pages[_currentStep];
            var next = SetupWizardRouteCalculator.GetNextStep(_currentStep, _vm.RouteState);
            NextButton.IsVisible = _currentStep != SetupWizardStep.Finish;
            NextButton.IsEnabled = info.CanNext() && next.HasValue && _controls.ContainsKey(next.Value);
            BackButton.IsEnabled = info.CanBackAndCancel() && _history.Count > 1;
            CancelButton.IsEnabled = info.CanBackAndCancel();
            FinishButton.IsVisible = _currentStep == SetupWizardStep.Finish;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count > 1)
                NavigateTo(_history[_history.Count - 2]);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            var info = _pages[_currentStep];
            var next = SetupWizardRouteCalculator.GetNextStep(_currentStep, _vm.RouteState);
            if (info.CanNext() && next.HasValue)
                NavigateTo(next.Value);
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) =>
            Close();
    }
}
