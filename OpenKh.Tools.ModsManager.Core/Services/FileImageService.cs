using OpenKh.Tools.ModsManager.Interfaces;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class FileImageService : IImageService
    {
        public async Task<ImageData> LoadAsync(ImageRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(request?.Source) || !File.Exists(request.Source))
                return null;

            var bytes = await File.ReadAllBytesAsync(request.Source, cancellationToken);
            return new ImageData(bytes, ImagePixelFormat.Encoded);
        }
    }
}
