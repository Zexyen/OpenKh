using OpenKh.Common;
using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class GamePatchService : IGamePatchService
    {
        public Task<GamePatchResult> PatchAsync(GamePatchRequest request, IProgress<GamePatchProgress> progress,
            CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ConfigurationService.GameEdition != ViewModels.SetupWizardViewModel.PC)
                return new GamePatchResult(true);
            ArrangePackages(cancellationToken);
            var staging = Path.Combine(ConfigurationService.GameModPath, "patch-staging");
            var packageDirectories = Directory.GetDirectories(staging).Select(Path.GetFileName).ToHashSet();
            var specialStaging = Path.Combine(staging, "special");
            var specialDirectories = Directory.Exists(specialStaging)
                ? Directory.GetDirectories(specialStaging).Select(Path.GetFileName).ToArray()
                : Array.Empty<string>();
            foreach (var package in packageDirectories)
                Directory.Move(Path.Combine(staging, package), Path.Combine(ConfigurationService.GameModPath, package));
            foreach (var special in specialDirectories)
                Directory.Move(Path.Combine(ConfigurationService.GameModPath, "special", special),
                    Path.Combine(ConfigurationService.GameModPath, special));
            packageDirectories.Remove("special");
            Directory.Delete(staging, true);
            var specialRoot = Path.Combine(ConfigurationService.GameModPath, "special");
            if (Directory.Exists(specialRoot)) Directory.Delete(specialRoot, true);

            foreach (var directory in packageDirectories.Select(x => Path.Combine(ConfigurationService.GameModPath, x)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PatchPackage(request, directory, progress);
            }
            return new GamePatchResult(true);
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        public Task<GamePatchResult> RestoreAsync(GameRestoreRequest request, IProgress<GamePatchProgress> progress,
            CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ConfigurationService.GameEdition != ViewModels.SetupWizardViewModel.PC)
                return new GamePatchResult(true);
            if (request.Patched)
            {
                var root = request.GameId == "kh3d" ? ConfigurationService.PcReleaseLocationKH3D : ConfigurationService.PcReleaseLocation;
                var backup = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "BackupImage");
                if (!Directory.Exists(backup))
                    progress?.Report(new GamePatchProgress("backup folder cannot be found! Cannot restore the game."));
                else
                {
                    foreach (var file in Directory.GetFiles(backup, "*.pkg").Where(x => x.Contains(request.GameId)))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new GamePatchProgress($"Restoring Package File {file.Replace(".pkg", "")}"));
                        var destination = Path.Combine(root, "Image", LanguageDirectory(), Path.GetFileName(file));
                        File.Delete(Path.ChangeExtension(destination, "hed"));
                        File.Delete(destination);
                        File.Copy(file, destination);
                        File.Copy(Path.ChangeExtension(file, "hed"), Path.ChangeExtension(destination, "hed"));
                    }
                }
            }
            if (Directory.Exists(ConfigurationService.GameModPath))
            {
                try { Directory.Delete(ConfigurationService.GameModPath, true); }
                catch (Exception ex) { progress?.Report(new GamePatchProgress($"Unable to fully clean the mod directory:\n{ex.Message}")); }
            }
            return new GamePatchResult(true);
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        private static void ArrangePackages(CancellationToken cancellationToken)
        {
            var root = ConfigurationService.GameModPath;
            var map = File.ReadLines(Path.Combine(root, "patch-package-map.txt"))
                .Select(x => x.Split(" $$$$ ")).ToDictionary(x => x[0], x => x[1]);
            var staging = Path.Combine(root, "patch-staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            foreach (var entry in map)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(root, entry.Key);
                var destination = Path.Combine(staging, entry.Value);
                if (!File.Exists(source)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Move(source, destination);
            }
            foreach (var directory in Directory.GetDirectories(root))
                if (Path.GetFileName(directory) != "patch-staging") Directory.Delete(directory, true);
        }

        private static void PatchPackage(GamePatchRequest request, string directory, IProgress<GamePatchProgress> progress)
        {
            var files = new List<string>();
            var original = Path.Combine(directory, "original");
            var raw = Path.Combine(directory, "raw");
            if (Directory.Exists(original)) files.AddRange(OpenKh.Egs.Helpers.GetAllFiles(original));
            if (Directory.Exists(raw)) files.AddRange(OpenKh.Egs.Helpers.GetAllFiles(raw));
            var package = GetPackageName(request.GameId, request.FastMode, Path.GetFileName(directory));
            var root = request.GameId == "kh3d" ? ConfigurationService.PcReleaseLocationKH3D : ConfigurationService.PcReleaseLocation;
            if (string.IsNullOrEmpty(root))
            {
                progress?.Report(new GamePatchProgress("Game Location for selected game cannot be found! Re-run the setup wizard."));
                return;
            }
            var packagePath = Path.Combine(root, "Image", LanguageDirectory(), package + ".pkg");
            var hedPath = Path.ChangeExtension(packagePath, "hed");
            var backup = Path.Combine(root, "BackupImage");
            Directory.CreateDirectory(backup);
            var backupPkg = Path.Combine(backup, package + ".pkg");
            var backupHed = Path.Combine(backup, package + ".hed");
            if (!File.Exists(backupPkg))
            {
                progress?.Report(new GamePatchProgress($"Backing Up Package File {package}"));
                File.Copy(packagePath, backupPkg);
                File.Copy(hedPath, backupHed);
            }
            else
            {
                progress?.Report(new GamePatchProgress($"Restoring Package File {package}"));
                File.Copy(backupPkg, packagePath, true);
                File.Copy(backupHed, hedPath, true);
            }

            using var hed = File.OpenRead(hedPath);
            using var pkg = File.OpenRead(packagePath);
            var headers = OpenKh.Egs.Hed.Read(hed).ToList();
            var output = Path.Combine(Path.GetTempPath(), "OpenKh.ModsManager", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            var outputHed = Path.Combine(output, Path.GetFileName(hedPath));
            var outputPkg = Path.Combine(output, Path.GetFileName(packagePath));
            using (var patchedHed = File.Create(outputHed))
            using (var patchedPkg = File.Create(outputPkg))
            {
                foreach (var header in headers)
                {
                    var hash = OpenKh.Egs.Helpers.ToString(header.MD5);
                    if (!OpenKh.Egs.EgsTools.Names.TryGetValue(hash, out var filename)) continue;
                    var asset = new OpenKh.Egs.EgsHdAsset(pkg.SetPosition(header.Offset));
                    if (files.Remove(filename) && header.DataLength > 0)
                    {
                        OpenKh.Egs.EgsTools.ReplaceFile(directory, filename, patchedHed, patchedPkg, asset, header);
                        progress?.Report(new GamePatchProgress($"Replacing File {filename} in {package}"));
                    }
                    else
                        OpenKh.Egs.EgsTools.ReplaceFile(directory, filename, patchedHed, patchedPkg, asset, header);
                }
                foreach (var filename in files)
                {
                    OpenKh.Egs.EgsTools.AddFile(directory, filename, patchedHed, patchedPkg);
                    progress?.Report(new GamePatchProgress($"Adding File {filename} to {package}"));
                }
            }
            File.Copy(outputHed, hedPath, true);
            File.Copy(outputPkg, packagePath, true);
            Directory.Delete(output, true);
        }

        private static string GetPackageName(string gameId, bool fast, string directory) => gameId switch
        {
            "kh1" => fast ? "kh1_first" : directory,
            "bbs" => fast ? "bbs_first" : directory,
            "Recom" => "Recom",
            "kh3d" => fast ? "kh3d_first" : directory,
            _ => fast ? "kh2_first" : directory
        };
        private static string LanguageDirectory() =>
            ConfigurationService.PCVersion == "Steam" && ConfigurationService.PcReleaseLanguage == "en"
                ? "dt" : ConfigurationService.PcReleaseLanguage;
    }
}
