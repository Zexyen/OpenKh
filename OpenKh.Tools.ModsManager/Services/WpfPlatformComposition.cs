using OpenKh.Tools.ModsManager.Interfaces;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed record WpfPlatformServices(IUiDispatcher Dispatcher, IMessageDialogService Messages,
        IFilePickerService Files, IClipboardService Clipboard, IBrowserService Browser,
        IApplicationLifetime Lifetime, IProgressDialogService Progress, IDebugLogService DebugLog,
        IShellProcessLauncher Processes, INavigationService Navigation, IImageService Images);

    public static class WpfPlatformComposition
    {
        public static WpfPlatformServices Create() => new(new WpfUiDispatcher(), new WpfMessageDialogService(),
            new WpfFilePickerService(), new WpfClipboardService(), new WpfBrowserService(),
            new WpfApplicationLifetime(), new WpfProgressDialogService(), new WpfDebugLogService(),
            new WpfShellProcessLauncher(), new WpfNavigationService(), new FileImageService());
    }
}
