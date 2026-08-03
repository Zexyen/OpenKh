using OpenKh.Tools.ModsManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Interfaces
{
    public interface IGameInstallDiscoveryService
    {
        Task<GameInstallDiscoveryResult> DiscoverAsync(GameInstallDiscoveryRequest request, CancellationToken cancellationToken = default);
    }

    public interface ISetupWizardModLoaderService
    {
        Task<PanaceaStatusResult> GetPanaceaStatusAsync(PanaceaStatusRequest request, CancellationToken cancellationToken = default);
        Task<OperationOutcome> InstallPanaceaAsync(PanaceaInstallRequest request, CancellationToken cancellationToken = default);
        Task<OperationOutcome> RemovePanaceaAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default);
        Task<LuaBackendStatusResult> GetLuaBackendStatusAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default);
        Task<LuaBackendDownloadResult> DownloadLuaBackendAsync(CancellationToken cancellationToken = default);
        Task<OperationOutcome> InstallLuaBackendAsync(LuaBackendInstallRequest request, CancellationToken cancellationToken = default);
        Task<OperationOutcome> ConfigureLuaBackendAsync(LuaBackendConfigureRequest request, CancellationToken cancellationToken = default);
        Task<OperationOutcome> RemoveLuaBackendAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default);
        Task<SteamAppIdStatusResult> GetSteamAppIdStatusAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default);
        Task<OperationOutcome> InstallSteamAppIdAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default);
        Task<OperationOutcome> RemoveSteamAppIdAsync(CollectionOperationRequest request, CancellationToken cancellationToken = default);
        Task<ProtonLaunchOptionsInspectionResult> InspectProtonLaunchOptionsAsync(ProtonLaunchOptionsRequest request, CancellationToken cancellationToken = default);
        Task<ProtonLaunchOptionsUpdateResult> UpdateProtonLaunchOptionsAsync(ProtonLaunchOptionsRequest request, CancellationToken cancellationToken = default);
    }

    public interface IGameDataExtractionOperations
    {
        Task<GameDataExtractionResult> ExtractAsync(
            GameDataExtractionRequest request,
            IProgress<GameDataExtractionProgress> progress = null,
            CancellationToken cancellationToken = default);
    }

    public interface ISetupWizardFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        IEnumerable<string> EnumerateFiles(string path, string pattern);
        string ReadAllText(string path);
        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
        void WriteAllText(string path, string content);
        Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);
        void CreateDirectory(string path);
        void CopyFile(string source, string destination, bool overwrite);
        void MoveFile(string source, string destination, bool overwrite);
        void DeleteFile(string path);
        void DeleteDirectory(string path, bool recursive);
        Stream OpenRead(string path);
    }

    public interface ILuaBackendReleaseSource
    {
        Task<LuaBackendRelease> GetLatestAsync(CancellationToken cancellationToken);
        Task DownloadAsync(Uri uri, string destinationPath, CancellationToken cancellationToken);
    }

    public sealed record LuaBackendRelease(string Version, Uri DownloadUri, string AssetName);

    public interface IProtonConfigRepository
    {
        bool IsSteamRunning { get; }
        IReadOnlyList<string> GetConfigurationFiles();
        string Read(string path);
        void BackupAndWrite(string path, string content);
    }
}
