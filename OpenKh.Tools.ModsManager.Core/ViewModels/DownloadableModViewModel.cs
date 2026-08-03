using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OpenKh.Tools.ModsManager.ViewModels
{
    public class DownloadableModViewModel : INotifyPropertyChanged
    {
        private readonly DownloadableModModel _model;
        private readonly IProgressDialogService _progressDialogs;
        private readonly IMessageDialogService _messages;
        private readonly IUiDispatcher _dispatcher;
        private readonly Func<string, Action<string>, Action<float>, Task> _installMod;
        private readonly AsyncCommand _installCommand;
        private bool _isInstalling;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<DownloadableModViewModel> ModInstalled;

        public DownloadableModViewModel(DownloadableModModel model, IProgressDialogService progressDialogs,
            IMessageDialogService messages, IUiDispatcher dispatcher)
            : this(model, progressDialogs, messages, dispatcher,
                  (repo, progress, progressNumber) => ModsService.InstallModFromGithub(repo, progress, progressNumber))
        {
        }

        internal DownloadableModViewModel(DownloadableModModel model, IProgressDialogService progressDialogs,
            IMessageDialogService messages, IUiDispatcher dispatcher,
            Func<string, Action<string>, Action<float>, Task> installMod)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _progressDialogs = progressDialogs ?? throw new ArgumentNullException(nameof(progressDialogs));
            _messages = messages ?? throw new ArgumentNullException(nameof(messages));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _installMod = installMod ?? throw new ArgumentNullException(nameof(installMod));
            _installCommand = new AsyncCommand(InstallModAsync, () => !IsInstalling);
            InstallCommand = _installCommand;
        }

        public string Repo => _model.Repo;
        public string Title => _model.Title;
        public string Author => _model.OriginalAuthor;
        public string Description => _model.Description;
        public string Game => _model.Game;
        public ImageData IconImage => _model.IconImage;
        public ImageData ScreenshotImage => _model.ScreenshotImageSource;
        public string RepoUrl => $"https://github.com/{_model.Repo}";

        public ICommand InstallCommand { get; }

        public bool IsInstalling
        {
            get => _isInstalling;
            private set
            {
                if (_isInstalling == value)
                    return;

                _isInstalling = value;
                OnPropertyChanged();
                _installCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task InstallModAsync()
        {
            IsInstalling = true;
            try
            {
                await _progressDialogs.RunAsync(
                    new ProgressDialogRequest($"Installing {Title}", "Initializing", IsIndeterminate: true, IsCancellable: false),
                    async (progress, cancellationToken) =>
                    {
                        await _installMod(
                            Repo,
                            message => progress.Report(new ProgressDialogUpdate(Message: message)),
                            value => progress.Report(new ProgressDialogUpdate(Value: value, IsIndeterminate: false)));
                    });

                await _dispatcher.InvokeAsync(() => ModInstalled?.Invoke(this));
            }
            catch (Exception ex)
            {
                await _messages.ShowAsync(new MessageDialogRequest(
                    $"Error installing mod {Title}: {ex.Message}",
                    "Error",
                    MessageDialogKind.Error));
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
