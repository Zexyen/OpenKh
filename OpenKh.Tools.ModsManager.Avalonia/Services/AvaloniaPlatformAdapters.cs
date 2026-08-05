using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class AvaloniaUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
        public void Post(Action action) => Dispatcher.UIThread.Post(action);
        public async Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(action); }
        public async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return await Dispatcher.UIThread.InvokeAsync(action); }
    }

    public sealed class AvaloniaFilePickerService : IFilePickerService
    {
        public async Task<IReadOnlyList<string>> OpenFilesAsync(OpenFileRequest request, CancellationToken cancellationToken = default)
        {
            var files = await Storage().OpenFilePickerAsync(new FilePickerOpenOptions { Title = request.Title, AllowMultiple = request.AllowMultiple, FileTypeFilter = Types(request.Filters) });
            cancellationToken.ThrowIfCancellationRequested();
            return files.Select(x => x.TryGetLocalPath()).Where(x => x != null).ToArray();
        }
        public async Task<string> SaveFileAsync(SaveFileRequest request, CancellationToken cancellationToken = default)
        {
            var file = await Storage().SaveFilePickerAsync(new FilePickerSaveOptions { Title = request.Title, SuggestedFileName = request.SuggestedFileName, DefaultExtension = request.DefaultExtension, FileTypeChoices = Types(request.Filters) });
            cancellationToken.ThrowIfCancellationRequested(); return file?.TryGetLocalPath();
        }
        public async Task<string> OpenFolderAsync(OpenFolderRequest request, CancellationToken cancellationToken = default)
        {
            var folders = await Storage().OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = request.Title, AllowMultiple = false });
            cancellationToken.ThrowIfCancellationRequested(); return folders.FirstOrDefault()?.TryGetLocalPath();
        }
        private static IStorageProvider Storage() => ActiveWindow().StorageProvider;
        internal static Window ActiveWindow() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(x => x.IsActive)
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        private static IReadOnlyList<FilePickerFileType> Types(IReadOnlyList<FilePickerFilter> filters) => filters?.Select(x => new FilePickerFileType(x.Name) { Patterns = x.Patterns }).ToArray();
    }

    public sealed class AvaloniaClipboardService : IClipboardService
    {
        public async Task SetTextAsync(string text, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); await AvaloniaFilePickerService.ActiveWindow().Clipboard.SetTextAsync(text ?? string.Empty); }
        public async Task<string> GetTextAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return await AvaloniaFilePickerService.ActiveWindow().Clipboard.GetTextAsync(); }
    }
    public sealed class AvaloniaBrowserService : IBrowserService
    { public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; } }
    public sealed class AvaloniaApplicationLifetime : OpenKh.Tools.ModsManager.Interfaces.IApplicationLifetime
    { public void Shutdown(int exitCode = 0) => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(exitCode); }
    public sealed class AvaloniaShellProcessLauncher : IShellProcessLauncher
    {
        public Task<ShellProcessResult> LaunchAsync(ShellProcessRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = Process.Start(new ProcessStartInfo(request.FileName, request.Arguments ?? string.Empty) { WorkingDirectory = request.WorkingDirectory ?? string.Empty, UseShellExecute = request.UseShellExecute, CreateNoWindow = request.CreateNoWindow });
            return Task.FromResult(new ShellProcessResult(process != null, process?.Id));
        }
    }

    public sealed class AvaloniaProgressDialogService : IProgressDialogService
    {
        public async Task<ProgressDialogResult> RunAsync(ProgressDialogRequest request, Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var state = new WorkInProgressWindow.TViewModel(request.Title, request.Message, request.IsIndeterminate, (float)(request.Value ?? 0), new AvaloniaDelegateCommand(linked.Cancel), request.IsCancellable);
            var window = new WorkInProgressWindow { DataContext = state };
            var progress = new Progress<ProgressDialogUpdate>(u => { state = state with { DialogTitle = u.Title ?? state.DialogTitle, OperationName = u.Message ?? state.OperationName, ProgressValue = (float)(u.Value ?? state.ProgressValue), ProgressUnknown = u.IsIndeterminate ?? state.ProgressUnknown, CancelEnabled = u.IsCancellable ?? state.CancelEnabled }; window.ViewModel = state; });
            window.Show(AvaloniaFilePickerService.ActiveWindow());
            try { await operation(progress, linked.Token); return new ProgressDialogResult(linked.IsCancellationRequested, !linked.IsCancellationRequested); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { return new ProgressDialogResult(true, false); }
            finally { window.Close(); }
        }
    }

    public sealed class AvaloniaDebugLogService : IDebugLogService
    {
        public IDebugLogSession Start(DebugLogRequest request = null) => new Session(request ?? new DebugLogRequest());
        private sealed class Session : IDebugLogSession
        {
            private readonly DebugLogRequest _request; private DebuggingWindow _window;
            public Session(DebugLogRequest request) { _request = request; if (request.ShowImmediately) ShowAsync().GetAwaiter().GetResult(); }
            public bool IsOpen => _window?.IsVisible == true;
            public void Write(DebugLogEntry entry) => Debug.WriteLine(entry.Message);
            public Task ShowAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _window ??= new DebuggingWindow { Title = _request.Title }; _window.Show(AvaloniaFilePickerService.ActiveWindow()); return Task.CompletedTask; }
            public Task CloseAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _window?.Close(); _window = null; return Task.CompletedTask; }
            public void Dispose() => CloseAsync().GetAwaiter().GetResult();
        }
    }
    internal sealed class AvaloniaDelegateCommand : ICommand
    { private readonly Action _execute; public AvaloniaDelegateCommand(Action execute) => _execute = execute; public bool CanExecute(object parameter) => true; public void Execute(object parameter) => _execute(); public event EventHandler CanExecuteChanged { add { } remove { } } }
}
