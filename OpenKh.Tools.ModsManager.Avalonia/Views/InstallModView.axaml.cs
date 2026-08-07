using Avalonia.Input;
using Avalonia.Interactivity;
using OpenKh.Tools.Common.Avalonia;
using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;

namespace OpenKh.Tools.ModsManager.Views
{
    public partial class InstallModView : DialogWindowBase
    {
        public ColorThemeService ColorTheme => ColorThemeService.Instance;
        private static readonly FilePickerFilter[] _zipFilter =
        {
            new("Mod archive", new[] { "*.zip", "*.kh2pcpatch", "*.kh1pcpatch", "*.compcpatch", "*.bbspcpatch", "*.dddpcpatch", "*.lua" }),
        };
        private readonly IMessageDialogService _messages;
        private readonly IFilePickerService _files;

        public RelayCommand CloseCommand { get; }
        public string RepositoryName { get; set; }
        public string BranchName { get; set; }
        public bool IsZipFile { get; private set; }
        public bool IsLuaFile { get; private set; } = false;

        public InstallModView() : this(null, null) { }

        internal InstallModView(IMessageDialogService messages, IFilePickerService files)
        {
            InitializeComponent();
            DataContext = this;
            _messages = messages ?? new AvaloniaMessageDialogService(() => this);
            _files = files ?? new AvaloniaFilePickerService(() => this);

            CloseCommand = new RelayCommand(_ => Close());
            Opened += (_, _) => txtSourceModUrl.Focus();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var isBlocked = false;
            var blockedMessage = string.Empty;
            if (ModsService.IsUserBlocked(RepositoryName))
            {
                isBlocked = true;
                blockedMessage = "The author of this mod violated OpenKH rules therefore we do not recommend their mods. Do you wish to install it anyway?";
            }
            else if (ModsService.IsModBlocked(RepositoryName))
            {
                isBlocked = true;
                blockedMessage = "The selected mod violates OpenKH rules, therefore we do not recommend its installation. Do you wish to install it anyway?";
            }

            if (isBlocked)
            {
                var result = await _messages.ShowAsync(new MessageDialogRequest(
                    blockedMessage,
                    $"Warning on installing {RepositoryName}",
                    MessageDialogKind.Warning,
                    MessageDialogButtons.YesNo));
                DialogResult = result == MessageDialogResult.Yes;
            }
            else
                DialogResult = true;

            Close();
        }

        private async void InstallLocalFile_Click(object sender, RoutedEventArgs e)
        {
            var files = await _files.OpenFilesAsync(new OpenFileRequest(Filters: _zipFilter));
            var fileName = files.Count == 0 ? null : files[0];
            if (fileName == null)
                return;

            if (!fileName.Contains(".lua"))
            {
                IsZipFile = true;
                RepositoryName = fileName;
            }
            else
            {
                IsZipFile = false;
                IsLuaFile = true;
                RepositoryName = fileName;
            }
            DialogResult = true;
            Close();
        }

        private void txtSourceModUrl_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Install_Click(sender, e);

            e.Handled = true;
        }
    }
}
