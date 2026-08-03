using System;
using System.Collections.Generic;

namespace OpenKh.Tools.ModsManager.Models
{
    public enum WizardGameEdition
    {
        OpenKhGameEngine = 0,
        Pcsx2 = 1,
        Pc = 2
    }

    public enum PcLauncher
    {
        EpicGamesStore = 0,
        Steam = 1,
        Other = 2
    }

    public enum PcGameCollection
    {
        KingdomHearts1525 = 0,
        KingdomHearts28 = 1
    }

    public enum WizardGameId
    {
        KingdomHearts1,
        KingdomHearts2,
        BirthBySleep,
        ReChainOfMemories,
        DreamDropDistance
    }

    public enum OperationFailureKind
    {
        None,
        InvalidRequest,
        NotFound,
        Unsupported,
        Conflict,
        Network,
        FileSystem,
        InvalidData,
        Cancelled,
        Unexpected
    }

    public sealed record OperationOutcome(
        bool Succeeded,
        bool Changed,
        OperationFailureKind FailureKind = OperationFailureKind.None,
        string Message = null)
    {
        public static OperationOutcome Success(bool changed = false, string message = null) =>
            new(true, changed, OperationFailureKind.None, message);

        public static OperationOutcome Failure(OperationFailureKind kind, string message) =>
            new(false, false, kind, message);
    }

    public sealed record GameInstallDiscoveryRequest(
        PcLauncher Launcher,
        string EpicManifestDirectory = null,
        IReadOnlyList<string> SteamAppsCandidates = null);

    public sealed record DiscoveredGameInstall(PcGameCollection Collection, string InstallPath, PcLauncher Launcher);

    public sealed record GameInstallDiscoveryResult(
        OperationOutcome Outcome,
        IReadOnlyList<DiscoveredGameInstall> Installs);

    public sealed record CollectionOperationRequest(PcGameCollection Collection, string InstallPath);

    public sealed record PanaceaStatusRequest(PcGameCollection Collection, string InstallPath, string SourceDllPath);

    public sealed record PanaceaStatusResult(
        OperationOutcome Outcome,
        bool IsInstalled,
        string InstalledDllPath = null);

    public sealed record PanaceaInstallRequest(
        PcGameCollection Collection,
        string InstallPath,
        string SourceDirectory,
        string ModRootPath,
        bool UseDbgHelpName);

    public sealed record LuaBackendStatusResult(OperationOutcome Outcome, bool IsInstalled);

    public sealed record LuaBackendDownload(string ArchivePath, string Version = null);

    public sealed record LuaBackendDownloadResult(OperationOutcome Outcome, LuaBackendDownload Download = null);

    public sealed record LuaBackendInstallRequest(
        PcGameCollection Collection,
        string InstallPath,
        string ArchivePath,
        string ModRootPath,
        PcLauncher Launcher,
        IReadOnlyCollection<WizardGameId> ScriptGames);

    public sealed record LuaBackendConfigureRequest(
        PcGameCollection Collection,
        string InstallPath,
        string ModRootPath,
        PcLauncher Launcher,
        IReadOnlyCollection<WizardGameId> ScriptGames,
        bool ReplaceExistingOpenKhScriptPaths = false);

    public sealed record SteamAppIdStatusResult(
        OperationOutcome Outcome,
        bool Exists,
        bool HasExpectedValue,
        string ActualValue = null);

    public sealed record ProtonLaunchOptionsRequest(PcGameCollection Collection);

    public sealed record ProtonLaunchOptionsInspectionResult(
        OperationOutcome Outcome,
        bool ConfigurationFound,
        bool IsConfigured,
        bool IsSteamRunning,
        int ConfigurationCount);

    public sealed record ProtonLaunchOptionsUpdateResult(
        OperationOutcome Outcome,
        int ConfigurationCount,
        int UpdatedCount,
        bool IsSteamRunning);

    public enum GameDataExtractionSource
    {
        Ps2Iso,
        PcRelease
    }

    public sealed record GameDataExtractionRequest(
        GameDataExtractionSource Source,
        string DestinationPath,
        string IsoPath = null,
        WizardGameId? IsoGame = null,
        string Pc1525Path = null,
        string Pc28Path = null,
        string PcLanguageFolder = "en",
        bool ExtractKh1 = false,
        bool ExtractKh2 = false,
        bool ExtractBbs = false,
        bool ExtractRecom = false,
        bool ExtractKh3d = false,
        Func<Exception, System.Threading.Tasks.Task<bool>> RetryAsync = null);

    public sealed record GameDataExtractionProgress(float Fraction, string Stage = null);

    public sealed record GameDataExtractionResult(OperationOutcome Outcome);
}
