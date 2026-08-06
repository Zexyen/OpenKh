using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace OpenKh.Tools.ModsManager.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _closeApproved;
        public MainWindow()
        {
            InitializeComponent();
            ModsService.Initialize(new WpfMessageDialogService());
            DataContext = WpfPlatformComposition.CreateMainViewModel();
            Loaded += InitializeAsync;
            Closing += CloseAsync;
        }

        private async void InitializeAsync(object sender, RoutedEventArgs e)
        {
            Loaded -= InitializeAsync;
            try
            {
                await ((MainViewModel)DataContext).InitializeAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                await new WpfMessageDialogService().ShowAsync(new Interfaces.MessageDialogRequest(
                    exception.Message, "Initialization error", Interfaces.MessageDialogKind.Error));
            }
        }

        private async void CloseAsync(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeApproved) return;
            e.Cancel = true;
            Closing -= CloseAsync;
            if (DataContext is MainViewModel viewModel)
                await viewModel.CloseAsync();
            WinSettings.Default.Save();
            _closeApproved = true;
            Close();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
