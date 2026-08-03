using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class WpfMessageDialogService : IMessageDialogService
    {
        public Task<MessageDialogResult> ShowAsync(
            MessageDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            var result = MessageBox.Show(
                request.Message,
                request.Title ?? string.Empty,
                ToNativeButtons(request.Buttons),
                ToNativeImage(request.Kind));

            return Task.FromResult(ToNeutralResult(result));
        }

        private static MessageBoxButton ToNativeButtons(MessageDialogButtons buttons) => buttons switch
        {
            MessageDialogButtons.Ok => MessageBoxButton.OK,
            MessageDialogButtons.OkCancel => MessageBoxButton.OKCancel,
            MessageDialogButtons.YesNo => MessageBoxButton.YesNo,
            MessageDialogButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            _ => throw new ArgumentOutOfRangeException(nameof(buttons)),
        };

        private static MessageBoxImage ToNativeImage(MessageDialogKind kind) => kind switch
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
