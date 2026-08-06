using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class GameWorkflowService : IGameWorkflowService
    {
        private static readonly string[] Executables =
        [
            "KINGDOM HEARTS II FINAL MIX.exe", "KINGDOM HEARTS FINAL MIX.exe",
            "KINGDOM HEARTS Birth by Sleep FINAL MIX.exe", "KINGDOM HEARTS Re_Chain of Memories.exe",
            "KINGDOM HEARTS Dream Drop Distance.exe"
        ];

        private readonly IShellProcessLauncher _shell;

        public GameWorkflowService(IShellProcessLauncher shell) =>
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        public GameAvailability GetAvailability() => new(
            Directory.Exists(ConfigurationService.PcReleaseLocation),
            Directory.Exists(ConfigurationService.PcReleaseLocationKH3D),
            !string.IsNullOrEmpty(ConfigurationService.IsoLocationKH2),
            !string.IsNullOrEmpty(ConfigurationService.IsoLocationKH1),
            !string.IsNullOrEmpty(ConfigurationService.IsoLocationRecom));

        public string GetPreferredGameId(string gameId)
        {
            if (ConfigurationService.GameEdition != ViewModels.SetupWizardViewModel.PCSX2)
                return gameId;
            if (IsValidIso(gameId))
                return gameId;
            if (IsValidIso("kh2")) return "kh2";
            if (IsValidIso("kh1")) return "kh1";
            if (IsValidIso("Recom")) return "Recom";
            return "kh2";
        }

        public async Task<GameStartResult> StartAsync(GameStartRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            var gameId = request.GameId;
            switch (ConfigurationService.GameEdition)
            {
                case ViewModels.SetupWizardViewModel.OpenKHGameEngine:
                    return StartProcess(new ProcessStartInfo
                    {
                        FileName = ConfigurationService.OpenKhGameEngineLocation,
                        WorkingDirectory = Path.GetDirectoryName(ConfigurationService.OpenKhGameEngineLocation),
                        Arguments = $"--data \"{ConfigurationService.GameDataLocation}\" --modpath \"{ConfigurationService.GameModPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    }, false);
                case ViewModels.SetupWizardViewModel.PCSX2:
                    if (!PlatformCapabilities.SupportsPcsx2Injection)
                        return Error("Launching PCSX2 with live mod patching is only supported on Windows for now.");
                    var iso = GetIso(gameId);
                    if (string.IsNullOrEmpty(iso))
                        return Error("Unable to locate the executable. Please run the Wizard by going to the Settings menu.");
                    return StartProcess(new ProcessStartInfo
                    {
                        FileName = ConfigurationService.Pcsx2Location,
                        WorkingDirectory = Path.GetDirectoryName(ConfigurationService.Pcsx2Location),
                        Arguments = $"\"{iso}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    }, true);
                case ViewModels.SetupWizardViewModel.PC:
                    return await StartPcAsync(gameId, cancellationToken).ConfigureAwait(false);
                default:
                    return Error("A game edition has not been configured.");
            }
        }

        private async Task<GameStartResult> StartPcAsync(string gameId, CancellationToken cancellationToken)
        {
            var isKh3d = gameId == "kh3d";
            var root = isKh3d ? ConfigurationService.PcReleaseLocationKH3D : ConfigurationService.PcReleaseLocation;
            if (!Directory.Exists(root))
                return Error(isKh3d
                    ? "Unable to locate KINGDOM HEARTS HD 2.8 Final Chapter Prologue install. Please re-run the setup wizard and confirm it is correct."
                    : "Unable to locate KINGDOM HEARTS HD 1.5+2.5 ReMIX install. Please re-run the setup wizard and confirm it is correct.");

            EnsureLaunchSettings(root, gameId);
            if (ConfigurationService.PCVersion == "EGS")
            {
                if (!PlatformCapabilities.SupportsEpicGamesStore)
                    return Error("There is no Epic Games Store client on this platform, so the game cannot be launched through it. Use the Steam version instead, or launch the game manually.");
                var uri = isKh3d
                    ? "com.epicgames.launcher://apps/c8ff067c1c984cd7ab1998e8a9afc8b6%3Aaa743b9f52e84930b0ba1b701951e927%3Ad1a8f7c478d4439b8c60a5808715dc05?action=launch&silent=true"
                    : "com.epicgames.launcher://apps/4158b699dd70447a981fee752d970a3e%3A5aac304f0e8948268ddfd404334dbdc7%3A68c214c58f694ae88c2dab6f209b43e4?action=launch&silent=true";
                await _shell.LaunchAsync(new ShellProcessRequest(uri, UseShellExecute: true), cancellationToken).ConfigureAwait(false);
                return new GameStartResult(null, CloseApplication: true);
            }

            var useSteam = isKh3d ? !ConfigurationService.SteamAPITrick28 : !ConfigurationService.SteamAPITrick1525;
            if (ConfigurationService.PCVersion == "Steam" && (useSteam || !OperatingSystem.IsWindows()))
            {
                await _shell.LaunchAsync(new ShellProcessRequest(isKh3d ? "steam://rungameid/2552440" : "steam://rungameid/2552430",
                    UseShellExecute: true), cancellationToken).ConfigureAwait(false);
                return new GameStartResult(null, CloseApplication: true);
            }
            if (!OperatingSystem.IsWindows())
                return Error("Launching the game executable directly is not supported on Linux. Select the Steam launcher in the setup wizard so the game can be started through the Steam client.");

            var index = gameId switch { "kh1" => 1, "bbs" => 2, "Recom" => 3, "kh3d" => 4, _ => 0 };
            var executable = Path.Combine(root, Executables[index]);
            if (!File.Exists(executable))
                return Error("Unable to locate game executable. Please make sure your Kingdom Hearts executable is correctly named and in the correct folder.");
            await _shell.LaunchAsync(new ShellProcessRequest(executable, WorkingDirectory: root), cancellationToken).ConfigureAwait(false);
            return new GameStartResult(null, CloseApplication: true);
        }

        public void UpdatePanaceaSettings(PanaceaSettings settings)
        {
            if (!ConfigurationService.PanaceaInstalled)
                return;
            var root = settings.GameId == "kh3d" ? ConfigurationService.PcReleaseLocationKH3D : ConfigurationService.PcReleaseLocation;
            if (string.IsNullOrEmpty(root))
                return;
            var path = Path.Combine(root, "panacea_settings.txt");
            var devPath = File.Exists(path) ? Array.Find(File.ReadAllLines(path), x => x.Contains("dev_path")) : null;
            var text = $"mod_path={WinePathUtil.ToGamePath(Path.GetFullPath(Path.Combine(ConfigurationService.GameModPath, "..")))}\r\n";
            if (devPath != null) text += devPath;
            text += $"\r\nshow_console={settings.ShowConsole}\r\ndebug_log={settings.DebugLog}\r\nsound_debug={settings.SoundDebug}\r\nenable_cache={settings.EnableCache}\r\nquick_menu={settings.QuickMenu}";
            File.WriteAllText(path, text);
        }

        public Task<PanaceaUpdateResult> ApplyPendingPanaceaUpdateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ConfigurationService.PanaceaInstalled || !ConfigurationService.Updated)
                return Task.FromResult(new PanaceaUpdateResult(false, false));
            try
            {
                var source = Path.Combine(AppContext.BaseDirectory, "OpenKH.Panacea.dll");
                InstallPanacea(source, ConfigurationService.PcReleaseLocation);
                InstallPanacea(source, ConfigurationService.PcReleaseLocationKH3D);
                ConfigurationService.Updated = false;
                return Task.FromResult(new PanaceaUpdateResult(true, true));
            }
            catch
            {
                ConfigurationService.Updated = false;
                return Task.FromResult(new PanaceaUpdateResult(true, false));
            }
        }

        private static void InstallPanacea(string source, string root)
        {
            if (string.IsNullOrEmpty(root)) return;
            var primary = Path.Combine(root, "DBGHELP.dll");
            var alternate = Path.Combine(root, "version.dll");
            var usePrimary = OperatingSystem.IsWindows() && Process.GetProcessesByName("winlogon").Length > 0;
            File.Copy(source, usePrimary ? primary : alternate, true);
            File.Delete(usePrimary ? alternate : primary);
        }

        private static void EnsureLaunchSettings(string root, string gameId)
        {
            if (!ConfigurationService.PanaceaInstalled) return;
            var path = Path.Combine(root, "panacea_settings.txt");
            if (!File.Exists(path))
                File.WriteAllLines(path,
                [
                    $"mod_path={WinePathUtil.ToGamePath(Path.GetFullPath(Path.Combine(ConfigurationService.GameModPath, "..")))}",
                    "show_console=False"
                ]);
            File.AppendAllText(path, "\nquick_launch=" + gameId);
        }

        private GameStartResult StartProcess(ProcessStartInfo info, bool inject)
        {
            if (!File.Exists(info.FileName))
                return Error("Unable to locate the executable. Please run the Wizard by going to the Settings menu.");
            var session = new ProcessGameSession(info, inject);
            session.Start();
            return new GameStartResult(session);
        }

        private static GameStartResult Error(string message) => new(null, message);
        private static string GetIso(string gameId) => gameId switch
        {
            "kh1" => ConfigurationService.IsoLocationKH1,
            "Recom" => ConfigurationService.IsoLocationRecom,
            _ => ConfigurationService.IsoLocationKH2
        };
        private static bool IsValidIso(string gameId)
        {
            var iso = GetIso(gameId);
            return !string.IsNullOrEmpty(iso) && GameService.DetectGameId(iso)?.Id == gameId;
        }
    }

    internal sealed class ProcessGameSession : IGameSession, IDebugging
    {
        private readonly Process _process;
        private readonly Pcsx2Injector _injector;
        private int _stopped;
        private int _disposed;

        public ProcessGameSession(ProcessStartInfo startInfo, bool inject)
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (inject) _injector = new Pcsx2Injector(new OperationDispatcher());
        }

        public bool IsRunning => Volatile.Read(ref _stopped) == 0 && !_process.HasExited;
        public event EventHandler<GameOutputEventArgs> OutputReceived;
        public event EventHandler Exited;

        public void Start()
        {
            _process.OutputDataReceived += OnOutput;
            _process.ErrorDataReceived += OnOutput;
            _process.Exited += OnExited;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            if (_injector != null)
            {
                _injector.RegionId = ConfigurationService.RegionId;
                _injector.Region = Kh2.Constants.Regions[_injector.RegionId];
                _injector.Language = Kh2.Constants.Languages[_injector.RegionId];
                _injector.Run(_process, this);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            if (_injector != null) await _injector.StopAsync().ConfigureAwait(false);
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.HasExited) _process.Kill(true);
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _process.OutputDataReceived -= OnOutput;
            _process.ErrorDataReceived -= OnOutput;
            _process.Exited -= OnExited;
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            _process.Dispose();
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && Volatile.Read(ref _disposed) == 0)
                OutputReceived?.Invoke(this, new GameOutputEventArgs(e.Data));
        }

        private void OnExited(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0 && Volatile.Read(ref _disposed) == 0)
                Exited?.Invoke(this, EventArgs.Empty);
        }

        public void HideDebugger() { }
        public void Log(long ms, string tag, string str)
        {
            if (Volatile.Read(ref _disposed) == 0)
                OutputReceived?.Invoke(this, new GameOutputEventArgs(str));
        }
    }
}
