using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class AvaloniaMessageDialogService : IMessageDialogService
    {
        public Task<MessageDialogResult> ShowAsync(
            MessageDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            // System.Windows.MessageBox is supplied by the Avalonia-native
            // compatibility layer in OpenKh.Tools.Common.Avalonia.
            var result = MessageBox.Show(
                request.Message,
                request.Title ?? string.Empty,
                ToCompatButtons(request.Buttons),
                ToCompatImage(request.Kind));

            return Task.FromResult(ToNeutralResult(result));
        }

        private static MessageBoxButton ToCompatButtons(MessageDialogButtons buttons) => buttons switch
        {
            MessageDialogButtons.Ok => MessageBoxButton.OK,
            MessageDialogButtons.OkCancel => MessageBoxButton.OKCancel,
            MessageDialogButtons.YesNo => MessageBoxButton.YesNo,
            MessageDialogButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            _ => throw new ArgumentOutOfRangeException(nameof(buttons)),
        };

        private static MessageBoxImage ToCompatImage(MessageDialogKind kind) => kind switch
        {
            MessageDialogKind.Information => MessageBoxImage.Information,
            MessageDialogKind.Warning => MessageBoxImage.Warning,
            MessageDialogKind.Error => MessageBoxImage.Error,
            MessageDialogKind.Question => MessageBoxImage.Question,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        private static MessageDialogResult ToNeutralResult(MessageBoxResult result) => result switch
        {
            MessageBoxResult.OK => MessageDialogResult.Ok,
            MessageBoxResult.Cancel => MessageDialogResult.Cancel,
            MessageBoxResult.Yes => MessageDialogResult.Yes,
            MessageBoxResult.No => MessageDialogResult.No,
            _ => MessageDialogResult.None,
        };
    }
}
