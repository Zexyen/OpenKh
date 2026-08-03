using System.Linq;
using Xe.Tools.Wpf.Dialogs;
using Xunit;

namespace OpenKh.Tests.Common.Avalonia
{
    public class FileDialogFilterTests
    {
        [Theory]
        [InlineData("*")]
        [InlineData("*.png")]
        [InlineData("file.bin")]
        [InlineData("2ld;*.lad")]
        public void ByPatternsPreservesPatterns(string pattern)
        {
            var filter = FileDialogFilter.ByPatterns("Test", pattern);

            Assert.Equal(new[] { pattern }, filter.Patterns);
        }

        [Fact]
        public void ByPatternsPreservesMultiplePatterns()
        {
            var patterns = new[] { "*.png", "*.jpg", "icon.*" };

            var filter = FileDialogFilter.ByPatterns("Images", patterns);

            Assert.Equal(patterns, filter.Patterns.ToArray());
        }

        [Fact]
        public void ByExtensionsConvertsExtensionsToPatterns()
        {
            var filter = FileDialogFilter.ByExtensions("Images", "png", "jpg");

            Assert.Equal(new[] { "*.png", "*.jpg" }, filter.Patterns);
        }
    }
}
