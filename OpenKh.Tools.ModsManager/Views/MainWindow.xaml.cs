using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;
using System;
using System.Windows;

namespace OpenKh.Tools.ModsManager.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ModsService.Initialize(new WpfMessageDialogService());
            var platform = WpfPlatformComposition.Create();
            ModViewModelFactory.Configure((model, changeState) => new ModViewModel(model, changeState,
                platform.Progress, platform.Messages, platform.Dispatcher, platform.Navigation, platform.Images));
            DataContext = new MainViewModel(platform.Progress);
        }

        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainViewModel)?.CloseAllWindows();
            WinSettings.Default.Save();
            base.OnClosed(e);
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
