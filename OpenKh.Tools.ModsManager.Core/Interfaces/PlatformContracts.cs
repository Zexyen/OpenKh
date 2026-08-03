using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Interfaces
{
    public interface IUiDispatcher
    {
        bool CheckAccess();
        void Post(Action action);
        Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
        Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
    }

    public interface IMessageDialogService
    {
        Task<MessageDialogResult> ShowAsync(MessageDialogRequest request, CancellationToken cancellationToken = default);
    }

    public interface IFilePickerService
    {
        Task<IReadOnlyList<string>> OpenFilesAsync(OpenFileRequest request, CancellationToken cancellationToken = default);
        Task<string> SaveFileAsync(SaveFileRequest request, CancellationToken cancellationToken = default);
        Task<string> OpenFolderAsync(OpenFolderRequest request, CancellationToken cancellationToken = default);
    }

    public interface IClipboardService
    {
        Task SetTextAsync(string text, CancellationToken cancellationToken = default);
        Task<string> GetTextAsync(CancellationToken cancellationToken = default);
    }

    public interface IBrowserService
    {
        Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
    }

    public interface IApplicationLifetime
    {
        void Shutdown(int exitCode = 0);
    }

    public interface INavigationService
    {
        Task<NavigationResult> ShowAsync(NavigationRequest request, CancellationToken cancellationToken = default);
        Task<bool> CloseAsync(NavigationDestination destination, NavigationResult result = null, CancellationToken cancellationToken = default);
    }

    public interface IProgressDialogService
    {
        Task<ProgressDialogResult> RunAsync(
            ProgressDialogRequest request,
            Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> operation,
            CancellationToken cancellationToken = default);
    }

    public interface IDebugLogService
    {
        IDebugLogSession Start(DebugLogRequest request = null);
    }

    public interface IDebugLogSession : IDisposable
    {
        bool IsOpen { get; }
        void Write(DebugLogEntry entry);
        Task ShowAsync(CancellationToken cancellationToken = default);
        Task CloseAsync(CancellationToken cancellationToken = default);
    }

    public interface IShellProcessLauncher
    {
        Task<ShellProcessResult> LaunchAsync(ShellProcessRequest request, CancellationToken cancellationToken = default);
    }

    public interface IImageService
    {
        Task<ImageData> LoadAsync(ImageRequest request, CancellationToken cancellationToken = default);
    }

    public enum MessageDialogKind
    {
        Information,
        Warning,
        Error,
        Question
    }

    public enum MessageDialogButtons
    {
        Ok,
        OkCancel,
        YesNo,
        YesNoCancel
    }

    public enum MessageDialogResult
    {
        None,
        Ok,
        Cancel,
        Yes,
        No
    }

    public sealed record MessageDialogRequest(
        string Message,
        string Title = null,
        MessageDialogKind Kind = MessageDialogKind.Information,
        MessageDialogButtons Buttons = MessageDialogButtons.Ok);

    public sealed record FilePickerFilter(string Name, IReadOnlyList<string> Patterns);

    public sealed record OpenFileRequest(
        string Title = null,
        bool AllowMultiple = false,
        string SuggestedStartLocation = null,
        IReadOnlyList<FilePickerFilter> Filters = null);

    public sealed record SaveFileRequest(
        string Title = null,
        string SuggestedFileName = null,
        string SuggestedStartLocation = null,
        string DefaultExtension = null,
        IReadOnlyList<FilePickerFilter> Filters = null);

    public sealed record OpenFolderRequest(string Title = null, string SuggestedStartLocation = null);

    public enum NavigationDestination
    {
        InstallSelection,
        SetupWizard,
        CollectionSettings,
        Presets,
        YamlGenerator,
        ModSearch
    }

    public interface INavigationContext { }

    public abstract record NavigationParameter;

    public sealed record InstallSelectionParameter(string RepositoryName = null, string BranchName = null) : NavigationParameter;
    public sealed record CollectionSettingsParameter(INavigationContext Context) : NavigationParameter;
    public sealed record PresetsParameter(INavigationContext Context) : NavigationParameter;
    public sealed record YamlGeneratorParameter(INavigationContext Context = null) : NavigationParameter;
    public sealed record ModSearchParameter(INavigationContext Context = null) : NavigationParameter;

    public abstract record NavigationResult(bool Accepted);

    public sealed record EmptyNavigationResult(bool Accepted = true) : NavigationResult(Accepted);
    public sealed record InstallSelectionResult(
        bool Accepted,
        string RepositoryName = null,
        string BranchName = null,
        bool IsArchive = false,
        bool IsLua = false) : NavigationResult(Accepted);
    public sealed record SetupWizardResult(bool Accepted, bool Completed) : NavigationResult(Accepted);
    public sealed record CollectionSettingsResult(bool Accepted) : NavigationResult(Accepted);

    public sealed record NavigationRequest(
        NavigationDestination Destination,
        NavigationParameter Parameter = null,
        bool IsModal = false);

    public sealed record ProgressDialogRequest(
        string Title,
        string Message = null,
        double? Value = null,
        bool IsIndeterminate = true,
        bool IsCancellable = true);

    public sealed record ProgressDialogUpdate(
        string Title = null,
        string Message = null,
        double? Value = null,
        bool? IsIndeterminate = null,
        bool? IsCancellable = null);

    public sealed record ProgressDialogResult(bool IsCancelled, bool Completed = true);

    public sealed record DebugLogRequest(string Title = "OpenKh debug log", bool ShowImmediately = false);
    public sealed record DebugLogEntry(string Message, DateTimeOffset? Timestamp = null, string Category = null);

    public sealed record ShellProcessRequest(
        string FileName,
        string Arguments = null,
        string WorkingDirectory = null,
        bool UseShellExecute = false,
        bool CreateNoWindow = false);

    public sealed record ShellProcessResult(bool Started, int? ProcessId = null);

    public enum ImagePixelFormat
    {
        Encoded,
        Rgba8888,
        Bgra8888
    }

    /// <summary>
    /// Immutable, framework-neutral image payload. The constructor and
    /// <see cref="ToArray"/> keep ownership of the encoded/pixel bytes explicit.
    /// </summary>
    public sealed class ImageData : IEquatable<ImageData>
    {
        private readonly byte[] _bytes;

        public ImageData(ReadOnlyMemory<byte> bytes, ImagePixelFormat format,
            int? pixelWidth = null, int? pixelHeight = null, string mediaType = null)
        {
            _bytes = bytes.ToArray();
            Format = format;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            MediaType = mediaType;
        }

        public ReadOnlyMemory<byte> Bytes => new ReadOnlyMemory<byte>((byte[])_bytes.Clone());
        public ImagePixelFormat Format { get; }
        public int? PixelWidth { get; }
        public int? PixelHeight { get; }
        public string MediaType { get; }

        public byte[] ToArray() => (byte[])_bytes.Clone();

        public bool Equals(ImageData other) =>
            other != null &&
            Format == other.Format &&
            PixelWidth == other.PixelWidth &&
            PixelHeight == other.PixelHeight &&
            string.Equals(MediaType, other.MediaType, StringComparison.OrdinalIgnoreCase) &&
            _bytes.SequenceEqual(other._bytes);

        public override bool Equals(object obj) => Equals(obj as ImageData);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Format);
            hash.Add(PixelWidth);
            hash.Add(PixelHeight);
            hash.Add(MediaType, StringComparer.OrdinalIgnoreCase);
            foreach (var value in _bytes)
                hash.Add(value);
            return hash.ToHashCode();
        }
    }

    public sealed record ImageRequest(string Source, int? DecodePixelWidth = null, int? DecodePixelHeight = null);
}
