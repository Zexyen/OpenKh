using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.ViewModels;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed record AvaloniaPlatformServices(IUiDispatcher Dispatcher, IMessageDialogService Messages,
        IFilePickerService Files, IClipboardService Clipboard, IBrowserService Browser,
        IApplicationLifetime Lifetime, IProgressDialogService Progress, IDebugLogService DebugLog,
        IShellProcessLauncher Processes, INavigationService Navigation, IImageService Images);

    public static class AvaloniaPlatformComposition
    {
        public static AvaloniaPlatformServices Create() => new(new AvaloniaUiDispatcher(), new AvaloniaMessageDialogService(),
            new AvaloniaFilePickerService(), new AvaloniaClipboardService(), new AvaloniaBrowserService(),
            new AvaloniaApplicationLifetime(), new AvaloniaProgressDialogService(), new AvaloniaDebugLogService(),
            new AvaloniaShellProcessLauncher(), new AvaloniaNavigationService(), new FileImageService());

        public static SetupWizardViewModel CreateSetupWizardViewModel()
        {
            var platform = Create();
            return new SetupWizardViewModel(new SetupWizardDependencies(platform.Dispatcher, platform.Messages, platform.Files,
                new GameInstallDiscoveryService(), new SetupWizardModLoaderService(), new GameDataExtractionService()));
        }

        public static MainViewModel CreateMainViewModel()
        {
            var platform = Create();
            return new MainViewModel(new MainViewModelDependencies(platform.Progress, platform.Messages,
                platform.Dispatcher, platform.Navigation, platform.Browser, platform.Lifetime,
                platform.Processes, platform.DebugLog,
                (model, changeState) => new ModViewModel(model, changeState, platform.Progress,
                    platform.Messages, platform.Dispatcher, platform.Navigation, platform.Images),
                new ModWorkflowService(), new PresetService(), new ApplicationUpdateChecker(),
                new UnsupportedApplicationUpdateExecutor(), new GameWorkflowService(platform.Processes),
                new GamePatchService()));
        }
    }
}
