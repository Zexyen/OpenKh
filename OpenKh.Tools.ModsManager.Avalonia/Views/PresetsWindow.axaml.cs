using Avalonia.Input;
using Avalonia.Interactivity;
using OpenKh.Tools.Common.Avalonia;
using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;

namespace OpenKh.Tools.ModsManager.Views
{
    public partial class PresetsWindow : DialogWindowBase
    {
        public ColorThemeService ColorTheme => ColorThemeService.Instance;
        public MainViewModel MainVm { get; set; }
        public RelayCommand CloseCommand { get; }
        public string PresetName { get; set; }
        private readonly IMessageDialogService _messages;

        public PresetsWindow() : this(null, null) { }

        internal PresetsWindow(MainViewModel mvm, IMessageDialogService messages)
        {
            MainVm = mvm;
            InitializeComponent();
            DataContext = this;
            _messages = messages ?? new AvaloniaMessageDialogService(() => this);

            CloseCommand = new RelayCommand(_ => Close());
        }

        public PresetsWindow(MainViewModel mvm) : this(mvm, null) { }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            MainVm.SavePreset(txtSourceModUrl.Text);
        }

        private void txtSourceModUrl_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Save_Click(sender, e);

            e.Handled = true;
        }

        private void Button_ApplyPreset(object sender, RoutedEventArgs e)
        {
            if (List_Presets.SelectedItem == null)
                return;

            string presetName = (string)List_Presets.SelectedItem;
            MainVm.LoadPreset(presetName);
            Close();
        }

        private async void Button_RemovePreset(object sender, RoutedEventArgs e)
        {
            if (List_Presets.SelectedItem == null)
                return;
            string presetName = (string)List_Presets.SelectedItem;
            var result = await _messages.ShowAsync(new MessageDialogRequest(
                $"Do you want to remove {presetName} preset.",
                "Delete Confirmation",
                MessageDialogKind.Information,
                MessageDialogButtons.YesNo));
            if (result == MessageDialogResult.Yes)
            {
                MainVm.RemovePreset(presetName);
            }
        }
    }
}
