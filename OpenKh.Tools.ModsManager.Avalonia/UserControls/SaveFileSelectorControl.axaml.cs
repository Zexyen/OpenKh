using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.IO;
using System.Linq;

namespace OpenKh.Tools.ModsManager.UserControls
{
    public partial class SaveFileSelectorControl : UserControl
    {
        private readonly IFilePickerService _files;

        public SaveFileSelectorControl() : this(null) { }

        internal SaveFileSelectorControl(IFilePickerService files)
        {
            InitializeComponent();
            _files = files ?? new AvaloniaFilePickerService(() => TopLevel.GetTopLevel(this) as Window);
        }

        public static readonly StyledProperty<string> FilePathProperty =
            AvaloniaProperty.Register<SaveFileSelectorControl, string>(nameof(FilePath), string.Empty);

        public string FilePath
        {
            get => GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        public static readonly StyledProperty<string> FilterProperty =
            AvaloniaProperty.Register<SaveFileSelectorControl, string>(nameof(Filter), string.Empty);

        public string Filter
        {
            get => GetValue(FilterProperty);
            set => SetValue(FilterProperty, value);
        }

        public static FilePickerFilter[] ParseFilters(string filter) =>
            string.IsNullOrEmpty(filter)
                ? null
                : filter.Split('|')
                    .Chunk(2)
                    .Where(pair => pair.Length == 2)
                    .Select(pair => new FilePickerFilter(pair[0], pair[1].Split(';')))
                    .ToArray();

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var path = await _files.SaveFileAsync(new SaveFileRequest(
                SuggestedFileName: string.IsNullOrEmpty(FilePath) ? null : Path.GetFileName(FilePath),
                SuggestedStartLocation: string.IsNullOrEmpty(FilePath) ? null : Path.GetDirectoryName(FilePath),
                Filters: ParseFilters(Filter)));
            if (path != null)
                FilePath = path;
        }
    }
}
