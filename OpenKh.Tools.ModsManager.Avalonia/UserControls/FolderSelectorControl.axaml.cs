using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;

namespace OpenKh.Tools.ModsManager.UserControls
{
    public partial class FolderSelectorControl : UserControl
    {
        private readonly IFilePickerService _files;

        public FolderSelectorControl() : this(null) { }

        internal FolderSelectorControl(IFilePickerService files)
        {
            InitializeComponent();
            _files = files ?? new AvaloniaFilePickerService(() => TopLevel.GetTopLevel(this) as Window);
        }

        public static readonly StyledProperty<string> FolderPathProperty =
            AvaloniaProperty.Register<FolderSelectorControl, string>(nameof(FolderPath), string.Empty);

        public string FolderPath
        {
            get => GetValue(FolderPathProperty);
            set => SetValue(FolderPathProperty, value);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var path = await _files.OpenFolderAsync(new OpenFolderRequest(SuggestedStartLocation: FolderPath));
            if (path != null)
                FolderPath = path;
        }
    }
}
