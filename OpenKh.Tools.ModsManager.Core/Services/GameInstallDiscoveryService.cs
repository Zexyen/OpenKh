using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class GameInstallDiscoveryService : IGameInstallDiscoveryService
    {
        private const string RemixFolder = "KINGDOM HEARTS -HD 1.5+2.5 ReMIX-";
        private const string Kh28Folder = "KINGDOM HEARTS HD 2.8 Final Chapter Prologue";
        private readonly ISetupWizardFileSystem _files;

        public GameInstallDiscoveryService(ISetupWizardFileSystem files = null) =>
            _files = files ?? new SetupWizardFileSystem();

        public Task<GameInstallDiscoveryResult> DiscoverAsync(GameInstallDiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return Task.FromResult(Failure(OperationFailureKind.InvalidRequest, "A discovery request is required."));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(request.Launcher switch
                {
                    PcLauncher.EpicGamesStore => DiscoverEpic(request, cancellationToken),
                    PcLauncher.Steam => DiscoverSteam(request, cancellationToken),
                    _ => Failure(OperationFailureKind.Unsupported, "Automatic discovery is supported only for Epic Games Store and Steam.")
                });
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(Failure(OperationFailureKind.Cancelled, "Discovery was cancelled."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Failure(OperationFailureKind.FileSystem, ex.Message));
            }
        }

        private GameInstallDiscoveryResult DiscoverEpic(GameInstallDiscoveryRequest request, CancellationToken cancellationToken)
        {
            var directory = request.EpicManifestDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!_files.DirectoryExists(directory))
                return Failure(OperationFailureKind.NotFound, "The Epic Games Launcher manifest directory was not found.");

            var installs = new Dictionary<PcGameCollection, DiscoveredGameInstall>();
            foreach (var file in _files.EnumerateFiles(directory, "*.item"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = JObject.Parse(_files.ReadAllText(file));
                var executable = (string)manifest["LaunchExecutable"];
                var installPath = (string)manifest["InstallLocation"];
                PcGameCollection? collection = executable switch
                {
                    "KINGDOM HEARTS HD 1.5+2.5 ReMIX.exe" => PcGameCollection.KingdomHearts1525,
                    "KINGDOM HEARTS HD 2.8 Final Chapter Prologue.exe" => PcGameCollection.KingdomHearts28,
                    _ => null
                };
                if (collection != null && IsValidInstall(installPath, "EOSSDK-Win64-Shipping.dll"))
                    installs[collection.Value] = new(collection.Value, installPath, PcLauncher.EpicGamesStore);
            }
            return Completed(installs.Values);
        }

        private GameInstallDiscoveryResult DiscoverSteam(GameInstallDiscoveryRequest request, CancellationToken cancellationToken)
        {
            var candidates = request.SteamAppsCandidates ?? DefaultSteamAppsCandidates();
            var steamApps = candidates.FirstOrDefault(_files.DirectoryExists);
            if (steamApps == null)
                return Failure(OperationFailureKind.NotFound, "A Steam library directory was not found.");

            var libraries = new List<string> { Path.GetDirectoryName(steamApps) };
            var libraryFile = Path.Combine(steamApps, "libraryfolders.vdf");
            if (_files.FileExists(libraryFile))
            {
                var content = _files.ReadAllText(libraryFile);
                libraries.AddRange(Regex.Matches(content, "\"path\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase).Cast<Match>()
                    .Select(match => match.Groups[1].Value.Replace(@"\\", @"\")));
            }

            var installs = new Dictionary<PcGameCollection, DiscoveredGameInstall>();
            foreach (var library in libraries.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var common = Path.Combine(library, "steamapps", "common");
                AddSteamInstall(installs, PcGameCollection.KingdomHearts1525, Path.Combine(common, RemixFolder));
                AddSteamInstall(installs, PcGameCollection.KingdomHearts28, Path.Combine(common, Kh28Folder));
            }
            return Completed(installs.Values);
        }

        private void AddSteamInstall(IDictionary<PcGameCollection, DiscoveredGameInstall> installs, PcGameCollection collection, string path)
        {
            if (IsValidInstall(path, "steam_api64.dll"))
                installs[collection] = new(collection, path, PcLauncher.Steam);
        }

        private bool IsValidInstall(string path, string marker) =>
            !string.IsNullOrWhiteSpace(path) && _files.DirectoryExists(path) && _files.FileExists(Path.Combine(path, marker));

        private static IReadOnlyList<string> DefaultSteamAppsCandidates()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                return new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps") };
            return new[]
            {
                Path.Combine(home, ".local", "share", "Steam", "steamapps"),
                Path.Combine(home, ".steam", "steam", "steamapps"),
                Path.Combine(home, ".steam", "root", "steamapps"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps")
            };
        }

        private static GameInstallDiscoveryResult Completed(IEnumerable<DiscoveredGameInstall> installs)
        {
            var values = installs.OrderBy(value => value.Collection).ToArray();
            return new(values.Length > 0
                ? OperationOutcome.Success(message: $"Found {values.Length} game installation(s).")
                : OperationOutcome.Failure(OperationFailureKind.NotFound, "No valid game installations were found."), values);
        }

        private static GameInstallDiscoveryResult Failure(OperationFailureKind kind, string message) =>
            new(OperationOutcome.Failure(kind, message), Array.Empty<DiscoveredGameInstall>());
    }
}
