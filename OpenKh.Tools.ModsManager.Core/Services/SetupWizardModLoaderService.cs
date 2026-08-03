using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class SetupWizardModLoaderService : ISetupWizardModLoaderService
    {
        private static readonly string[] PanaceaDependencies =
        {
            "avcodec-vgmstream-59.dll", "avformat-vgmstream-59.dll", "avutil-vgmstream-57.dll",
            "bass.dll", "bass_vgmstream.dll", "libatrac9.dll", "libcelt-0061.dll", "libcelt-0110.dll",
            "libg719_decode.dll", "libmpg123-0.dll", "libspeex-1.dll", "libvorbis.dll", "swresample-vgmstream-4.dll"
        };

        private readonly ISetupWizardFileSystem _files;
        private readonly ILuaBackendReleaseSource _luaReleases;
        private readonly IProtonConfigRepository _proton;

        public SetupWizardModLoaderService(
            ISetupWizardFileSystem files = null,
            ILuaBackendReleaseSource luaReleases = null,
            IProtonConfigRepository proton = null)
        {
            _files = files ?? new SetupWizardFileSystem();
            _luaReleases = luaReleases ?? new GitHubLuaBackendReleaseSource();
            _proton = proton ?? new SteamProtonConfigRepository();
        }

        public Task<PanaceaStatusResult> GetPanaceaStatusAsync(PanaceaStatusRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidPath(request?.InstallPath))
                return Task.FromResult(new PanaceaStatusResult(InvalidPath(), false));
            if (string.IsNullOrWhiteSpace(request.SourceDllPath) || !_files.FileExists(request.SourceDllPath))
                return Task.FromResult(new PanaceaStatusResult(OperationOutcome.Success(message: "The source DLL is absent; status is indeterminate in development deployments."), true));

            foreach (var destination in PanaceaDllPaths(request.InstallPath))
            {
                if (_files.FileExists(destination) && FilesEqual(request.SourceDllPath, destination))
                    return Task.FromResult(new PanaceaStatusResult(OperationOutcome.Success(), true, destination));
            }
            return Task.FromResult(new PanaceaStatusResult(OperationOutcome.Success(), false));
        }

        public Task<OperationOutcome> InstallPanaceaAsync(PanaceaInstallRequest request, CancellationToken cancellationToken = default) =>
            RunFileOperation(() =>
            {
                if (!ValidPath(request?.InstallPath) || string.IsNullOrWhiteSpace(request.SourceDirectory))
                    return InvalidPath();
                var source = Path.Combine(request.SourceDirectory, "OpenKH.Panacea.dll");
                if (!_files.FileExists(source))
                    return OperationOutcome.Failure(OperationFailureKind.NotFound, "OpenKH.Panacea.dll was not found.");

                var destinations = PanaceaDllPaths(request.InstallPath);
                var selected = request.UseDbgHelpName ? destinations[0] : destinations[1];
                var alternate = request.UseDbgHelpName ? destinations[1] : destinations[0];
                var dependencies = Path.Combine(request.InstallPath, "dependencies");
                _files.CreateDirectory(dependencies);
                _files.CopyFile(source, selected, true);
                _files.DeleteFile(alternate);
                foreach (var dependency in PanaceaDependencies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dependencySource = Path.Combine(request.SourceDirectory, dependency);
                    if (!_files.FileExists(dependencySource))
                    {
                        CleanupPanacea(request.InstallPath);
                        return OperationOutcome.Failure(OperationFailureKind.NotFound, $"Panacea dependency '{dependency}' was not found.");
                    }
                    _files.CopyFile(dependencySource, Path.Combine(dependencies, dependency), true);
                }
                _files.WriteAllText(Path.Combine(request.InstallPath, "panacea_settings.txt"),
                    $"mod_path={WinePathUtil.ToGamePath(Path.GetFullPath(request.ModRootPath))}{Environment.NewLine}show_console=False{Environment.NewLine}");
                return OperationOutcome.Success(changed: true, message: selected);
            }, cancellationToken);

        public Task<OperationOutcome> RemovePanaceaAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default) =>
            RunFileOperation(() =>
            {
                if (!ValidPath(request?.InstallPath)) return InvalidPath();
                var changed = PanaceaDllPaths(request.InstallPath).Any(_files.FileExists);
                CleanupPanacea(request.InstallPath);
                return OperationOutcome.Success(changed);
            }, cancellationToken);

        public Task<LuaBackendStatusResult> GetLuaBackendStatusAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidPath(request?.InstallPath)) return Task.FromResult(new LuaBackendStatusResult(InvalidPath(), false));
            var installed = _files.FileExists(Path.Combine(request.InstallPath, "LuaBackend.dll")) &&
                _files.FileExists(Path.Combine(request.InstallPath, "LuaBackend.toml"));
            return Task.FromResult(new LuaBackendStatusResult(OperationOutcome.Success(), installed));
        }

        public async Task<LuaBackendDownloadResult> DownloadLuaBackendAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var release = await _luaReleases.GetLatestAsync(cancellationToken);
                if (release == null)
                    return new(OperationOutcome.Failure(OperationFailureKind.NotFound, "The latest Lua Backend release has no downloadable asset."));
                var path = Path.Combine(Path.GetTempPath(), $"LuaBackend-{Guid.NewGuid():N}{Path.GetExtension(release.AssetName)}");
                await _luaReleases.DownloadAsync(release.DownloadUri, path, cancellationToken);
                return new(OperationOutcome.Success(changed: true), new LuaBackendDownload(path, release.Version));
            }
            catch (OperationCanceledException) { return new(OperationOutcome.Failure(OperationFailureKind.Cancelled, "Download was cancelled.")); }
            catch (Exception ex) { return new(OperationOutcome.Failure(OperationFailureKind.Network, ex.Message)); }
        }

        public Task<OperationOutcome> InstallLuaBackendAsync(LuaBackendInstallRequest request, CancellationToken cancellationToken = default) =>
            RunFileOperation(() =>
            {
                if (!ValidPath(request?.InstallPath) || !_files.FileExists(request.ArchivePath)) return InvalidPath();
                var temporary = Path.Combine(Path.GetTempPath(), $"LuaBackend-{Guid.NewGuid():N}");
                try
                {
                    ZipFile.ExtractToDirectory(request.ArchivePath, temporary, true);
                    cancellationToken.ThrowIfCancellationRequested();
                    var dll = Path.Combine(temporary, "DBGHELP.dll");
                    var config = Path.Combine(temporary, "LuaBackend.toml");
                    if (!_files.FileExists(dll) || !_files.FileExists(config))
                        return OperationOutcome.Failure(OperationFailureKind.InvalidData, "The Lua Backend archive is missing DBGHELP.dll or LuaBackend.toml.");
                    _files.MoveFile(dll, Path.Combine(request.InstallPath, "LuaBackend.dll"), true);
                    var transformed = TransformLuaConfiguration(_files.ReadAllText(config), request.Collection, request.ModRootPath,
                        request.Launcher, request.ScriptGames, false);
                    _files.WriteAllText(Path.Combine(request.InstallPath, "LuaBackend.toml"), transformed);
                    return OperationOutcome.Success(changed: true);
                }
                finally
                {
                    if (_files.DirectoryExists(temporary)) _files.DeleteDirectory(temporary, true);
                }
            }, cancellationToken);

        public Task<OperationOutcome> ConfigureLuaBackendAsync(LuaBackendConfigureRequest request, CancellationToken cancellationToken = default) =>
            RunFileOperation(() =>
            {
                if (!ValidPath(request?.InstallPath)) return InvalidPath();
                var path = Path.Combine(request.InstallPath, "LuaBackend.toml");
                if (!_files.FileExists(path)) return OperationOutcome.Failure(OperationFailureKind.NotFound, "LuaBackend.toml was not found.");
                var original = _files.ReadAllText(path);
                var transformed = TransformLuaConfiguration(original, request.Collection, request.ModRootPath,
                    request.Launcher, request.ScriptGames, request.ReplaceExistingOpenKhScriptPaths);
                if (transformed == original) return OperationOutcome.Success();
                _files.WriteAllText(path, transformed);
                return OperationOutcome.Success(changed: true);
            }, cancellationToken);

        public Task<OperationOutcome> RemoveLuaBackendAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default) =>
            RunFileOperation(() =>
            {
                if (!ValidPath(request?.InstallPath)) return InvalidPath();
                var dll = Path.Combine(request.InstallPath, "LuaBackend.dll");
                var toml = Path.Combine(request.InstallPath, "LuaBackend.toml");
                var changed = _files.FileExists(dll) || _files.FileExists(toml);
                _files.DeleteFile(dll); _files.DeleteFile(toml);
                return OperationOutcome.Success(changed);
            }, cancellationToken);

        public async Task<SteamAppIdStatusResult> GetSteamAppIdStatusAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidPath(request?.InstallPath)) return new(InvalidPath(), false, false);
            var path = Path.Combine(request.InstallPath, "steam_appid.txt");
            if (!_files.FileExists(path)) return new(OperationOutcome.Success(), false, false);
            var actual = (await _files.ReadAllTextAsync(path, cancellationToken)).Trim();
            return new(OperationOutcome.Success(), true, actual == AppId(request.Collection), actual);
        }

        public Task<OperationOutcome> InstallSteamAppIdAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default) =>
            WriteSteamAppIdAsync(request, cancellationToken);

        private async Task<OperationOutcome> WriteSteamAppIdAsync(CollectionOperationRequest request, CancellationToken cancellationToken)
        {
            if (!ValidPath(request?.InstallPath)) return InvalidPath();
            try
            {
                var path = Path.Combine(request.InstallPath, "steam_appid.txt");
                var expected = AppId(request.Collection);
                var changed = !_files.FileExists(path) || _files.ReadAllText(path).Trim() != expected;
                await _files.WriteAllTextAsync(path, expected, cancellationToken);
                return OperationOutcome.Success(changed);
            }
            catch (OperationCanceledException) { return OperationOutcome.Failure(OperationFailureKind.Cancelled, "Operation was cancelled."); }
            catch (Exception ex) { return OperationOutcome.Failure(OperationFailureKind.FileSystem, ex.Message); }
        }

        public Task<OperationOutcome> RemoveSteamAppIdAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default) =>
            RunFileOperation(() =>
            {
                if (!ValidPath(request?.InstallPath)) return InvalidPath();
                var path = Path.Combine(request.InstallPath, "steam_appid.txt");
                var changed = _files.FileExists(path); _files.DeleteFile(path);
                return OperationOutcome.Success(changed);
            }, cancellationToken);

        public Task<ProtonLaunchOptionsInspectionResult> InspectProtonLaunchOptionsAsync(ProtonLaunchOptionsRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = _proton.GetConfigurationFiles();
            var found = 0; var configured = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = SteamService.TryGetLaunchOptions(_proton.Read(file), AppId(request.Collection));
                if (value == null) continue;
                found++;
                if (value.Contains("WINEDLLOVERRIDES")) configured++;
            }
            return Task.FromResult(new ProtonLaunchOptionsInspectionResult(OperationOutcome.Success(), found > 0,
                found > 0 && found == configured, _proton.IsSteamRunning, files.Count));
        }

        public Task<ProtonLaunchOptionsUpdateResult> UpdateProtonLaunchOptionsAsync(ProtonLaunchOptionsRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var files = _proton.GetConfigurationFiles(); var found = 0; var updated = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var original = _proton.Read(file);
                    var transformed = SteamService.SetLaunchOptions(original, AppId(request.Collection));
                    if (transformed == null) continue;
                    found++;
                    if (transformed != original) { _proton.BackupAndWrite(file, transformed); updated++; }
                }
                var outcome = found == 0
                    ? OperationOutcome.Failure(OperationFailureKind.NotFound, "No Steam configuration containing this game was found.")
                    : OperationOutcome.Success(updated > 0);
                return Task.FromResult(new ProtonLaunchOptionsUpdateResult(outcome, files.Count, updated, _proton.IsSteamRunning));
            }
            catch (OperationCanceledException) { return Task.FromResult(new ProtonLaunchOptionsUpdateResult(OperationOutcome.Failure(OperationFailureKind.Cancelled, "Operation was cancelled."), 0, 0, _proton.IsSteamRunning)); }
            catch (Exception ex) { return Task.FromResult(new ProtonLaunchOptionsUpdateResult(OperationOutcome.Failure(OperationFailureKind.FileSystem, ex.Message), 0, 0, _proton.IsSteamRunning)); }
        }

        internal static string TransformLuaConfiguration(string config, PcGameCollection collection, string modRootPath,
            PcLauncher launcher, IReadOnlyCollection<WizardGameId> games, bool replaceExisting)
        {
            config = config.Replace("\\", "/");
            var selected = games ?? Array.Empty<WizardGameId>();
            foreach (var game in selected.Where(game => Supports(collection, game)))
            {
                var (section, folder) = LuaNames(game);
                var sectionIndex = config.IndexOf($"[{section}]", StringComparison.Ordinal);
                if (sectionIndex < 0) continue;
                var scriptsIndex = config.IndexOf("scripts", sectionIndex, StringComparison.Ordinal);
                var listEnd = scriptsIndex < 0 ? -1 : config.IndexOf(']', scriptsIndex);
                var absolute = Path.Combine(WinePathUtil.ToGamePathForwardSlashes(Path.GetFullPath(modRootPath)), folder, "scripts").Replace("\\", "/");
                if (scriptsIndex >= 0 && listEnd >= scriptsIndex)
                {
                    var existing = config.Substring(scriptsIndex, listEnd - scriptsIndex + 1);
                    if (existing.Contains("/mod/") && !replaceExisting) continue;
                    var replacement = $"scripts = [{{ path = \"scripts/{section}/\", relative = true }}, {{path = \"{absolute}\" , relative = false}}]";
                    config = config.Remove(scriptsIndex, listEnd - scriptsIndex + 1).Insert(scriptsIndex, replacement);
                }
            }
            if (launcher == PcLauncher.Steam)
            {
                config = EnableSteamDocumentsPath(config, "kh1", "KINGDOM HEARTS HD 1.5+2.5 ReMIX");
                config = EnableSteamDocumentsPath(config, "kh2", "KINGDOM HEARTS HD 1.5+2.5 ReMIX");
                config = EnableSteamDocumentsPath(config, "bbs", "KINGDOM HEARTS HD 1.5+2.5 ReMIX");
                config = EnableSteamDocumentsPath(config, "recom", "KINGDOM HEARTS HD 1.5+2.5 ReMIX");
                config = EnableSteamDocumentsPath(config, "kh3d", "KINGDOM HEARTS HD 2.8 Final Chapter Prologue");
            }
            return config;
        }

        private static string EnableSteamDocumentsPath(string config, string section, string title)
        {
            var sectionIndex = config.IndexOf($"[{section}]", StringComparison.Ordinal);
            if (sectionIndex < 0) return config;
            var epic = config.IndexOf($"game_docs = \"{title}\"", sectionIndex, StringComparison.Ordinal);
            var steam = config.IndexOf($"# game_docs = \"My Games/{title}\"", sectionIndex, StringComparison.Ordinal);
            if (epic >= 0 && steam >= 0) config = config.Remove(steam, 2).Insert(epic, "# ");
            return config;
        }

        private bool FilesEqual(string left, string right)
        {
            using var md5 = MD5.Create(); using var leftStream = _files.OpenRead(left); using var rightStream = _files.OpenRead(right);
            return md5.ComputeHash(leftStream).SequenceEqual(md5.ComputeHash(rightStream));
        }

        private void CleanupPanacea(string path)
        {
            foreach (var dll in PanaceaDllPaths(path)) _files.DeleteFile(dll);
            foreach (var dependency in PanaceaDependencies) _files.DeleteFile(Path.Combine(path, "dependencies", dependency));
            _files.DeleteFile(Path.Combine(path, "panacea_settings.txt"));
        }

        private static Task<OperationOutcome> RunFileOperation(Func<OperationOutcome> operation, CancellationToken token)
        {
            try { token.ThrowIfCancellationRequested(); return Task.FromResult(operation()); }
            catch (OperationCanceledException) { return Task.FromResult(OperationOutcome.Failure(OperationFailureKind.Cancelled, "Operation was cancelled.")); }
            catch (Exception ex) { return Task.FromResult(OperationOutcome.Failure(OperationFailureKind.FileSystem, ex.Message)); }
        }

        private bool ValidPath(string path) => !string.IsNullOrWhiteSpace(path) && _files.DirectoryExists(path);
        private static OperationOutcome InvalidPath() => OperationOutcome.Failure(OperationFailureKind.InvalidRequest, "A valid game installation path is required.");
        private static string[] PanaceaDllPaths(string path) => new[] { Path.Combine(path, "DBGHELP.dll"), Path.Combine(path, "version.dll") };
        private static string AppId(PcGameCollection collection) => collection == PcGameCollection.KingdomHearts1525 ? SteamService.AppIdKh1525 : SteamService.AppIdKh28;
        private static bool Supports(PcGameCollection collection, WizardGameId game) => collection == PcGameCollection.KingdomHearts1525 ? game != WizardGameId.DreamDropDistance : game == WizardGameId.DreamDropDistance;
        private static (string section, string folder) LuaNames(WizardGameId game) => game switch
        {
            WizardGameId.KingdomHearts1 => ("kh1", "kh1"), WizardGameId.KingdomHearts2 => ("kh2", "kh2"),
            WizardGameId.BirthBySleep => ("bbs", "bbs"), WizardGameId.ReChainOfMemories => ("recom", "Recom"),
            _ => ("kh3d", "kh3d")
        };
    }
}
