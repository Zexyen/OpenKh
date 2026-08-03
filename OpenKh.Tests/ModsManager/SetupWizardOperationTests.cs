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
    public class SetupWizardOperationTests
    {
        [Fact]
        public void OperationOutcomeCarriesSuccessChangeAndFailureSemantics()
        {
            Assert.Equal(new OperationOutcome(true, true), OperationOutcome.Success(true));
            var failure = OperationOutcome.Failure(OperationFailureKind.NotFound, "missing");
            Assert.False(failure.Succeeded);
            Assert.False(failure.Changed);
            Assert.Equal(OperationFailureKind.NotFound, failure.FailureKind);
        }

        [Fact]
        public async Task EpicDiscoveryParsesManifestAndValidatesMarker()
        {
            using var fixture = new TemporaryDirectory();
            var install = fixture.CreateDirectory("game");
            File.WriteAllText(Path.Combine(install, "EOSSDK-Win64-Shipping.dll"), "marker");
            var manifests = fixture.CreateDirectory("manifests");
            File.WriteAllText(Path.Combine(manifests, "kh.item"),
                "{\"LaunchExecutable\":\"KINGDOM HEARTS HD 1.5+2.5 ReMIX.exe\",\"InstallLocation\":" +
                System.Text.Json.JsonSerializer.Serialize(install) + "}");

            var result = await new GameInstallDiscoveryService().DiscoverAsync(
                new GameInstallDiscoveryRequest(PcLauncher.EpicGamesStore, manifests));

            Assert.True(result.Outcome.Succeeded);
            var found = Assert.Single(result.Installs);
            Assert.Equal(PcGameCollection.KingdomHearts1525, found.Collection);
            Assert.Equal(install, found.InstallPath);
        }

        [Fact]
        public async Task SteamDiscoverySelectsLibraryPathFromVdf()
        {
            using var fixture = new TemporaryDirectory();
            var primary = fixture.CreateDirectory("primary/steamapps");
            var library = fixture.CreateDirectory("library");
            var install = fixture.CreateDirectory("library/steamapps/common/KINGDOM HEARTS HD 2.8 Final Chapter Prologue");
            File.WriteAllText(Path.Combine(install, "steam_api64.dll"), "marker");
            File.WriteAllText(Path.Combine(primary, "libraryfolders.vdf"), $"\"path\" \"{library.Replace("\\", "\\\\")}\"");

            var result = await new GameInstallDiscoveryService().DiscoverAsync(
                new GameInstallDiscoveryRequest(PcLauncher.Steam, SteamAppsCandidates: new[] { primary }));

            Assert.Equal(install, Assert.Single(result.Installs).InstallPath);
        }

        [Fact]
        public void LuaConfigurationTransformsSelectedScriptsAndSteamDocuments()
        {
            using var fixture = new TemporaryDirectory();
            var config = "[kh1]\nscripts = [{ path = \"scripts/kh1/\", relative = true }]\n" +
                "game_docs = \"KINGDOM HEARTS HD 1.5+2.5 ReMIX\"\n" +
                "# game_docs = \"My Games/KINGDOM HEARTS HD 1.5+2.5 ReMIX\"\n";

            var result = SetupWizardModLoaderService.TransformLuaConfiguration(config,
                PcGameCollection.KingdomHearts1525, fixture.Path, PcLauncher.Steam,
                new[] { WizardGameId.KingdomHearts1 }, false);

            Assert.Contains("relative = false", result);
            Assert.Contains("# game_docs = \"KINGDOM HEARTS", result);
            Assert.Contains("game_docs = \"My Games/KINGDOM HEARTS", result);
        }

        [Theory]
        [InlineData(true, "DBGHELP.dll")]
        [InlineData(false, "version.dll")]
        public async Task PanaceaInstallSelectsRequestedDestination(bool useDbgHelp, string expected)
        {
            using var fixture = new TemporaryDirectory();
            var source = fixture.CreateDirectory("source");
            var install = fixture.CreateDirectory("install");
            File.WriteAllText(Path.Combine(source, "OpenKH.Panacea.dll"), "panacea");
            foreach (var dependency in PanaceaDependencies)
                File.WriteAllText(Path.Combine(source, dependency), dependency);

            var result = await new SetupWizardModLoaderService().InstallPanaceaAsync(new PanaceaInstallRequest(
                PcGameCollection.KingdomHearts1525, install, source, fixture.Path, useDbgHelp));

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(Path.Combine(install, expected)));
        }

        [Fact]
        public async Task SteamAppIdOperationsReportExactStatusAndChanges()
        {
            using var fixture = new TemporaryDirectory();
            var service = new SetupWizardModLoaderService();
            var request = new CollectionOperationRequest(PcGameCollection.KingdomHearts28, fixture.Path);

            Assert.True((await service.InstallSteamAppIdAsync(request)).Changed);
            var status = await service.GetSteamAppIdStatusAsync(request);
            Assert.True(status.Exists);
            Assert.True(status.HasExpectedValue);
            Assert.Equal("2552440", status.ActualValue);
            Assert.True((await service.RemoveSteamAppIdAsync(request)).Changed);
        }

        [Fact]
        public async Task ProtonUpdateReportsNotFoundWithoutWriting()
        {
            var repository = new FakeProtonRepository("\"apps\" { \"1\" { } }");
            var service = new SetupWizardModLoaderService(proton: repository);

            var result = await service.UpdateProtonLaunchOptionsAsync(
                new ProtonLaunchOptionsRequest(PcGameCollection.KingdomHearts1525));

            Assert.Equal(OperationFailureKind.NotFound, result.Outcome.FailureKind);
            Assert.Equal(0, repository.WriteCount);
        }

        [Fact]
        public async Task ExtractionAdapterHonorsPreCancelledTokenWithoutAssets()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = await new GameDataExtractionService().ExtractAsync(new GameDataExtractionRequest(
                GameDataExtractionSource.Ps2Iso, "destination", "missing.iso", WizardGameId.KingdomHearts2),
                cancellationToken: cancellation.Token);

            Assert.Equal(OperationFailureKind.Cancelled, result.Outcome.FailureKind);
        }

        private static readonly string[] PanaceaDependencies =
        {
            "avcodec-vgmstream-59.dll", "avformat-vgmstream-59.dll", "avutil-vgmstream-57.dll",
            "bass.dll", "bass_vgmstream.dll", "libatrac9.dll", "libcelt-0061.dll", "libcelt-0110.dll",
            "libg719_decode.dll", "libmpg123-0.dll", "libspeex-1.dll", "libvorbis.dll", "swresample-vgmstream-4.dll"
        };

        private sealed class FakeProtonRepository : IProtonConfigRepository
        {
            private string _content;
            public FakeProtonRepository(string content) => _content = content;
            public bool IsSteamRunning => false;
            public int WriteCount { get; private set; }
            public IReadOnlyList<string> GetConfigurationFiles() => new[] { "config.vdf" };
            public string Read(string path) => _content;
            public void BackupAndWrite(string path, string content) { _content = content; WriteCount++; }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenKhTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public string Path { get; }
            public string CreateDirectory(string relative)
            {
                var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(path);
                return path;
            }
            public void Dispose() => Directory.Delete(Path, true);
        }
    }
}
