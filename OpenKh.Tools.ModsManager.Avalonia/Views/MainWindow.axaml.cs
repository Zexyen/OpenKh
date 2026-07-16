using Avalonia;
using Avalonia.Controls;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.ViewModels;
using System;

namespace OpenKh.Tools.ModsManager.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            RestoreWindowPlacement();
            Closing += (_, _) => SaveWindowPlacement();
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

        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainViewModel)?.CloseAllWindows();
            base.OnClosed(e);
        }
    }
}
