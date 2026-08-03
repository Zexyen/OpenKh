using OpenKh.Patcher;
using OpenKh.Tools.ModsManager.Exceptions;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.Collections.Generic;
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

        [Theory]
        [InlineData(WizardGameEdition.OpenKhGameEngine, PcLauncher.EpicGamesStore, false, true, SetupWizardStep.Finish)]
        [InlineData(WizardGameEdition.Pcsx2, PcLauncher.EpicGamesStore, false, true, SetupWizardStep.IsoSelection)]
        [InlineData(WizardGameEdition.Pc, PcLauncher.EpicGamesStore, false, true, SetupWizardStep.PanaceaInstall)]
        public void SetupWizardGameEditionRouteIsTypedAndPure(
            WizardGameEdition edition, PcLauncher launcher, bool hasKh2Iso, bool isWindows, SetupWizardStep expected)
        {
            var state = new SetupWizardRouteState(edition, launcher, hasKh2Iso, isWindows);
            Assert.Equal(expected, SetupWizardRouteCalculator.GetNextStep(SetupWizardStep.GameEdition, state));
        }

        [Theory]
        [InlineData(PcLauncher.Steam, true, SetupWizardStep.SteamApiTrick)]
        [InlineData(PcLauncher.Steam, false, SetupWizardStep.GameData)]
        [InlineData(PcLauncher.EpicGamesStore, true, SetupWizardStep.GameData)]
        [InlineData(PcLauncher.Other, true, SetupWizardStep.GameData)]
        public void SetupWizardLuaRouteCoversLauncherAndPlatform(
            PcLauncher launcher, bool isWindows, SetupWizardStep expected)
        {
            var state = new SetupWizardRouteState(WizardGameEdition.Pc, launcher, false, isWindows);
            Assert.Equal(expected, SetupWizardRouteCalculator.GetNextStep(SetupWizardStep.LuaBackendInstall, state));
        }

        [Theory]
        [InlineData(WizardGameEdition.Pcsx2, true, SetupWizardStep.Region)]
        [InlineData(WizardGameEdition.Pcsx2, false, SetupWizardStep.Finish)]
        [InlineData(WizardGameEdition.Pc, true, SetupWizardStep.Finish)]
        [InlineData(WizardGameEdition.OpenKhGameEngine, true, SetupWizardStep.Finish)]
        public void SetupWizardGameDataRouteCoversEditionAndKh2Iso(
            WizardGameEdition edition, bool hasKh2Iso, SetupWizardStep expected)
        {
            var state = new SetupWizardRouteState(edition, PcLauncher.EpicGamesStore, hasKh2Iso, true);
            Assert.Equal(expected, SetupWizardRouteCalculator.GetNextStep(SetupWizardStep.GameData, state));
        }

        [Theory]
        [InlineData(SetupWizardStep.Intro, SetupWizardStep.GameEdition)]
        [InlineData(SetupWizardStep.IsoSelection, SetupWizardStep.GameData)]
        [InlineData(SetupWizardStep.PanaceaInstall, SetupWizardStep.LuaBackendInstall)]
        [InlineData(SetupWizardStep.SteamApiTrick, SetupWizardStep.GameData)]
        [InlineData(SetupWizardStep.Region, SetupWizardStep.Finish)]
        public void SetupWizardFixedRoutesAreComplete(SetupWizardStep current, SetupWizardStep expected)
        {
            var state = new SetupWizardRouteState(WizardGameEdition.Pc, PcLauncher.EpicGamesStore, false, true);
            Assert.Equal(expected, SetupWizardRouteCalculator.GetNextStep(current, state));
        }

        [Fact]
        public void SetupWizardFinishHasNoNextStep()
        {
            var state = new SetupWizardRouteState(WizardGameEdition.Pc, PcLauncher.EpicGamesStore, false, true);
            Assert.Null(SetupWizardRouteCalculator.GetNextStep(SetupWizardStep.Finish, state));
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
