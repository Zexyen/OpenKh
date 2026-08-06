using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenKh.Common;
using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;

namespace OpenKh.Tools.ModsManager.ViewModels;

public sealed record MainViewModelDependencies(
	IProgressDialogService Progress,
	IMessageDialogService Messages,
	IUiDispatcher Dispatcher,
	INavigationService Navigation,
	IBrowserService Browser,
	IApplicationLifetime Lifetime,
	IShellProcessLauncher Processes,
	IDebugLogService DebugLog,
	Func<ModModel, IChangeModEnableState, ModViewModel> ModViewModelFactory,
	IModWorkflowService ModWorkflows = null,
	IPresetService Presets = null,
	IApplicationUpdateChecker UpdateChecker = null,
	IApplicationUpdateExecutor UpdateExecutor = null,
	IGameWorkflowService GameWorkflows = null,
	IGamePatchService GamePatches = null);

public class MainViewModel : ObservableObject, IChangeModEnableState, INavigationContext, IDisposable, IAsyncDisposable
{
	public enum GameIDs
	{
		KH2,
		KH1,
		BBS,
		Recom,
		KH3D
	}

	private static Version _version = Assembly.GetEntryAssembly()?.GetName()?.Version;

	private static string ApplicationName = Assembly.GetEntryAssembly()?.GetName().Name ?? "OpenKh Mods Manager";

	private static string ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty;

	private readonly IProgressDialogService _progressDialogService;

	private readonly IMessageDialogService _messages;

	private readonly IUiDispatcher _dispatcher;

	private readonly INavigationService _navigation;

	private readonly IBrowserService _browser;

	private readonly IApplicationLifetime _lifetime;

	private readonly IShellProcessLauncher _processes;

	private readonly IDebugLogService _debugLog;

	private readonly Func<ModModel, IChangeModEnableState, ModViewModel> _modViewModelFactory;

	private readonly IModWorkflowService _modWorkflows;

	private readonly IPresetService _presets;

	private readonly IApplicationUpdateChecker _updateChecker;

	private readonly IApplicationUpdateExecutor _updateExecutor;

	private readonly IGameWorkflowService _gameWorkflows;

	private readonly IGamePatchService _gamePatches;

	private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();

	private readonly SemaphoreSlim _workflowGate = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _updateFetchGate = new SemaphoreSlim(1, 1);

	private readonly Log.LogDispatch _logHandler;

	private IDebugLogSession _debugSession;

	private bool _disposed;

	private ModViewModel _selectedValue;

	private IGameSession _gameSession;

	private Task _closeTask;

	private bool _isBuilding;

	private bool _isBusy;

	private bool _isRunning;

	private Task _initializationTask;

	private bool _pc;

	private bool _pcsx2;

	private bool _panaceaInstalled;

	private bool _panaceaConsoleEnabled;

	private bool _panaceaDebugLogEnabled;

	private bool _panaceaSoundDebugEnabled;

	private bool _panaceaCacheEnabled;

	private bool _panaceaQuickMenuEnabled;

	private bool _devView;

	private bool _autoUpdateMods = false;

	private string _launchGame = "kh2";

	private List<string> _supportedGames = new List<string> { "kh2", "kh1", "bbs", "Recom", "kh3d" };

	private List<string> _supportedPCSX2Games = new List<string> { "kh2", "kh1", "Recom" };

	private int _wizardVersionNumber = 1;

	private string[] executable = new string[5] { "KINGDOM HEARTS II FINAL MIX.exe", "KINGDOM HEARTS FINAL MIX.exe", "KINGDOM HEARTS Birth by Sleep FINAL MIX.exe", "KINGDOM HEARTS Re_Chain of Memories.exe", "KINGDOM HEARTS Dream Drop Distance.exe" };

	private int launchExecutable = 0;

	public ColorThemeService ColorTheme => ColorThemeService.Instance;

	public string Title => ApplicationName;

	public string CurrentVersion => ApplicationVersion;

	public ObservableCollection<ModViewModel> ModsList { get; set; }

	public ObservableCollection<string> PresetList { get; set; }

	public ICommand ExitCommand { get; set; }

	public ICommand AddModCommand { get; set; }

	public ICommand RemoveModCommand { get; set; }

	public ICommand OpenModFolderCommand { get; set; }

	public ICommand MoveTop { get; set; }

	public ICommand MoveUp { get; set; }

	public ICommand MoveDown { get; set; }

	public ICommand BuildCommand { get; set; }

	public ICommand PatchCommand { get; set; }

	public ICommand RestoreCommand { get; set; }

	public ICommand RunCommand { get; set; }

	public ICommand BuildAndRunCommand { get; set; }

	public ICommand StopRunningInstanceCommand { get; set; }

	public ICommand WizardCommand { get; set; }

	public ICommand OpenPresetMenuCommand { get; private set; }

	public ICommand CheckForModUpdatesCommand { get; private set; }

	public ICommand OpenLinkCommand { get; private set; }

	public ICommand CheckOpenkhUpdateCommand { get; private set; }

	public ICommand YamlGeneratorCommand { get; private set; }

	public ICommand OpenModSearchCommand { get; private set; }

	public ModViewModel SelectedValue
	{
		get
		{
			return _selectedValue;
		}
		set
		{
			_selectedValue = value;
			OnPropertyChanged("SelectedValue");
			OnPropertyChanged("IsModSelected");
			OnPropertyChanged("IsModInfoVisible");
			OnPropertyChanged("IsModUnselectedMessageVisible");
			OnPropertyChanged("MoveUp");
			OnPropertyChanged("MoveDown");
			OnPropertyChanged("AddModCommand");
			OnPropertyChanged("RemoveModCommand");
			OnPropertyChanged("OpenModFolderCommand");
		}
	}

	public bool IsModSelected => SelectedValue != null;

	public bool IsModInfoVisible => IsModSelected;

	public bool IsModUnselectedMessageVisible => !IsModSelected;

	public bool PatchVisible => PC && (!PanaceaInstalled || DevView);

	public bool ModLoader => !PC || PanaceaInstalled;

	public bool notPC => !PC;

	public bool isPC => PC;

	public bool GameSelectInteractable => (PC && _gameWorkflows.GetAvailability().PcInstallExists) || (PCSX2 && MultiEmuGames);

	public bool GameSelectVisible => PC || PCSX2;

	public bool GameSelectKH2 => (PC && _gameWorkflows.GetAvailability().PcInstallExists) || (PCSX2 && _gameWorkflows.GetAvailability().Kh2IsoExists);

	public bool GameSelectKH1 => (PC && _gameWorkflows.GetAvailability().PcInstallExists) || (PCSX2 && _gameWorkflows.GetAvailability().Kh1IsoExists);

	public bool GameSelectBBS => PC && _gameWorkflows.GetAvailability().PcInstallExists;

	public bool GameSelectRecom => (PC && _gameWorkflows.GetAvailability().PcInstallExists) || (PCSX2 && _gameWorkflows.GetAvailability().RecomIsoExists);

	public bool GameSelectKH3D => PC && _gameWorkflows.GetAvailability().Kh3dInstallExists;

	public bool PanaceaSettings => PC && PanaceaInstalled;

	public bool MultiEmuGames => _gameWorkflows.GetAvailability().HasMultipleEmulatorGames;

	public bool PanaceaConsoleEnabled
	{
		get
		{
			return _panaceaConsoleEnabled;
		}
		set
		{
			_panaceaConsoleEnabled = value;
			ConfigurationService.ShowConsole = _panaceaConsoleEnabled;
			if (_panaceaDebugLogEnabled)
			{
				PanaceaDebugLogEnabled = false;
			}
			OnPropertyChanged("PanaceaConsoleEnabled");
			UpdatePanaceaSettings();
		}
	}

	public bool PanaceaDebugLogEnabled
	{
		get
		{
			return _panaceaDebugLogEnabled;
		}
		set
		{
			_panaceaDebugLogEnabled = value;
			ConfigurationService.DebugLog = _panaceaDebugLogEnabled;
			if (_panaceaSoundDebugEnabled)
			{
				PanaceaSoundDebugEnabled = false;
			}
			OnPropertyChanged("PanaceaDebugLogEnabled");
			UpdatePanaceaSettings();
		}
	}

	public bool PanaceaSoundDebugEnabled
	{
		get
		{
			return _panaceaSoundDebugEnabled;
		}
		set
		{
			_panaceaSoundDebugEnabled = value;
			ConfigurationService.SoundDebug = _panaceaSoundDebugEnabled;
			OnPropertyChanged("PanaceaSoundDebugEnabled");
			UpdatePanaceaSettings();
		}
	}

	public bool PanaceaCacheEnabled
	{
		get
		{
			return _panaceaCacheEnabled;
		}
		set
		{
			_panaceaCacheEnabled = value;
			ConfigurationService.EnableCache = _panaceaCacheEnabled;
			UpdatePanaceaSettings();
		}
	}

	public bool PanaceaQuickMenuEnabled
	{
		get
		{
			return _panaceaQuickMenuEnabled;
		}
		set
		{
			_panaceaQuickMenuEnabled = value;
			ConfigurationService.QuickMenu = _panaceaQuickMenuEnabled;
			UpdatePanaceaSettings();
		}
	}

	public bool DevView
	{
		get
		{
			return _devView;
		}
		set
		{
			_devView = value;
			ConfigurationService.DevView = DevView;
			OnPropertyChanged("PatchVisible");
		}
	}

	public bool AutoUpdateMods
	{
		get
		{
			return _autoUpdateMods;
		}
		set
		{
			_autoUpdateMods = value;
			ConfigurationService.AutoUpdateMods = _autoUpdateMods;
		}
	}

	public bool PanaceaInstalled
	{
		get
		{
			return _panaceaInstalled;
		}
		set
		{
			_panaceaInstalled = value;
			OnPropertyChanged("PatchVisible");
			OnPropertyChanged("ModLoader");
			OnPropertyChanged("PanaceaSettings");
		}
	}

	public bool PC
	{
		get
		{
			return _pc;
		}
		set
		{
			_pc = value;
			OnPropertyChanged("PC");
			OnPropertyChanged("ModLoader");
			OnPropertyChanged("PatchVisible");
			OnPropertyChanged("notPC");
			OnPropertyChanged("isPC");
			OnPropertyChanged("GameSelectVisible");
			OnPropertyChanged("GameSelectInteractable");
			OnPropertyChanged("PanaceaSettings");
		}
	}

	public bool PCSX2
	{
		get
		{
			return _pcsx2;
		}
		set
		{
			_pcsx2 = value;
			OnPropertyChanged("PCSX2");
			OnPropertyChanged("ModLoader");
			OnPropertyChanged("PatchVisible");
			OnPropertyChanged("notPC");
			OnPropertyChanged("isPC");
			OnPropertyChanged("GameSelectVisible");
			OnPropertyChanged("GameSelectInteractable");
			OnPropertyChanged("PanaceaSettings");
		}
	}

	public int GametoLaunch
	{
		get
		{
			switch (_launchGame)
			{
			case "kh2":
				launchExecutable = 0;
				break;
			case "kh1":
				launchExecutable = 1;
				break;
			case "bbs":
				launchExecutable = 2;
				break;
			case "Recom":
				launchExecutable = 3;
				break;
			case "kh3d":
				launchExecutable = 4;
				break;
			default:
				launchExecutable = 0;
				break;
			}
			return launchExecutable;
		}
		set
		{
			launchExecutable = value;
			switch ((GameIDs)value)
			{
			case GameIDs.KH2:
				_launchGame = "kh2";
				break;
			case GameIDs.KH1:
				_launchGame = "kh1";
				break;
			case GameIDs.BBS:
				_launchGame = "bbs";
				break;
			case GameIDs.Recom:
				_launchGame = "Recom";
				break;
			case GameIDs.KH3D:
				_launchGame = "kh3d";
				break;
			default:
				_launchGame = "kh2";
				break;
			}
			ConfigurationService.LaunchGame = _launchGame;
			ReloadModsList();
		}
	}

	public bool IsBuilding
	{
		get
		{
			return _isBuilding;
		}
		set
		{
			_isBuilding = value;
			_dispatcher.Post(delegate
			{
				OnPropertyChanged("BuildCommand");
				OnPropertyChanged("BuildAndRunCommand");
			});
		}
	}

	public bool IsRunning
	{
		get
		{
			return _isRunning;
		}
		private set
		{
			if (_isRunning != value)
			{
				_isRunning = value;
				OnPropertyChanged("IsRunning");
				InvalidateWorkflowCommands();
			}
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		private set
		{
			if (_isBusy != value)
			{
				_isBusy = value;
				OnPropertyChanged("IsBusy");
				InvalidateWorkflowCommands();
			}
		}
	}

	public MainViewModel(MainViewModelDependencies dependencies)
	{
		ArgumentNullException.ThrowIfNull(dependencies, "dependencies");
		_progressDialogService = dependencies.Progress ?? throw new ArgumentNullException("Progress");
		_messages = dependencies.Messages ?? throw new ArgumentNullException("Messages");
		_dispatcher = dependencies.Dispatcher ?? throw new ArgumentNullException("Dispatcher");
		_navigation = dependencies.Navigation ?? throw new ArgumentNullException("Navigation");
		_browser = dependencies.Browser ?? throw new ArgumentNullException("Browser");
		_lifetime = dependencies.Lifetime ?? throw new ArgumentNullException("Lifetime");
		_processes = dependencies.Processes ?? throw new ArgumentNullException("Processes");
		_debugLog = dependencies.DebugLog ?? throw new ArgumentNullException("DebugLog");
		_modViewModelFactory = dependencies.ModViewModelFactory ?? throw new ArgumentNullException("ModViewModelFactory");
		_modWorkflows = dependencies.ModWorkflows ?? throw new ArgumentNullException("ModWorkflows");
		_presets = dependencies.Presets ?? throw new ArgumentNullException("Presets");
		_updateChecker = dependencies.UpdateChecker ?? throw new ArgumentNullException("UpdateChecker");
		_updateExecutor = dependencies.UpdateExecutor ?? throw new ArgumentNullException("UpdateExecutor");
		_gameWorkflows = dependencies.GameWorkflows ?? throw new ArgumentNullException("GameWorkflows");
		_gamePatches = dependencies.GamePatches ?? throw new ArgumentNullException("GamePatches");
		_logHandler = HandleLog;
		if (ConfigurationService.GameEdition == 2)
		{
			PC = true;
			PCSX2 = false;
			PanaceaInstalled = ConfigurationService.PanaceaInstalled;
			DevView = ConfigurationService.DevView;
			_panaceaConsoleEnabled = ConfigurationService.ShowConsole;
			_panaceaDebugLogEnabled = ConfigurationService.DebugLog;
			_panaceaSoundDebugEnabled = ConfigurationService.SoundDebug;
			_panaceaCacheEnabled = ConfigurationService.EnableCache;
			_panaceaQuickMenuEnabled = ConfigurationService.QuickMenu;
		}
		else if (ConfigurationService.GameEdition == 1)
		{
			PC = false;
			PCSX2 = true;
		}
		else
		{
			PC = false;
			PCSX2 = false;
		}
		if ((_supportedGames.Contains(ConfigurationService.LaunchGame) && PC) || (_supportedPCSX2Games.Contains(ConfigurationService.LaunchGame) && PCSX2))
		{
			_launchGame = ConfigurationService.LaunchGame;
		}
		else
		{
			ConfigurationService.LaunchGame = _launchGame;
		}
		AutoUpdateMods = ConfigurationService.AutoUpdateMods;
		try
		{
			Log.OnLogDispatch += _logHandler;
		}
		catch
		{
			ShowMessage("Mods Manager had problems starting and must close.", "Error", MessageDialogKind.Error);
			_lifetime.Shutdown(1);
		}
		ReloadModsList();
		SelectedValue = ModsList.FirstOrDefault();
		ReloadPresetList();
		ExitCommand = new RelayCommand((Action<object>)delegate
		{
			_lifetime.Shutdown();
		}, (Predicate<object>)null);
		AddModCommand = new AsyncCommand(async delegate(object _, CancellationToken cancellation)
		{
			InstallSelectionResult selection = (await _navigation.ShowAsync(new NavigationRequest(NavigationDestination.InstallSelection, new InstallSelectionParameter(), IsModal: true), cancellation)) as InstallSelectionResult;
			InstallSelectionResult installSelectionResult = selection;
			if ((object)installSelectionResult == null || !installSelectionResult.Accepted)
			{
				return;
			}
			try
			{
				string name = selection.RepositoryName;
				ModInstallResult installResult = null;
				if (!(await _progressDialogService.RunAsync(new ProgressDialogRequest(selection.IsArchive ? GetDisplayName(name) : name, "Initializing", null, IsIndeterminate: true, IsCancellable: false), async delegate(IProgress<ProgressDialogUpdate> progress, CancellationToken token)
				{
					installResult = await _modWorkflows.InstallAsync(new ModInstallRequest(name, selection.IsArchive, selection.IsLua, selection.BranchName), progress, token);
				}, cancellation)).IsCancelled)
				{
					ModModel mod = _modWorkflows.GetMods(new string[1] { installResult.InstalledName }).First();
					await _dispatcher.InvokeAsync(delegate
					{
						if (installResult.OverwroteExistingMod)
						{
							ModViewModel modViewModel = ModsList.FirstOrDefault((ModViewModel smod) => smod.Title == mod.Metadata.Title);
							if (modViewModel != null)
							{
								ModsList.Remove(modViewModel);
							}
						}
						ModsList.Insert(0, Map(mod));
						SelectedValue = ModsList[0];
					});
				}
			}
			catch (Exception ex)
			{
				Log.Warn("Unable to install the mod `{0}`: {1}\n", selection.RepositoryName, Log.FormatSecondaryLinesWithIndent(ex.ToString(), "  "));
				ShowMessage(ex.Message, "Install error", MessageDialogKind.Error);
			}
		}, (object _) => !IsBusy, _lifetimeCancellation.Token);
		RemoveModCommand = new AsyncCommand(async delegate(object _, CancellationToken cancellation)
		{
			ModViewModel mod = SelectedValue;
			if (await ConfirmAsync("Do you want to delete the mod '" + mod.Source + "'?", "Remove mod " + mod.Source, cancellation))
			{
				try
				{
					await _modWorkflows.RemoveAsync(mod.Path, cancellation);
					ModsList.RemoveAt(ModsList.IndexOf(SelectedValue));
				}
				catch (Exception ex)
				{
					ShowMessage(ex.Message, "Remove mod error", MessageDialogKind.Error);
				}
			}
		}, (object _) => IsModSelected && !IsBusy, _lifetimeCancellation.Token);
		OpenModFolderCommand = new AsyncCommand((object _, CancellationToken cancellation) => _processes.LaunchAsync(new ShellProcessRequest(SelectedValue.Path, null, null, UseShellExecute: true), cancellation), (object _) => IsModSelected, _lifetimeCancellation.Token);
		MoveTop = new RelayCommand(delegate
		{
			MoveSelectedModTop();
		}, (object _) => CanSelectedModMoveUp());
		MoveUp = new RelayCommand(delegate
		{
			MoveSelectedModUp();
		}, (object _) => CanSelectedModMoveUp());
		MoveDown = new RelayCommand(delegate
		{
			MoveSelectedModDown();
		}, (object _) => CanSelectedModMoveDown());
		BuildCommand = new AsyncCommand((object _, CancellationToken cancellation) => RunExclusiveAsync(async delegate(CancellationToken token)
		{
			ResetLogWindow();
			await BuildPatches(fastMode: false, token);
			await CloseDebugSessionAsync(token);
		}, cancellation), (object _) => CanStartWorkflow(), _lifetimeCancellation.Token);
		PatchCommand = new AsyncCommand((object fastMode, CancellationToken cancellation) => RunExclusiveAsync(async delegate(CancellationToken token)
		{
			try
			{
				ResetLogWindow();
				await BuildPatches(Convert.ToBoolean(fastMode), token);
				await _gamePatches.PatchAsync(new GamePatchRequest(_launchGame, Convert.ToBoolean(fastMode)), new Progress<GamePatchProgress>(delegate(GamePatchProgress x)
				{
					CaptureLog(x.Message);
				}), token);
				await CloseDebugSessionAsync(token);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				ShowMessage(ex2.Message, "Patch error", MessageDialogKind.Error);
			}
		}, cancellation), (object _) => CanStartWorkflow(), _lifetimeCancellation.Token);
		RunCommand = new AsyncCommand((object _, CancellationToken cancellation) => RunExclusiveAsync(async delegate(CancellationToken token)
		{
			await CloseRunningProcessAsync(token);
			ResetLogWindow();
			await RunGameAsync(token);
		}, cancellation), (object _) => CanStartWorkflow(), _lifetimeCancellation.Token);
		RestoreCommand = new AsyncCommand((object patched, CancellationToken cancellation) => RunExclusiveAsync(async delegate(CancellationToken token)
		{
			ResetLogWindow();
			await _gamePatches.RestoreAsync(new GameRestoreRequest(_launchGame, Convert.ToBoolean(patched)), new Progress<GamePatchProgress>(delegate(GamePatchProgress x)
			{
				CaptureLog(x.Message);
			}), token);
			await CloseDebugSessionAsync(token);
		}, cancellation), (object _) => CanStartWorkflow(), _lifetimeCancellation.Token);
		BuildAndRunCommand = new AsyncCommand((object _, CancellationToken cancellation) => RunExclusiveAsync(async delegate(CancellationToken token)
		{
			await CloseRunningProcessAsync(token);
			ResetLogWindow();
			if (await BuildPatches(fastMode: false, token))
			{
				await RunGameAsync(token);
			}
		}, cancellation), (object _) => CanStartWorkflow(), _lifetimeCancellation.Token);
		StopRunningInstanceCommand = new AsyncCommand(async delegate(object _, CancellationToken cancellation)
		{
			await CloseRunningProcessAsync(cancellation);
			ResetLogWindow();
		}, (object _) => IsRunning, _lifetimeCancellation.Token);
		WizardCommand = new AsyncCommand((Func<object, Task>)async delegate
		{
			if (((await _navigation.ShowAsync(new NavigationRequest(NavigationDestination.SetupWizard, null, IsModal: true))) as SetupWizardResult)?.Completed ?? false)
			{
				if (ConfigurationService.GameEdition == 2)
				{
					PC = true;
					PCSX2 = false;
					PanaceaInstalled = ConfigurationService.PanaceaInstalled;
					GameAvailability availability = _gameWorkflows.GetAvailability();
					if (!availability.PcInstallExists)
					{
						if (availability.Kh3dInstallExists)
						{
							GametoLaunch = 4;
						}
						else
						{
							ShowMessage("Unable to locate install locations for both KINGDOM HEARTS HD 1.5+2.5 ReMIX and KINGDOM HEARTS HD 2.8 Final Chapter Prologue. They are either missing or corrupted. Please re-run the setup wizard and confirm the install paths are correct.", "Run error", MessageDialogKind.Error);
						}
					}
				}
				else if (ConfigurationService.GameEdition == 1)
				{
					PC = false;
					PCSX2 = true;
					string preferred = _gameWorkflows.GetPreferredGameId(_launchGame);
					if (1 == 0)
					{
					}
					string text = preferred;
					int gametoLaunch = ((text == "kh1") ? 1 : ((text == "Recom") ? 3 : 0));
					if (1 == 0)
					{
					}
					GametoLaunch = gametoLaunch;
				}
				else
				{
					PC = false;
					PCSX2 = false;
					GametoLaunch = 0;
				}
				ConfigurationService.WizardVersionNumber = _wizardVersionNumber;
			}
			OnPropertyChanged("GametoLaunch");
			OnPropertyChanged("GameSelectKH2");
			OnPropertyChanged("GameSelectKH1");
			OnPropertyChanged("GameSelectBBS");
			OnPropertyChanged("GameSelectRecom");
			OnPropertyChanged("GameSelectKH3D");
		}, (Predicate<object>)null);
		OpenPresetMenuCommand = new AsyncCommand(() => _navigation.ShowAsync(new NavigationRequest(NavigationDestination.Presets, new PresetsParameter(this), IsModal: true)));
		CheckForModUpdatesCommand = new AsyncCommand(FetchUpdates, () => !IsBusy, _lifetimeCancellation.Token);
		OpenLinkCommand = new AsyncCommand((object url) => Uri.TryCreate(url as string, UriKind.Absolute, out Uri result) ? _browser.OpenAsync(result) : Task.CompletedTask);
		CheckOpenkhUpdateCommand = new AsyncCommand(UpdateOpenkhAsync, () => !IsBusy, _lifetimeCancellation.Token);
		YamlGeneratorCommand = new AsyncCommand(() => _navigation.ShowAsync(new NavigationRequest(NavigationDestination.YamlGenerator, new YamlGeneratorParameter(this))));
		OpenModSearchCommand = new AsyncCommand((Func<Task>)async delegate
		{
			await _navigation.ShowAsync(new NavigationRequest(NavigationDestination.ModSearch, new ModSearchParameter(this), IsModal: true));
			ReloadModsList();
		}, (Func<bool>)null);
	}

	public Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (_initializationTask != null)
		{
			return _initializationTask;
		}
		_initializationTask = InitializeCoreAsync(cancellationToken);
		return _initializationTask;
	}

	private async Task InitializeCoreAsync(CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token, cancellationToken);
		PanaceaUpdateResult panaceaUpdate = await _gameWorkflows.ApplyPendingPanaceaUpdateAsync(linked.Token);
		if (panaceaUpdate.Attempted)
		{
			ShowMessage(panaceaUpdate.Succeeded ? "Panacea has successfully been updated alongside Mods Manager.\nIf you notice any mod loading bugs you might still want to uninstall and reinstall Panacea just in case." : "Unable to automatically update Panacea.\nPlease manually run the setup wizard and reinstall Panacea.", panaceaUpdate.Succeeded ? "Success" : "Error", (!panaceaUpdate.Succeeded) ? MessageDialogKind.Error : MessageDialogKind.Information);
		}
		await FetchUpdates(linked.Token);
		AsyncCommand wizard = default(AsyncCommand);
		int num;
		if (ConfigurationService.WizardVersionNumber < _wizardVersionNumber)
		{
			ICommand wizardCommand = WizardCommand;
			wizard = wizardCommand as AsyncCommand;
			num = ((wizard != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			await wizard.ExecuteAsync();
		}
	}

	private async Task CloseRunningProcessAsync(CancellationToken cancellationToken)
	{
		IGameSession session = Interlocked.Exchange(ref _gameSession, null);
		if (session != null)
		{
			session.OutputReceived -= GameOutputReceived;
			session.Exited -= GameExited;
			await session.StopAsync(cancellationToken);
			await session.DisposeAsync();
		}
		await _dispatcher.InvokeAsync(() => IsRunning = false, cancellationToken);
	}

	private void ResetLogWindow()
	{
		var previous = _debugSession;
		_debugSession = null;
		if (previous != null)
		{
			previous.Dispose();
		}
		if (!_disposed)
		{
			_debugSession = _debugLog.Start(new DebugLogRequest("OpenKh debug log", ShowImmediately: true));
		}
	}

	private async Task<bool> BuildPatches(bool fastMode, CancellationToken cancellationToken)
	{
		IsBuilding = true;
		try
		{
			return await _modWorkflows.BuildAsync(fastMode, cancellationToken);
		}
		finally
		{
			IsBuilding = false;
		}
	}

	private async Task RunGameAsync(CancellationToken cancellationToken)
	{
		GameStartResult result = await _gameWorkflows.StartAsync(new GameStartRequest(_launchGame), cancellationToken);
		if (!string.IsNullOrEmpty(result.ErrorMessage))
		{
			ShowMessage(result.ErrorMessage, "Run error", MessageDialogKind.Error);
		}
		if (result.CloseApplication)
		{
			_lifetime.Shutdown();
		}
		_gameSession = result.Session;
		if (_gameSession != null)
		{
			_gameSession.OutputReceived += GameOutputReceived;
			_gameSession.Exited += GameExited;
			IsRunning = _gameSession.IsRunning;
		}
	}

	private void GameOutputReceived(object sender, GameOutputEventArgs e)
	{
		if (!_disposed)
		{
			CaptureLog(e.Text);
		}
	}

	private void GameExited(object sender, EventArgs e)
	{
		if (_disposed || sender != _gameSession)
		{
			return;
		}
		_dispatcher.Post(delegate
		{
			if (!_disposed && sender == _gameSession)
			{
				IsRunning = false;
			}
		});
	}

	private bool CanStartWorkflow()
	{
		return !IsBusy && !IsRunning && !_disposed;
	}

	private async Task RunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken commandToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token, commandToken);
		if (!(await _workflowGate.WaitAsync(0, linked.Token)))
		{
			return;
		}
		IsBusy = true;
		try
		{
			await action(linked.Token);
		}
		finally
		{
			IsBusy = false;
			_workflowGate.Release();
		}
	}

	private void InvalidateWorkflowCommands()
	{
		ICommand[] array = new ICommand[10] { AddModCommand, RemoveModCommand, BuildCommand, PatchCommand, RestoreCommand, RunCommand, BuildAndRunCommand, StopRunningInstanceCommand, CheckForModUpdatesCommand, CheckOpenkhUpdateCommand };
		foreach (ICommand command in array)
		{
			(command as AsyncCommand)?.RaiseCanExecuteChanged();
		}
	}

	private void CaptureLog(string data)
	{
		if (data != null)
		{
			if (data.Contains("err", StringComparison.InvariantCultureIgnoreCase))
			{
				Log.Err(data);
			}
			else if (data.Contains("wrn", StringComparison.InvariantCultureIgnoreCase))
			{
				Log.Warn(data);
			}
			else if (data.Contains("warn", StringComparison.InvariantCultureIgnoreCase))
			{
				Log.Warn(data);
			}
			else
			{
				Log.Info(data);
			}
		}
	}

	public void ReloadModsList()
	{
		ModsList = new ObservableCollection<ModViewModel>(ModsService.GetMods(ModsService.Mods).Select(Map));
		OnPropertyChanged("ModsList");
		OnPropertyChanged("CollectionModsList");
	}

	private ModViewModel Map(ModModel mod)
	{
		return _modViewModelFactory(mod, this);
	}

	public void ModEnableStateChanged()
	{
		ConfigurationService.EnabledMods = (from x in ModsList
			where x.Enabled
			select x.Source).ToList();
		OnPropertyChanged("BuildAndRunCommand");
	}

	private void MoveSelectedModDown()
	{
		int num = ModsList.IndexOf(SelectedValue);
		if (num >= 0)
		{
			ModViewModel item = ModsList[num];
			ModsList.RemoveAt(num);
			ModsList.Insert(++num, item);
			SelectedValue = ModsList[num];
			ModEnableStateChanged();
		}
	}

	private void MoveSelectedModUp()
	{
		int num = ModsList.IndexOf(SelectedValue);
		if (num >= 0)
		{
			ModViewModel item = ModsList[num];
			ModsList.RemoveAt(num);
			ModsList.Insert(--num, item);
			SelectedValue = ModsList[num];
			ModEnableStateChanged();
		}
	}

	private void MoveSelectedModTop()
	{
		int num = ModsList.IndexOf(SelectedValue);
		if (num >= 0)
		{
			ModViewModel item = ModsList[num];
			ModsList.RemoveAt(num);
			ModsList.Insert(num = 0, item);
			SelectedValue = ModsList[num];
			ModEnableStateChanged();
		}
	}

	private bool CanSelectedModMoveDown()
	{
		return SelectedValue != null && ModsList.IndexOf(SelectedValue) < ModsList.Count - 1;
	}

	private bool CanSelectedModMoveUp()
	{
		return SelectedValue != null && ModsList.IndexOf(SelectedValue) > 0;
	}

	private async Task FetchUpdates(CancellationToken cancellationToken = default(CancellationToken))
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token, cancellationToken);
		await _updateFetchGate.WaitAsync(linked.Token);
		try
		{
			await foreach (ModUpdateModel modUpdate in _modWorkflows.FetchUpdatesAsync(linked.Token))
			{
				ModViewModel mod = ModsList.FirstOrDefault((ModViewModel x) => x.Source == modUpdate.Name);
				if (mod != null)
				{
					await _dispatcher.InvokeAsync(() => mod.UpdateCount = modUpdate.UpdateCount, linked.Token);
				}
			}
			if (!AutoUpdateMods)
			{
				return;
			}
			foreach (ModViewModel mod2 in ModsList.Where((ModViewModel x) => x.UpdateCount > 0).ToList())
			{
				await _modWorkflows.UpdateAsync(mod2.Source, linked.Token);
			}
			ReloadModsList();
		}
		finally
		{
			_updateFetchGate.Release();
		}
	}

	private async Task UpdateOpenkhAsync(CancellationToken cancellationToken)
	{
		ApplicationUpdateInfo checkResult = null;
		if ((await _progressDialogService.RunAsync(new ProgressDialogRequest("OpenKh", "Checking update from github.com"), async delegate(IProgress<ProgressDialogUpdate> _, CancellationToken cancellation)
		{
			checkResult = await _updateChecker.CheckAsync(cancellation);
			cancellation.ThrowIfCancellationRequested();
		}, cancellationToken)).IsCancelled)
		{
			return;
		}
		if (checkResult.HasUpdate)
		{
			if (!_updateExecutor.CanExecute)
			{
				string linuxMessage = "A new version of OpenKh has been detected!\n" + $"[Current: {checkResult.CurrentVersion}, Latest: {checkResult.NewVersion}]\n\n" + "Do you want to open the releases page to download it?";
				if (await ConfirmAsync(linuxMessage, "OpenKh", cancellationToken))
				{
					await _browser.OpenAsync(new Uri("https://github.com/OpenKH/OpenKh/releases"));
				}
				return;
			}
			string message = "A new version of OpenKh has been detected!\n" + $"[Current: {checkResult.CurrentVersion}, Latest: {checkResult.NewVersion}]\n\n" + "Do you wish to update the game?";
			if (await ConfirmAsync(message, "OpenKh", cancellationToken) && !(await _progressDialogService.RunAsync(new ProgressDialogRequest("OpenKh", "Updating"), async delegate(IProgress<ProgressDialogUpdate> progress, CancellationToken cancellation)
			{
				await _updateExecutor.ExecuteAsync(checkResult.DownloadUrl, new Progress<double>(delegate(double rate)
				{
					progress.Report(new ProgressDialogUpdate(null, null, rate, false));
				}), cancellation);
			}, cancellationToken)).IsCancelled)
			{
				ConfigurationService.Updated = true;
				_lifetime.Shutdown();
			}
		}
		else
		{
			string message2 = "The latest version '" + checkResult.CurrentVersion + "' is already installed!";
			ShowMessage(message2, "OpenKh");
		}
	}

	public void UpdatePanaceaSettings()
	{
		if (PanaceaInstalled)
		{
			_gameWorkflows.UpdatePanaceaSettings(new PanaceaSettings(_launchGame, _panaceaConsoleEnabled, _panaceaDebugLogEnabled, _panaceaSoundDebugEnabled, _panaceaCacheEnabled, _panaceaQuickMenuEnabled));
		}
	}

	public void SavePreset(string presetName)
	{
		List<string> enabledMods = (from x in ModsList
			where x.Enabled
			select x.Source).ToList();
		_presets.Save(presetName, enabledMods);
		if (!PresetList.Contains(presetName))
		{
			PresetList.Add(presetName);
		}
	}

	public void RemovePreset(string presetName)
	{
		_presets.Remove(presetName);
		PresetList.Remove(presetName);
	}

	public void LoadPreset(string presetName)
	{
		if (_presets.TryLoad(presetName, out var enabledMods))
		{
			ConfigurationService.EnabledMods = enabledMods.ToList();
			ReloadModsList();
		}
		else
		{
			ShowMessage("Cannot find preset", "Error", MessageDialogKind.Warning);
		}
	}

	public void ReloadPresetList()
	{
		if (PresetList == null)
		{
			PresetList = new ObservableCollection<string>();
		}
		PresetList.Clear();
		foreach (string name in _presets.GetNames())
		{
			PresetList.Add(name);
		}
	}

	private void HandleLog(long milliseconds, string tag, string message)
	{
		if (!_disposed)
		{
			_debugSession?.Write(new DebugLogEntry(message, DateTimeOffset.UtcNow.AddMilliseconds(milliseconds), tag));
		}
	}

	private void ShowMessage(string message, string title = null, MessageDialogKind kind = MessageDialogKind.Information)
	{
		_messages.ShowAsync(new MessageDialogRequest(message, title, kind));
	}

	private async Task<bool> ConfirmAsync(string message, string title, CancellationToken cancellationToken)
	{
		return await _messages.ShowAsync(new MessageDialogRequest(message, title, MessageDialogKind.Question, MessageDialogButtons.YesNo), cancellationToken) == MessageDialogResult.Yes;
	}

	private async Task CloseDebugSessionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IDebugLogSession session = _debugSession;
		_debugSession = null;
		if (session != null)
		{
			await session.CloseAsync(cancellationToken);
			session.Dispose();
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_lifetimeCancellation.Cancel();
			Log.OnLogDispatch -= _logHandler;
			IGameSession gameSession = _gameSession;
			if (gameSession != null)
			{
				gameSession.OutputReceived -= GameOutputReceived;
				gameSession.Exited -= GameExited;
			}
			GC.SuppressFinalize(this);
		}
	}

	public Task CloseAsync()
	{
		if (_closeTask != null)
		{
			return _closeTask;
		}
		Dispose();
		return _closeTask = CloseCoreAsync();
	}

	private async Task CloseCoreAsync()
	{
		await CloseRunningProcessAsync(CancellationToken.None);
		await CloseDebugSessionAsync();
		_lifetimeCancellation.Dispose();
		_workflowGate.Dispose();
		_updateFetchGate.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		await CloseAsync();
		GC.SuppressFinalize(this);
	}

	private static string GetDisplayName(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}
		int num = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
		string result;
		if (num < 0)
		{
			result = path;
		}
		else
		{
			int num2 = num + 1;
			result = path.Substring(num2, path.Length - num2);
		}
		return result;
	}
}
