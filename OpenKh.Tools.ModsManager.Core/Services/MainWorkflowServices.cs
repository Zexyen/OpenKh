using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class ModWorkflowService : IModWorkflowService
    {
        public async Task<ModInstallResult> InstallAsync(ModInstallRequest request,
            IProgress<ProgressDialogUpdate> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ModsService.InstallMod(request.Name, request.IsArchive, request.IsLua,
                text => progress?.Report(new ProgressDialogUpdate(Message: text)),
                value => progress?.Report(new ProgressDialogUpdate(Value: value, IsIndeterminate: false)),
                request.BranchName).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var installedName = request.Name;
            if (!request.IsArchive && !request.IsLua)
            {
                var parts = request.Name.Split('/');
                if (parts.Length == 3)
                    installedName = $"{parts[0]}/{parts[1]}";
            }
            if (request.IsArchive || request.IsLua)
                installedName = Path.GetFileNameWithoutExtension(request.Name);
            return new ModInstallResult(result.OverwroteExistingMod, installedName);
        }

        public Task RemoveAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModsService.CleanModFiles(path);
            return Task.CompletedTask;
        }

        public IReadOnlyList<ModModel> GetMods(IEnumerable<string> names = null) =>
            ModsService.GetMods(names?.ToArray() ?? ModsService.Mods).ToList();

        public async IAsyncEnumerable<ModUpdateModel> FetchUpdatesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var update in ModsService.FetchUpdates().WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return update;
        }

        public async Task UpdateAsync(string source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ModsService.Update(source).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async Task<bool> BuildAsync(bool fastMode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ModsService.RunPatcherAsync(fastMode).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }

    public sealed class PresetService : IPresetService
    {
        public IReadOnlyList<string> GetNames() => Directory.GetFiles(ConfigurationService.PresetPath)
            .Select(Path.GetFileNameWithoutExtension).ToList();

        public void Save(string name, IEnumerable<string> enabledMods)
        {
            var safeName = string.Join("+", name.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllLines(Path.Combine(ConfigurationService.PresetPath, safeName + ".txt"), enabledMods);
        }

        public bool TryLoad(string name, out IReadOnlyList<string> enabledMods)
        {
            var path = Path.Combine(ConfigurationService.PresetPath, name + ".txt");
            enabledMods = File.Exists(path) ? File.ReadAllLines(path) : null;
            return enabledMods != null;
        }

        public void Remove(string name) => File.Delete(Path.Combine(ConfigurationService.PresetPath, name + ".txt"));
    }

    public sealed class ApplicationUpdateChecker : IApplicationUpdateChecker
    {
        public async Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken)
        {
            var result = await new OpenkhUpdateCheckerService().CheckAsync(cancellationToken).ConfigureAwait(false);
            return new ApplicationUpdateInfo(result.HasUpdate, result.CurrentVersion?.ToString(),
                result.NewVersion?.ToString(), result.DownloadZipUrl);
        }
    }

    public sealed class UnsupportedApplicationUpdateExecutor : IApplicationUpdateExecutor
    {
        public bool CanExecute => false;
        public Task ExecuteAsync(string downloadUrl, IProgress<double> progress, CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException("In-place application updates are not supported."));
    }
}
