using Avalonia;
using Avalonia.Controls;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;
using System;

namespace OpenKh.Tools.ModsManager.Views
{
    public partial class MainWindow : Window
    {
        private bool _closeApproved;
        public MainWindow()
        {
            InitializeComponent();
            ModsService.Initialize(new AvaloniaMessageDialogService());
            DataContext = AvaloniaPlatformComposition.CreateMainViewModel();
            Opened += InitializeAsync;

            RestoreWindowPlacement();
            Closing += CloseAsync;
        }

        private async void InitializeAsync(object sender, EventArgs e)
        {
            Opened -= InitializeAsync;
            try
            {
                await ((MainViewModel)DataContext).InitializeAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                await new AvaloniaMessageDialogService().ShowAsync(new Interfaces.MessageDialogRequest(
                    exception.Message, "Initialization error", Interfaces.MessageDialogKind.Error));
            }
        }

        private void RestoreWindowPlacement()
        {
            if (ConfigurationService.WindowWidth > 100 && ConfigurationService.WindowHeight > 100)
            {
                Width = ConfigurationService.WindowWidth;
                Height = ConfigurationService.WindowHeight;
                Position = new PixelPoint(ConfigurationService.WindowX, ConfigurationService.WindowY);
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            if (ConfigurationService.WindowMaximized)
                WindowState = WindowState.Maximized;
        }

        private void SaveWindowPlacement()
        {
            ConfigurationService.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                ConfigurationService.WindowWidth = Width;
                ConfigurationService.WindowHeight = Height;
                ConfigurationService.WindowX = Position.X;
                ConfigurationService.WindowY = Position.Y;
            }
        }

        private async void CloseAsync(object sender, WindowClosingEventArgs e)
        {
            if (_closeApproved) return;
            e.Cancel = true;
            Closing -= CloseAsync;
            SaveWindowPlacement();
            if (DataContext is MainViewModel viewModel)
                await viewModel.CloseAsync();
            _closeApproved = true;
            Close();
        }
    }
}
