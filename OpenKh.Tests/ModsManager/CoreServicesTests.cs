using OpenKh.Patcher;
using OpenKh.Tools.ModsManager.Exceptions;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenKh.Tests.ModsManager
{
    public class CoreServicesTests
    {
        [Theory]
        [InlineData("sora red", "Red Sora recolor", true)]
        [InlineData("  SORA   red  ", "Red Sora recolor", true)]
        [InlineData("sora blue", "Red Sora recolor", false)]
        [InlineData("", "anything", true)]
        public void KeywordsMatcherRequiresEveryCaseInsensitiveTerm(
            string input, string candidate, bool expected)
        {
            var matcher = new KeywordsMatcherService().CreateMatcher(input);

            Assert.Equal(expected, matcher(candidate));
        }

        [Fact]
        public void WizardPageStackTracksTheVisitedBranchAndRaisesBackNotification()
        {
            var first = new object();
            var second = new object();
            var third = new object();
            var alternate = new object();
            var service = new WizardPageStackService();
            var changes = new List<string>();
            ((INotifyPropertyChanged)service).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            service.OnPageChanged(first);
            service.OnPageChanged(second);
            service.OnPageChanged(third);
            Assert.Same(second, service.Back);

            service.OnPageChanged(second);
            Assert.Same(first, service.Back);
            service.OnPageChanged(alternate);

            Assert.Same(second, service.Back);
            Assert.All(changes, name => Assert.Equal(nameof(WizardPageStackService.Back), name));
        }

        [Fact]
        public async Task YamlGeneratorNormalizesExistingNamesAndAddsOnlyMissingFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), $"openkh-yaml-generator-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "obj", "nested"));
                await File.WriteAllTextAsync(Path.Combine(root, "obj", "existing.bin"), "existing");
                await File.WriteAllTextAsync(Path.Combine(root, "obj", "nested", "new.bin"), "new");
                var assets = new List<AssetFile>
                {
                    new AssetFile
                    {
                        Name = "obj\\existing.bin",
                        Source = new List<AssetFile>
                        {
                            new AssetFile { Name = "obj\\existing.bin" },
                        },
                    },
                };

                await new YamlGeneratorService().RefillAssetFilesAsync(assets, root);

                Assert.Equal("obj/existing.bin", assets[0].Name);
                Assert.Equal("obj/existing.bin", assets[0].Source.Single().Name);
                var added = Assert.Single(assets.Where(it => it.Name == "obj/nested/new.bin"));
                Assert.Equal("copy", added.Method);
                Assert.Equal("obj/nested/new.bin", Assert.Single(added.Source).Name);
                Assert.Single(assets.Where(it => it.Name == "obj/existing.bin"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Theory]
        [InlineData("kh1", "Kingdom Hearts I")]
        [InlineData("kh2", "Kingdom Hearts II")]
        [InlineData("Recom", "Kingdom Hearts Re:Chain of Memories")]
        public void GameLookupReturnsKnownGames(string id, string name) =>
            Assert.Equal(name, GameService.Lookup(id).Name);

        [Theory]
        [InlineData(MessageDialogResult.Yes, true)]
        [InlineData(MessageDialogResult.No, false)]
        [InlineData(MessageDialogResult.Cancel, false)]
        [InlineData(MessageDialogResult.None, false)]
        public async Task QueryApplyPatchReturnsTrueOnlyForYes(MessageDialogResult result, bool expected)
        {
            var dialog = new FakeMessageDialogService(result);

            var actual = await new QueryApplyPatchService(dialog).QueryAsync();

            Assert.Equal(expected, actual);
            Assert.Equal("Do you apply the result of output file?", dialog.Request.Message);
            Assert.Equal("ModsManager", dialog.Request.Title);
            Assert.Equal(MessageDialogKind.Warning, dialog.Request.Kind);
            Assert.Equal(MessageDialogButtons.YesNoCancel, dialog.Request.Buttons);
        }

        [Fact]
        public async Task ModsServiceOverwriteYesDeletesExistingDirectoryAndReturnsOutcome()
        {
            var root = Path.Combine(Path.GetTempPath(), $"openkh-mod-overwrite-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "old.txt"), "old");
            var dialog = new FakeMessageDialogService(MessageDialogResult.Yes);
            ModsService.Initialize(dialog);

            try
            {
                var result = await ModsService.PrepareInstallPathAsync(root, "author/mod");

                Assert.True(result.OverwroteExistingMod);
                Assert.False(Directory.Exists(root));
                Assert.Equal(MessageDialogButtons.YesNo, dialog.Request.Buttons);
                Assert.Equal(MessageDialogKind.Question, dialog.Request.Kind);
                Assert.Contains("author/mod", dialog.Request.Message);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task ModsServiceOverwriteNoPreservesExistingDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), $"openkh-mod-overwrite-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var existingFile = Path.Combine(root, "old.txt");
            await File.WriteAllTextAsync(existingFile, "old");
            ModsService.Initialize(new FakeMessageDialogService(MessageDialogResult.No));

            try
            {
                await Assert.ThrowsAsync<ModAlreadyExistsExceptions>(() =>
                    ModsService.PrepareInstallPathAsync(root, "author/mod"));
                Assert.True(File.Exists(existingFile));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private sealed class FakeMessageDialogService : IMessageDialogService
        {
            private readonly MessageDialogResult _result;

            public FakeMessageDialogService(MessageDialogResult result) => _result = result;

            public MessageDialogRequest Request { get; private set; }

            public Task<MessageDialogResult> ShowAsync(
                MessageDialogRequest request,
                CancellationToken cancellationToken = default)
            {
                Request = request;
                return Task.FromResult(_result);
            }
        }
    }
}
