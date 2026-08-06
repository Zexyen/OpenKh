using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.ViewModels
{
    public sealed record SetupWizardDependencies(
        IUiDispatcher Dispatcher,
        IMessageDialogService Messages,
        IFilePickerService Files,
        IGameInstallDiscoveryService Discovery,
        ISetupWizardModLoaderService ModLoader,
        IGameDataExtractionOperations Extraction);

    public class SetupWizardViewModel : ObservableObject, IDisposable
    {
        private const string PanaceaDllName = "OpenKH.Panacea.dll";
        private static readonly IReadOnlyList<FilePickerFilter> IsoFilter =
            new[] { new FilePickerFilter("PlayStation 2 game ISO", new[] { "*.iso" }) };
        private static readonly IReadOnlyList<FilePickerFilter> OpenKhGameFilter =
            new[] { new FilePickerFilter("OpenKH Game Engine Executable", new[] { "*Game.exe" }) };
        private static readonly IReadOnlyList<FilePickerFilter> Pcsx2Filter =
            new[] { new FilePickerFilter("PCSX2 Emulator", new[] { "*.exe" }) };

        public const int OpenKHGameEngine = 0;
        public const int PCSX2 = 1;
        public const int PC = 2;

        private readonly SetupWizardDependencies _dependencies;
        private CancellationTokenSource _lifetime = new();
        private readonly List<string> _luaScriptPaths = new();
        private int _gameEdition = ConfigurationService.GameEdition;
        private string _isoLocation;
        private string _isoLocationKH2 = ConfigurationService.IsoLocationKH2;
        private string _isoLocationKH1 = ConfigurationService.IsoLocationKH1;
        private string _isoLocationRecom = ConfigurationService.IsoLocationRecom;
        private string _openKhGameEngineLocation = ConfigurationService.OpenKhGameEngineLocation;
        private string _pcsx2Location = ConfigurationService.Pcsx2Location;
        private string _pcReleaseLocation = ConfigurationService.PcReleaseLocation;
        private string _pcReleaseLocationKH3D = ConfigurationService.PcReleaseLocationKH3D;
        private string _pcReleaseLanguage = ConfigurationService.PcReleaseLanguage;
        private string _gameDataLocation = ConfigurationService.GameDataLocation;
        private int _gameCollection;
        private bool _overrideGameDataFound;
        private bool _isBusy;
        private bool _isNotExtracting = true;
        private bool _isLuaBackendInstalled;
        private bool _isSteamApiFileInstalled;
        private bool _isLastPanaceaVersionInstalled;
        private float _extractionProgress;
        private bool _disposed;

        public SetupWizardViewModel(SetupWizardDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            ValidateIsoLocations();

            SelectIsoCommand = Command(SelectIsoAsync);
            SelectOpenKhGameEngineCommand = Command(SelectOpenKhGameEngineAsync);
            SelectPcsx2Command = Command(SelectPcsx2Async);
            SelectPcReleaseCommand = Command(_ => SelectFolderAsync(path => PcReleaseLocation = path, "Select the 1.5+2.5 installation"));
            SelectPcReleaseKH3DCommand = Command(_ => SelectFolderAsync(path => PcReleaseLocationKH3D = path, "Select the 2.8 installation"));
            SelectGameDataLocationCommand = Command(_ => SelectFolderAsync(path => GameDataLocation = path, "Select the game data folder"));
            ExtractGameDataCommand = Command(_ => ExtractGameDataAsync());
            DetectInstallsCommand = Command(_ => DetectInstallsAsync());
            InstallPanaceaCommand = Command(_ => InstallPanaceaAsync());
            RemovePanaceaCommand = Command(_ => RemovePanaceaAsync());
            InstallLuaBackendCommand = Command(InstallOrConfigureLuaBackendAsync);
            RemoveLuaBackendCommand = Command(_ => RemoveLuaBackendAsync());
            InstallSteamAPIFile = Command(_ => InstallSteamAppIdAsync());
            RemoveSteamAPIFile = Command(_ => RemoveSteamAppIdAsync());
        }

        public string Title => $"Set-up wizard | {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "OpenKH Mods Manager"}";
        public SetupWizardRouteState RouteState => new((WizardGameEdition)GameEdition, GetPcLauncher(),
            !string.IsNullOrEmpty(IsoLocationKH2), OperatingSystem.IsWindows());

        public AsyncCommand SelectIsoCommand { get; }
        public AsyncCommand SelectOpenKhGameEngineCommand { get; }
        public AsyncCommand SelectPcsx2Command { get; }
        public AsyncCommand SelectPcReleaseCommand { get; }
        public AsyncCommand SelectPcReleaseKH3DCommand { get; }
        public AsyncCommand SelectGameDataLocationCommand { get; }
        public AsyncCommand ExtractGameDataCommand { get; }
        public AsyncCommand DetectInstallsCommand { get; }
        public AsyncCommand InstallPanaceaCommand { get; }
        public AsyncCommand RemovePanaceaCommand { get; }
        public AsyncCommand InstallLuaBackendCommand { get; }
        public AsyncCommand RemoveLuaBackendCommand { get; }
        public AsyncCommand InstallSteamAPIFile { get; }
        public AsyncCommand RemoveSteamAPIFile { get; }

        private IEnumerable<AsyncCommand> Commands
        {
            get
            {
                yield return SelectIsoCommand; yield return SelectOpenKhGameEngineCommand; yield return SelectPcsx2Command;
                yield return SelectPcReleaseCommand; yield return SelectPcReleaseKH3DCommand; yield return SelectGameDataLocationCommand;
                yield return ExtractGameDataCommand; yield return DetectInstallsCommand; yield return InstallPanaceaCommand;
                yield return RemovePanaceaCommand; yield return InstallLuaBackendCommand; yield return RemoveLuaBackendCommand;
                yield return InstallSteamAPIFile; yield return RemoveSteamAPIFile;
            }
        }

        private AsyncCommand Command(Func<object, Task> execute) => new(async parameter =>
        {
            if (_disposed) return;
            IsBusy = true;
            try { await execute(parameter); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            finally { IsBusy = false; }
        }, _ => !IsBusy && !_disposed);

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                foreach (var command in Commands)
                    command?.RaiseCanExecuteChanged();
            }
        }

        public string GameId { get; private set; }
        public string GameName { get; private set; }
        public string IsoLocation
        {
            get => _isoLocation;
            set
            {
                if (!SetProperty(ref _isoLocation, value)) return;
                var game = File.Exists(value) ? GameService.DetectGameId(value) : null;
                GameId = game?.Id;
                GameName = game?.Name;
                Notify(nameof(GameId), nameof(GameName), nameof(IsIsoSelected), nameof(IsGameRecognized),
                    nameof(IsGameRecognizedVisible), nameof(IsGameNotRecognizedVisible));
            }
        }

        public string IsoLocationKH2
        {
            get => _isoLocationKH2;
            set { if (SetProperty(ref _isoLocationKH2, value)) { ConfigurationService.IsoLocationKH2 = value; NotifyIsoState(nameof(IsKh2RecognizedVisible), true); } }
        }
        public string IsoLocationKH1
        {
            get => _isoLocationKH1;
            set { if (SetProperty(ref _isoLocationKH1, value)) { ConfigurationService.IsoLocationKH1 = value; NotifyIsoState(nameof(IsKh1RecognizedVisible), false); } }
        }
        public string IsoLocationRecom
        {
            get => _isoLocationRecom;
            set { if (SetProperty(ref _isoLocationRecom, value)) { ConfigurationService.IsoLocationRecom = value; NotifyIsoState(nameof(IsRecomRecognizedVisible), false); } }
        }

        public bool IsIsoSelected => !string.IsNullOrEmpty(IsoLocation) && File.Exists(IsoLocation);
        public bool IsGameRecognized => IsIsoSelected && GameId != null;
        public bool IsGameRecognizedVisible => IsGameRecognized;
        public bool IsGameNotRecognizedVisible => IsIsoSelected && GameId == null;
        public bool IsKh1RecognizedVisible => !string.IsNullOrEmpty(IsoLocationKH1);
        public bool IsKh2RecognizedVisible => !string.IsNullOrEmpty(IsoLocationKH2);
        public bool IsRecomRecognizedVisible => !string.IsNullOrEmpty(IsoLocationRecom);
        public bool IsOpenKhGameEngineVisible => ConfigurationService.DevView;

        public int GameEdition
        {
            get => _gameEdition;
            set
            {
                if (!SetProperty(ref _gameEdition, value)) return;
                ConfigurationService.GameEdition = value;
                Notify(nameof(RouteState), nameof(IsGameSelected), nameof(IsOpenKhGameEngineConfigVisible),
                    nameof(IsPcsx2ConfigVisible), nameof(IsPcReleaseConfigVisible), nameof(IsBothPcReleasesSelected),
                    nameof(IsPcRelease1525Selected), nameof(IsPcRelease28Selected));
            }
        }

        public bool IsGameSelected => GameEdition switch
        {
            OpenKHGameEngine => File.Exists(OpenKhGameEngineLocation),
            PCSX2 => File.Exists(Pcsx2Location),
            PC => IsPcInstall(PcReleaseLocation) || IsPcInstall(PcReleaseLocationKH3D),
            _ => false
        };
        public bool IsOpenKhGameEngineConfigVisible => GameEdition == OpenKHGameEngine;
        public bool IsPcsx2ConfigVisible => GameEdition == PCSX2;
        public bool IsPcReleaseConfigVisible => GameEdition == PC;

        public string OpenKhGameEngineLocation
        {
            get => _openKhGameEngineLocation;
            set { if (SetProperty(ref _openKhGameEngineLocation, value)) { ConfigurationService.OpenKhGameEngineLocation = value; OnPropertyChanged(nameof(IsGameSelected)); } }
        }
        public string Pcsx2Location
        {
            get => _pcsx2Location;
            set { if (SetProperty(ref _pcsx2Location, value)) { ConfigurationService.Pcsx2Location = value; OnPropertyChanged(nameof(IsGameSelected)); } }
        }
        public string PcReleaseLocation
        {
            get => _pcReleaseLocation;
            set { if (SetProperty(ref _pcReleaseLocation, value)) { ConfigurationService.PcReleaseLocation = value; NotifyPcState(); } }
        }
        public string PcReleaseLocationKH3D
        {
            get => _pcReleaseLocationKH3D;
            set { if (SetProperty(ref _pcReleaseLocationKH3D, value)) { ConfigurationService.PcReleaseLocationKH3D = value; NotifyPcState(); } }
        }

        public string PcReleaseSelections => IsPcInstall(PcReleaseLocation) && IsPcInstall(PcReleaseLocationKH3D) ? "both" :
            IsPcInstall(PcReleaseLocation) ? "1.5+2.5" : IsPcInstall(PcReleaseLocationKH3D) ? "2.8" : "";
        public bool IsBothPcReleasesSelected => PcReleaseSelections == "both";
        public bool IsPcRelease1525Selected => PcReleaseSelections == "1.5+2.5";
        public bool IsPcRelease28Selected => PcReleaseSelections == "2.8";
        public bool IsInstallForPc1525Visible => GameCollection == 0 && (IsBothPcReleasesSelected || IsPcRelease1525Selected);
        public bool IsInstallForPc28Visible => GameCollection == 1 && (IsBothPcReleasesSelected || IsPcRelease28Selected);

        public int GameCollection
        {
            get => _gameCollection;
            set { if (SetProperty(ref _gameCollection, value)) NotifyLoaderState(); }
        }

        public int PCReleaseLanguage
        {
            get => _pcReleaseLanguage == "jp" ? 1 : 0;
            set { _pcReleaseLanguage = value == 1 ? "jp" : "en"; ConfigurationService.PcReleaseLanguage = _pcReleaseLanguage; OnPropertyChanged(); }
        }
        public int LaunchOption
        {
            get => GetPcLauncher() switch { PcLauncher.Steam => 1, PcLauncher.Other => 2, _ => 0 };
            set
            {
                ConfigurationService.PCVersion = value switch { 1 => "Steam", 2 => "Other", _ => "EGS" };
                Notify(nameof(LaunchOption), nameof(RouteState));
            }
        }

        public bool Extractkh1 { get => ConfigurationService.Extractkh1; set { ConfigurationService.Extractkh1 = value; OnPropertyChanged(); } }
        public bool Extractkh2 { get => ConfigurationService.Extractkh2; set { ConfigurationService.Extractkh2 = value; OnPropertyChanged(); } }
        public bool Extractbbs { get => ConfigurationService.Extractbbs; set { ConfigurationService.Extractbbs = value; OnPropertyChanged(); } }
        public bool Extractrecom { get => ConfigurationService.Extractrecom; set { ConfigurationService.Extractrecom = value; OnPropertyChanged(); } }
        public bool Extractkh3d { get => ConfigurationService.Extractkh3d; set { ConfigurationService.Extractkh3d = value; OnPropertyChanged(); } }
        public bool SkipRemastered { get => ConfigurationService.SkipRemastered; set { ConfigurationService.SkipRemastered = value; OnPropertyChanged(); } }
        public int RegionId { get => ConfigurationService.RegionId; set { ConfigurationService.RegionId = value; OnPropertyChanged(); } }

        public bool LuaConfigkh1 { get => HasLua("kh1"); set => SetLua("kh1", value); }
        public bool LuaConfigkh2 { get => HasLua("kh2"); set => SetLua("kh2", value); }
        public bool LuaConfigbbs { get => HasLua("bbs"); set => SetLua("bbs", value); }
        public bool LuaConfigrecom { get => HasLua("Recom"); set => SetLua("Recom", value); }
        public bool LuaConfigkh3d { get => HasLua("kh3d"); set => SetLua("kh3d", value); }

        public bool OverrideGameDataFound
        {
            get => _overrideGameDataFound;
            set { if (SetProperty(ref _overrideGameDataFound, value)) NotifyGameDataState(); }
        }
        public string GameDataLocation
        {
            get => _gameDataLocation;
            set { if (SetProperty(ref _gameDataLocation, value)) { ConfigurationService.GameDataLocation = value; NotifyGameDataState(); } }
        }
        public bool IsNotExtracting { get => _isNotExtracting; private set { if (SetProperty(ref _isNotExtracting, value)) NotifyGameDataState(); } }
        public float ExtractionProgress
        {
            get => _extractionProgress;
            private set { if (SetProperty(ref _extractionProgress, value)) OnPropertyChanged(nameof(IsExtractionCompleteVisible)); }
        }
        public bool IsGameDataFound => IsNotExtracting && ((GameEdition == PCSX2 &&
            (GameService.FolderContainsUniqueFile("kh2", Path.Combine(GameDataLocation, "kh2")) ||
             GameService.FolderContainsUniqueFile("kh1", Path.Combine(GameDataLocation, "kh1")) ||
             GameService.FolderContainsUniqueFile("Recom", Path.Combine(GameDataLocation, "Recom")))) ||
            (GameEdition == PC && (GameService.FolderContainsUniqueFile("kh2", Path.Combine(GameDataLocation, "kh2")) ||
             GameService.FolderContainsUniqueFile("kh1", Path.Combine(GameDataLocation, "kh1")) ||
             Directory.Exists(Path.Combine(GameDataLocation, "bbs", "message")) ||
             Directory.Exists(Path.Combine(GameDataLocation, "Recom", "SYS")) ||
             Directory.Exists(Path.Combine(GameDataLocation, "kh3d", "setdata")))) || OverrideGameDataFound);
        public bool IsGameDataNotFoundVisible => !IsGameDataFound;
        public bool IsGameDataFoundVisible => IsGameDataFound;
        public bool IsProgressBarVisible => !IsNotExtracting;
        public bool IsExtractionCompleteVisible => ExtractionProgress == 1f;

        public bool IsLuaBackendInstalled { get => _isLuaBackendInstalled; private set => SetProperty(ref _isLuaBackendInstalled, value); }
        public bool IsLuaBackendFoundVisible => IsLuaBackendInstalled;
        public bool IsLuaBackendNotFoundVisible => !IsLuaBackendInstalled;
        public bool IsSteamAPIFileInstalled { get => _isSteamApiFileInstalled; private set => SetProperty(ref _isSteamApiFileInstalled, value); }
        public bool IsSteamApiFileFoundVisible => IsSteamAPIFileInstalled;
        public bool IsSteamApiFileNotFoundVisible => !IsSteamAPIFileInstalled;
        public bool PanaceaInstalled { get; private set; }
        public bool IsLastPanaceaVersionInstalled { get => _isLastPanaceaVersionInstalled; private set => SetProperty(ref _isLastPanaceaVersionInstalled, value); }
        public bool IsPanaceaNotInstalledVisible => !IsLastPanaceaVersionInstalled;
        public bool IsPanaceaInstalledVisible => IsLastPanaceaVersionInstalled;

        public async Task InitializeAsync()
        {
            await RefreshLoaderStatusAsync();
        }

        private async Task SelectIsoAsync(object parameter)
        {
            var files = await _dependencies.Files.OpenFilesAsync(new OpenFileRequest("Select a game ISO", false, null, IsoFilter), _lifetime.Token);
            var file = files?.FirstOrDefault();
            if (string.IsNullOrEmpty(file)) return;
            IsoLocation = file;
            switch (parameter as string)
            {
                case "kh2": IsoLocationKH2 = file; GameName ??= "Kingdom Hearts II"; break;
                case "kh1": IsoLocationKH1 = file; GameName ??= "Kingdom Hearts I"; break;
                case "Recom": IsoLocationRecom = file; GameName ??= "Kingdom Hearts Re:Chain of Memories"; break;
            }
            OnPropertyChanged(nameof(GameName));
        }

        private async Task SelectOpenKhGameEngineAsync(object _)
        {
            var files = await _dependencies.Files.OpenFilesAsync(new OpenFileRequest("Select OpenKH Game Engine", false, null, OpenKhGameFilter), _lifetime.Token);
            if (files?.FirstOrDefault() is string file) OpenKhGameEngineLocation = file;
        }
        private async Task SelectPcsx2Async(object _)
        {
            var files = await _dependencies.Files.OpenFilesAsync(new OpenFileRequest("Select PCSX2", false, null, Pcsx2Filter), _lifetime.Token);
            if (files?.FirstOrDefault() is string file) Pcsx2Location = file;
        }
        private async Task SelectFolderAsync(Action<string> selected, string title)
        {
            var folder = await _dependencies.Files.OpenFolderAsync(new OpenFolderRequest(title), _lifetime.Token);
            if (!string.IsNullOrEmpty(folder)) { selected(folder); await RefreshLoaderStatusAsync(); }
        }

        private async Task DetectInstallsAsync()
        {
            var result = await _dependencies.Discovery.DiscoverAsync(new GameInstallDiscoveryRequest(GetPcLauncher()), _lifetime.Token);
            foreach (var install in result.Installs ?? Array.Empty<DiscoveredGameInstall>())
                if (install.Collection == PcGameCollection.KingdomHearts1525) PcReleaseLocation = install.InstallPath; else PcReleaseLocationKH3D = install.InstallPath;
            var title = result.Outcome.Succeeded ? "Success" : result.Outcome.FailureKind == OperationFailureKind.Unsupported ? "Unsupported" : "Failure";
            var message = result.Outcome.Succeeded ? DiscoveryMessage(result.Installs) :
                result.Outcome.Message + "\nPlease Manually Browse To Your Game Install Directory";
            await ShowAsync(message, title, result.Outcome.Succeeded ? MessageDialogKind.Information : MessageDialogKind.Warning);
            await RefreshLoaderStatusAsync();
        }

        private async Task InstallPanaceaAsync()
        {
            var request = new PanaceaInstallRequest(CurrentCollection, CurrentInstallPath, AppContext.BaseDirectory,
                Path.GetFullPath(Path.Combine(ConfigurationService.GameModPath, "..")), Process.GetProcessesByName("winlogon").Length > 0);
            var outcome = await _dependencies.ModLoader.InstallPanaceaAsync(request, _lifetime.Token);
            if (!outcome.Succeeded) await ShowOutcomeAsync(outcome, "Missing Panacea files");
            await RefreshLoaderStatusAsync();
            if (outcome.Succeeded) await OfferSteamLaunchOptionsAsync();
        }
        private async Task RemovePanaceaAsync()
        {
            var outcome = await _dependencies.ModLoader.RemovePanaceaAsync(CurrentCollectionRequest, _lifetime.Token);
            if (!outcome.Succeeded) await ShowOutcomeAsync(outcome, "Error");
            await RefreshLoaderStatusAsync();
        }
        private async Task InstallOrConfigureLuaBackendAsync(object installed)
        {
            var configureOnly = Convert.ToBoolean(installed);
            OperationOutcome outcome;
            if (configureOnly)
            {
                var replace = false;
                var first = await _dependencies.ModLoader.ConfigureLuaBackendAsync(CreateLuaConfigureRequest(false), _lifetime.Token);
                if (first.Succeeded && !first.Changed && SelectedScriptGames.Any())
                {
                    replace = await ConfirmAsync("Your Lua Backend may already be configured to run scripts from another OpenKH Mods Manager. Do you want to change it to this installation?", "Warning");
                }
                outcome = replace ? await _dependencies.ModLoader.ConfigureLuaBackendAsync(CreateLuaConfigureRequest(true), _lifetime.Token) : first;
            }
            else
            {
                var download = await _dependencies.ModLoader.DownloadLuaBackendAsync(_lifetime.Token);
                outcome = download.Outcome;
                if (outcome.Succeeded)
                    outcome = await _dependencies.ModLoader.InstallLuaBackendAsync(new LuaBackendInstallRequest(CurrentCollection,
                        CurrentInstallPath, download.Download.ArchivePath, ModRootPath, GetPcLauncher(), SelectedScriptGames), _lifetime.Token);
            }
            if (!outcome.Succeeded) await ShowOutcomeAsync(outcome, "Run error");
            await RefreshLoaderStatusAsync();
            if (outcome.Succeeded) await OfferSteamLaunchOptionsAsync();
        }
        private async Task RemoveLuaBackendAsync()
        {
            var outcome = await _dependencies.ModLoader.RemoveLuaBackendAsync(CurrentCollectionRequest, _lifetime.Token);
            if (!outcome.Succeeded) await ShowOutcomeAsync(outcome, "Error");
            await RefreshLoaderStatusAsync();
        }
        private async Task InstallSteamAppIdAsync()
        {
            var outcome = await _dependencies.ModLoader.InstallSteamAppIdAsync(CurrentCollectionRequest, _lifetime.Token);
            if (outcome.Succeeded) SetSteamConfiguration(true); else await ShowOutcomeAsync(outcome, "Error");
            await RefreshLoaderStatusAsync();
        }
        private async Task RemoveSteamAppIdAsync()
        {
            var outcome = await _dependencies.ModLoader.RemoveSteamAppIdAsync(CurrentCollectionRequest, _lifetime.Token);
            if (outcome.Succeeded) SetSteamConfiguration(false); else await ShowOutcomeAsync(outcome, "Error");
            await RefreshLoaderStatusAsync();
        }

        private async Task ExtractGameDataAsync()
        {
            do
            {
                IsNotExtracting = false;
                ExtractionProgress = 0;
                var progress = new ImmediateProgress<GameDataExtractionProgress>(value =>
                {
                    if (_dependencies.Dispatcher.CheckAccess()) ExtractionProgress = value.Fraction;
                    else _dependencies.Dispatcher.Post(() => ExtractionProgress = value.Fraction);
                });
                var outcomes = new List<GameDataExtractionResult>();
                if (GameEdition == PCSX2)
                {
                    if (Extractkh2 && ValidIso(IsoLocationKH2, "kh2")) outcomes.Add(await ExtractIsoAsync(IsoLocationKH2, WizardGameId.KingdomHearts2, progress));
                    if (Extractkh1 && ValidIso(IsoLocationKH1, "kh1")) outcomes.Add(await ExtractIsoAsync(IsoLocationKH1, WizardGameId.KingdomHearts1, progress));
                    if (Extractrecom && ValidIso(IsoLocationRecom, "Recom")) outcomes.Add(await ExtractIsoAsync(IsoLocationRecom, WizardGameId.ReChainOfMemories, progress));
                }
                else if (GameEdition == PC)
                {
                    if (PcReleaseSelections == "1.5+2.5") Extractkh3d = false;
                    if (PcReleaseSelections == "2.8") { Extractkh1 = Extractkh2 = Extractbbs = Extractrecom = false; }
                    outcomes.Add(await _dependencies.Extraction.ExtractAsync(CreatePcExtractionRequest(), progress, _lifetime.Token));
                }

                var failure = outcomes.Select(x => x.Outcome).FirstOrDefault(x => !x.Succeeded);
                if (failure == null) { ExtractionProgress = 1; IsNotExtracting = true; return; }
                IsNotExtracting = true;
                if (failure.FailureKind == OperationFailureKind.Cancelled) return;
                if (failure.FailureKind == OperationFailureKind.InvalidData) { await ShowOutcomeAsync(failure, "Extraction error"); return; }
                if (!await ConfirmAsync(failure.Message + "\n\nWould you like to try again?", "An Exception was Caught!")) return;
            } while (!_lifetime.IsCancellationRequested);
        }

        private Task<GameDataExtractionResult> ExtractIsoAsync(string path, WizardGameId game, IProgress<GameDataExtractionProgress> progress) =>
            _dependencies.Extraction.ExtractAsync(new GameDataExtractionRequest(GameDataExtractionSource.Ps2Iso,
                GameDataLocation, path, game), progress, _lifetime.Token);

        private GameDataExtractionRequest CreatePcExtractionRequest() => new(GameDataExtractionSource.PcRelease,
            GameDataLocation, Pc1525Path: PcReleaseLocation, Pc28Path: PcReleaseLocationKH3D,
            PcLanguageFolder: GetPcLauncher() == PcLauncher.Steam ? "dt" : _pcReleaseLanguage == "jp" ? "jp" : "en",
            ExtractKh1: Extractkh1, ExtractKh2: Extractkh2, ExtractBbs: Extractbbs, ExtractRecom: Extractrecom,
            ExtractKh3d: Extractkh3d, RetryAsync: ex => ConfirmAsync(ex.Message + "\n\nWould you like to retry again?", "An Exception was Caught!"));

        private async Task RefreshLoaderStatusAsync()
        {
            if (_disposed) return;
            var panacea = await _dependencies.ModLoader.GetPanaceaStatusAsync(new PanaceaStatusRequest(CurrentCollection,
                CurrentInstallPath, Path.Combine(AppContext.BaseDirectory, PanaceaDllName)), _lifetime.Token);
            var lua = await _dependencies.ModLoader.GetLuaBackendStatusAsync(CurrentCollectionRequest, _lifetime.Token);
            var steam = await _dependencies.ModLoader.GetSteamAppIdStatusAsync(CurrentCollectionRequest, _lifetime.Token);
            PanaceaInstalled = panacea.IsInstalled;
            IsLastPanaceaVersionInstalled = panacea.IsInstalled;
            ConfigurationService.PanaceaInstalled = panacea.IsInstalled;
            IsLuaBackendInstalled = lua.IsInstalled;
            IsSteamAPIFileInstalled = steam.Exists;
            if (steam.Exists) SetSteamConfiguration(true);
            NotifyLoaderState();
        }

        private async Task OfferSteamLaunchOptionsAsync()
        {
            if (OperatingSystem.IsWindows() || GetPcLauncher() != PcLauncher.Steam) return;
            var request = new ProtonLaunchOptionsRequest(CurrentCollection);
            var inspection = await _dependencies.ModLoader.InspectProtonLaunchOptionsAsync(request, _lifetime.Token);
            if (!inspection.Outcome.Succeeded || inspection.IsConfigured) return;
            var note = inspection.IsSteamRunning ? "\n\nNOTE: Steam appears to be running. Close Steam first, otherwise it will overwrite the change when it exits." : "";
            if (!await ConfirmAsync("For mods to load under Proton, the game's Steam launch options must include:\n\n" +
                SteamService.WineDllOverridesLaunchOptions + "\n\nDo you want Mods Manager to set this automatically?" + note, "Steam launch options")) return;
            var updated = await _dependencies.ModLoader.UpdateProtonLaunchOptionsAsync(request, _lifetime.Token);
            var message = updated.UpdatedCount > 0
                ? "Steam launch options updated. If Steam was running, restart it for the change to take effect."
                : "No Steam configuration with this game was found. Set the launch options manually in Steam:\n" + SteamService.WineDllOverridesLaunchOptions;
            await ShowAsync(message, "Steam launch options", updated.UpdatedCount > 0 ? MessageDialogKind.Information : MessageDialogKind.Warning);
        }

        public void ValidateIsoLocations()
        {
            GameName = null;
            ValidateIso(_isoLocationKH2, "kh2", "Kingdom Hearts II");
            ValidateIso(_isoLocationKH1, "kh1", "Kingdom Hearts I");
            ValidateIso(_isoLocationRecom, "Recom", "Kingdom Hearts Re:Chain of Memories");
            Notify(nameof(GameName), nameof(IsGameNotRecognizedVisible), nameof(IsGameRecognized));
        }
        private void ValidateIso(string path, string id, string name)
        {
            if (!ValidIso(path, id)) { _isoLocation = path; GameId = null; GameName = string.IsNullOrEmpty(GameName) ? name : GameName + " & " + name; }
        }

        public void SetAborted() => Cancel();
        public void Cancel()
        {
            if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cancel();
            _lifetime.Dispose();
            GC.SuppressFinalize(this);
        }

        private PcGameCollection CurrentCollection => GameCollection == 0 ? PcGameCollection.KingdomHearts1525 : PcGameCollection.KingdomHearts28;
        private string CurrentInstallPath => GameCollection == 0 ? PcReleaseLocation : PcReleaseLocationKH3D;
        private CollectionOperationRequest CurrentCollectionRequest => new(CurrentCollection, CurrentInstallPath);
        private string ModRootPath => Path.GetFullPath(Path.Combine(ConfigurationService.GameModPath, ".."));
        private IReadOnlyCollection<WizardGameId> SelectedScriptGames => _luaScriptPaths.Select(ToWizardGame).ToArray();
        private LuaBackendConfigureRequest CreateLuaConfigureRequest(bool replace) => new(CurrentCollection, CurrentInstallPath,
            ModRootPath, GetPcLauncher(), SelectedScriptGames, replace);
        private static PcLauncher GetPcLauncher() => ConfigurationService.PCVersion switch
        { "Steam" => PcLauncher.Steam, "Other" => PcLauncher.Other, _ => PcLauncher.EpicGamesStore };
        private static bool IsPcInstall(string path) => Directory.Exists(path) &&
            (File.Exists(Path.Combine(path, "EOSSDK-Win64-Shipping.dll")) || File.Exists(Path.Combine(path, "steam_api64.dll")));
        private static bool ValidIso(string path, string id) => File.Exists(path) && GameService.DetectGameId(path)?.Id == id;
        private bool HasLua(string id) => _luaScriptPaths.Contains(id);
        private void SetLua(string id, bool value) { if (value && !HasLua(id)) _luaScriptPaths.Add(id); else if (!value) _luaScriptPaths.Remove(id); }
        private static WizardGameId ToWizardGame(string id) => id switch
        { "kh1" => WizardGameId.KingdomHearts1, "kh2" => WizardGameId.KingdomHearts2, "bbs" => WizardGameId.BirthBySleep,
          "Recom" => WizardGameId.ReChainOfMemories, _ => WizardGameId.DreamDropDistance };
        private void SetSteamConfiguration(bool value) { if (GameCollection == 0) ConfigurationService.SteamAPITrick1525 = value; else ConfigurationService.SteamAPITrick28 = value; }
        private Task ShowOutcomeAsync(OperationOutcome outcome, string title) => ShowAsync(outcome.Message ?? "The operation failed.", title, MessageDialogKind.Error);
        private async Task<bool> ConfirmAsync(string message, string title) =>
            await _dependencies.Messages.ShowAsync(new MessageDialogRequest(message, title, MessageDialogKind.Question, MessageDialogButtons.YesNo), _lifetime.Token) == MessageDialogResult.Yes;
        private Task<MessageDialogResult> ShowAsync(string message, string title, MessageDialogKind kind) =>
            _dependencies.Messages.ShowAsync(new MessageDialogRequest(message, title, kind), _lifetime.Token);
        private static string DiscoveryMessage(IReadOnlyList<DiscoveredGameInstall> installs)
        {
            var remix = installs?.Any(x => x.Collection == PcGameCollection.KingdomHearts1525) == true ? "FOUND" : "MISSING";
            var kh28 = installs?.Any(x => x.Collection == PcGameCollection.KingdomHearts28) == true ? "FOUND" : "MISSING";
            return $"Kingdom Hearts HD 1.5+2.5: {remix}\nKingdom Hearts HD 2.8: {kh28}";
        }
        private sealed class ImmediateProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public ImmediateProgress(Action<T> report) => _report = report;
            public void Report(T value) => _report(value);
        }
        private void NotifyIsoState(string visibility, bool route) { Notify(nameof(IsGameDataFound), nameof(IsGameDataFoundVisible), nameof(IsGameDataNotFoundVisible), visibility); if (route) OnPropertyChanged(nameof(RouteState)); }
        private void NotifyPcState() { Notify(nameof(IsGameSelected), nameof(IsGameDataFound), nameof(PcReleaseSelections), nameof(IsBothPcReleasesSelected), nameof(IsPcRelease1525Selected), nameof(IsPcRelease28Selected)); NotifyLoaderState(); }
        private void NotifyGameDataState() => Notify(nameof(IsGameDataFound), nameof(IsGameDataFoundVisible), nameof(IsGameDataNotFoundVisible), nameof(IsProgressBarVisible));
        private void NotifyLoaderState() => Notify(nameof(IsInstallForPc1525Visible), nameof(IsInstallForPc28Visible), nameof(IsLuaBackendInstalled), nameof(IsLuaBackendFoundVisible), nameof(IsLuaBackendNotFoundVisible), nameof(IsSteamAPIFileInstalled), nameof(IsSteamApiFileFoundVisible), nameof(IsSteamApiFileNotFoundVisible), nameof(IsLastPanaceaVersionInstalled), nameof(IsPanaceaInstalledVisible), nameof(IsPanaceaNotInstalledVisible));
        private void Notify(params string[] properties) { foreach (var property in properties) OnPropertyChanged(property); }
    }
}
