using OpenKh.Tools.ModsManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Interfaces
{
    public interface IModWorkflowService
    {
        Task<ModInstallResult> InstallAsync(ModInstallRequest request, IProgress<ProgressDialogUpdate> progress,
            CancellationToken cancellationToken);
        Task RemoveAsync(string path, CancellationToken cancellationToken);
        IReadOnlyList<ModModel> GetMods(IEnumerable<string> names = null);
        IAsyncEnumerable<ModUpdateModel> FetchUpdatesAsync(CancellationToken cancellationToken);
        Task UpdateAsync(string source, CancellationToken cancellationToken);
        Task<bool> BuildAsync(bool fastMode, CancellationToken cancellationToken);
    }

    public sealed record ModInstallRequest(string Name, bool IsArchive, bool IsLua, string BranchName = null);
    public sealed record ModInstallResult(bool OverwroteExistingMod, string InstalledName);

    public interface IPresetService
    {
        IReadOnlyList<string> GetNames();
        void Save(string name, IEnumerable<string> enabledMods);
        bool TryLoad(string name, out IReadOnlyList<string> enabledMods);
        void Remove(string name);
    }

    public interface IApplicationUpdateChecker
    {
        Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken);
    }

    public interface IApplicationUpdateExecutor
    {
        bool CanExecute { get; }
        Task ExecuteAsync(string downloadUrl, IProgress<double> progress, CancellationToken cancellationToken);
    }

    public sealed record ApplicationUpdateInfo(bool HasUpdate, string CurrentVersion, string NewVersion, string DownloadUrl);

    public interface IGameWorkflowService
    {
        GameAvailability GetAvailability();
        string GetPreferredGameId(string gameId);
        Task<GameStartResult> StartAsync(GameStartRequest request, CancellationToken cancellationToken);
        void UpdatePanaceaSettings(PanaceaSettings settings);
        Task<PanaceaUpdateResult> ApplyPendingPanaceaUpdateAsync(CancellationToken cancellationToken);
    }

    public interface IGameSession : IAsyncDisposable
    {
        bool IsRunning { get; }
        event EventHandler<GameOutputEventArgs> OutputReceived;
        event EventHandler Exited;
        Task StopAsync(CancellationToken cancellationToken);
    }

    public sealed record GameStartRequest(string GameId);
    public sealed record GameStartResult(IGameSession Session, string ErrorMessage = null,
        bool CloseApplication = false)
    {
        public bool Started => Session != null;
    }

    public sealed record GameAvailability(bool PcInstallExists, bool Kh3dInstallExists,
        bool Kh2IsoExists, bool Kh1IsoExists, bool RecomIsoExists)
    {
        public bool HasMultipleEmulatorGames => new[] { Kh2IsoExists, Kh1IsoExists, RecomIsoExists }.Count(x => x) > 1;
    }

    public sealed record PanaceaSettings(string GameId, bool ShowConsole, bool DebugLog,
        bool SoundDebug, bool EnableCache, bool QuickMenu);
    public sealed record PanaceaUpdateResult(bool Attempted, bool Succeeded);

    public sealed class GameOutputEventArgs : EventArgs
    {
        public GameOutputEventArgs(string text) => Text = text;
        public string Text { get; }
    }

    public interface IGamePatchService
    {
        Task<GamePatchResult> PatchAsync(GamePatchRequest request, IProgress<GamePatchProgress> progress,
            CancellationToken cancellationToken);
        Task<GamePatchResult> RestoreAsync(GameRestoreRequest request, IProgress<GamePatchProgress> progress,
            CancellationToken cancellationToken);
    }

    public sealed record GamePatchRequest(string GameId, bool FastMode);
    public sealed record GameRestoreRequest(string GameId, bool Patched);
    public sealed record GamePatchProgress(string Message, double? Value = null);
    public sealed record GamePatchResult(bool Succeeded, string Message = null);
}
