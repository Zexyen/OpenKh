using Avalonia.Controls;
using OpenKh.Tools.Common.Avalonia;
using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Views
{
    public partial class NotepadWindow : DialogWindowBase
    {
        private readonly IMessageDialogService _messages;
        private readonly IFilePickerService _files;

        public NotepadWindow() : this(null, null) { }

        internal NotepadWindow(IMessageDialogService messages, IFilePickerService files)
        {
            InitializeComponent();
            DataContext = VM = new NotepadVM();
            _messages = messages ?? new AvaloniaMessageDialogService(() => this);
            _files = files ?? new AvaloniaFilePickerService(() => this);

            VM.CopyAllCommand = new AsyncCommand(CopyAllAsync);

            var saveTo = "";

            VM.SaveAsCommand = new AsyncCommand(async () =>
            {
                var path = await _files.SaveFileAsync(new SaveFileRequest(
                    SuggestedFileName: saveTo,
                    Filters: new[] { new FilePickerFilter("All files", new[] { "*" }) }));
                if (path == null)
                    return;
                saveTo = path;
                try
                {
                    await File.WriteAllTextAsync(path, VM.Text);
                }
                catch (Exception exception)
                {
                    await _messages.ShowAsync(new MessageDialogRequest("Failed to save to file!\n\n" + exception));
                }
            });
        }

        private async Task CopyAllAsync()
        {
            try
            {
                await TopLevel.GetTopLevel(this).Clipboard.SetTextAsync(VM.Text);
            }
            catch (Exception exception)
            {
                await _messages.ShowAsync(new MessageDialogRequest("Failed to copy!\n\n" + exception));
            }
        }

        public NotepadVM VM { get; }
    }
}
