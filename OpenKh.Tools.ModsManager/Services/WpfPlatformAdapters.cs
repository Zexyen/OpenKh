using Microsoft.Win32;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        private readonly Dispatcher _dispatcher;
        public WpfUiDispatcher(Dispatcher dispatcher = null) => _dispatcher = dispatcher ?? Application.Current.Dispatcher;
        public bool CheckAccess() => _dispatcher.CheckAccess();
        public void Post(Action action) => _dispatcher.BeginInvoke(action);
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) =>
            _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
        public async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) =>
            await _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }

    public sealed class WpfFilePickerService : IFilePickerService
    {
        public Task<IReadOnlyList<string>> OpenFilesAsync(OpenFileRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dialog = new OpenFileDialog { Title = request.Title ?? string.Empty, Multiselect = request.AllowMultiple };
            Apply(dialog, request.SuggestedStartLocation, request.Filters);
            IReadOnlyList<string> result = dialog.ShowDialog(GetOwner()) == true ? dialog.FileNames : Array.Empty<string>();
            return Task.FromResult(result);
        }

        public Task<string> SaveFileAsync(SaveFileRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dialog = new SaveFileDialog
            {
                Title = request.Title ?? string.Empty,
                FileName = request.SuggestedFileName ?? string.Empty,
                DefaultExt = request.DefaultExtension ?? string.Empty
            };
            Apply(dialog, request.SuggestedStartLocation, request.Filters);
            return Task.FromResult(dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null);
        }

        public Task<string> OpenFolderAsync(OpenFolderRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dialog = new OpenFolderDialog { Title = request.Title ?? string.Empty, InitialDirectory = request.SuggestedStartLocation ?? string.Empty };
            return Task.FromResult(dialog.ShowDialog(GetOwner()) == true ? dialog.FolderName : null);
        }

        private static Window GetOwner() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? Application.Current?.MainWindow;
        private static void Apply(FileDialog dialog, string start, IReadOnlyList<FilePickerFilter> filters)
        {
            dialog.InitialDirectory = start ?? string.Empty;
            dialog.Filter = filters == null ? string.Empty : string.Join("|", filters.Select(x => $"{x.Name}|{string.Join(";", x.Patterns)}"));
        }
    }

    public sealed class WpfClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Clipboard.SetText(text ?? string.Empty); return Task.CompletedTask; }
        public Task<string> GetTextAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Clipboard.ContainsText() ? Clipboard.GetText() : null); }
    }

    public sealed class WpfBrowserService : IBrowserService
    {
        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        }
    }

    public sealed class WpfApplicationLifetime : IApplicationLifetime
    {
        public void Shutdown(int exitCode = 0) => Application.Current.Shutdown(exitCode);
    }

    public sealed class WpfShellProcessLauncher : IShellProcessLauncher
    {
        public Task<ShellProcessResult> LaunchAsync(ShellProcessRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = Process.Start(new ProcessStartInfo(request.FileName, request.Arguments ?? string.Empty)
            {
                WorkingDirectory = request.WorkingDirectory ?? string.Empty,
                UseShellExecute = request.UseShellExecute,
                CreateNoWindow = request.CreateNoWindow
            });
            return Task.FromResult(new ShellProcessResult(process != null, process?.Id));
        }
    }

    public sealed class WpfProgressDialogService : IProgressDialogService
    {
        public async Task<ProgressDialogResult> RunAsync(ProgressDialogRequest request, Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var state = new WorkInProgressWindow.TViewModel(request.Title, request.Message, request.IsIndeterminate, (float)(request.Value ?? 0), new DelegateCommand(() => linked.Cancel()), request.IsCancellable);
            var window = new WorkInProgressWindow { Owner = Application.Current?.MainWindow, ViewModel = state };
            var progress = new Progress<ProgressDialogUpdate>(update =>
            {
                state = state with
                {
                    DialogTitle = update.Title ?? state.DialogTitle,
                    OperationName = update.Message ?? state.OperationName,
                    ProgressValue = (float)(update.Value ?? state.ProgressValue),
                    ProgressUnknown = update.IsIndeterminate ?? state.ProgressUnknown,
                    CancelEnabled = update.IsCancellable ?? state.CancelEnabled
                };
                window.ViewModel = state;
            });
            window.Show();
            try { await operation(progress, linked.Token); return new ProgressDialogResult(linked.IsCancellationRequested, !linked.IsCancellationRequested); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { return new ProgressDialogResult(true, false); }
            finally { window.Close(); }
        }
    }

    public sealed class WpfDebugLogService : IDebugLogService
    {
        public IDebugLogSession Start(DebugLogRequest request = null) => new Session(request ?? new DebugLogRequest());
        private sealed class Session : IDebugLogSession
        {
            private readonly DebugLogRequest _request;
            private readonly List<DebugLogEntry> _entries = new();
            private DebuggingWindow _window;
            public Session(DebugLogRequest request) { _request = request; if (request.ShowImmediately) ShowAsync().GetAwaiter().GetResult(); }
            public bool IsOpen => _window?.IsVisible == true;
            public void Write(DebugLogEntry entry) { _entries.Add(entry); Debug.WriteLine(entry.Message); }
            public Task ShowAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _window ??= new DebuggingWindow { Owner = Application.Current?.MainWindow, Title = _request.Title }; _window.Show(); return Task.CompletedTask; }
            public Task CloseAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _window?.Close(); _window = null; return Task.CompletedTask; }
            public void Dispose() => CloseAsync().GetAwaiter().GetResult();
        }
    }

    internal sealed class DelegateCommand : ICommand
    {
        private readonly Action _execute;
        public DelegateCommand(Action execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
}
