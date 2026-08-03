using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public class QueryApplyPatchService
    {
        private readonly IMessageDialogService _messageDialogService;

        public QueryApplyPatchService(IMessageDialogService messageDialogService)
        {
            _messageDialogService = messageDialogService ??
                throw new ArgumentNullException(nameof(messageDialogService));
        }

        public async Task<bool> QueryAsync()
        {
            var result = await _messageDialogService.ShowAsync(new MessageDialogRequest(
                "Do you apply the result of output file?",
                "ModsManager",
                MessageDialogKind.Warning,
                MessageDialogButtons.YesNoCancel));

            return result == MessageDialogResult.Yes;
        }
    }
}
