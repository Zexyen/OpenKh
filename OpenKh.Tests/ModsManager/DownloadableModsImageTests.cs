using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace OpenKh.Tests.ModsManager
{
    public class DownloadableModsImageTests
    {
        [Fact]
        public void ImageDataOwnsBytesAndUsesContentValueSemantics()
        {
            var source = new byte[] { 1, 2, 3 };
            var image = new ImageData(source, ImagePixelFormat.Encoded, 2, 1, "image/png");
            source[0] = 9;

            Assert.Equal(new byte[] { 1, 2, 3 }, image.ToArray());
            Assert.Equal(
                image,
                new ImageData(new byte[] { 1, 2, 3 }, ImagePixelFormat.Encoded, 2, 1, "IMAGE/PNG"));

            var copy = image.ToArray();
            copy[1] = 9;
            Assert.Equal(new byte[] { 1, 2, 3 }, image.ToArray());
        }

        [Fact]
        public async Task LoadImageUsesFreshCacheWithoutNetworkAndReplacesPlaceholder()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"OpenKh-image-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var cachePath = Path.Combine(directory, "icon.png");
                var cachedBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
                await File.WriteAllBytesAsync(cachePath, cachedBytes);
                var service = new DownloadableModsService(directory);
                var mod = new DownloadableModModel { Repo = "owner/repository" };
                ImageData result = null;

                var loaded = await service.LoadImageWithCache(
                    mod,
                    cachePath,
                    "https://127.0.0.1:1/must-not-be-requested.png",
                    image => result = image);

                Assert.True(loaded);
                Assert.NotNull(result);
                Assert.Equal(cachedBytes, result.ToArray());
                Assert.Equal("image/png", result.MediaType);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task LocalFeedFallbackRequiresNoNetworkAndFiltersByGame()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"OpenKh-feed-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "downloadable-mods.json"),
                    "{\"mods\":{\"kh2\":[{\"repo\":\"owner/mod\"}],\"kh1\":[{\"repo\":\"owner/other\"}]}}");
                var service = new DownloadableModsService(directory);

                var mods = await service.GetDownloadableModsLocallyAsync("kh2");

                var mod = Assert.Single(mods);
                Assert.Equal("owner/mod", mod.Repo);
                Assert.Equal("mod", mod.Title);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
