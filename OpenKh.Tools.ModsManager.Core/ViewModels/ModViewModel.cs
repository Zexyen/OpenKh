using OpenKh.Common;
using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.ViewModels
{
    public sealed class ModViewModel : ObservableObject, IChangeCollectionModEnableState, INavigationContext
    {
        private static readonly string FallbackImage = null;
        private readonly ModModel _model;
        private readonly IChangeModEnableState _changeModEnableState;
        private readonly IProgressDialogService _progressDialogs;
        private readonly IMessageDialogService _messages;
        private readonly IUiDispatcher _dispatcher;
        private readonly INavigationService _navigation;
        private readonly IImageService _images;
        private readonly Func<string, Action<string>, Action<float>, Task> _updateMod;
        private readonly Func<ModModel, IEnumerable<CollectionModModel>> _getCollectionMods;
        private readonly AsyncCommand _updateCommand;
        private readonly AsyncCommand _collectionSettingsCommand;
        private CollectionSettingsViewModel _selectedCollectionValue;
        private ImageData _iconImage;
        private ImageData _previewImage;
        private int _updateCount;
        private bool _isUpdating;
        private bool _isOpeningCollectionSettings;

        public ModViewModel(ModModel model, IChangeModEnableState changeModEnableState,
            IProgressDialogService progressDialogs, IMessageDialogService messages, IUiDispatcher dispatcher,
            INavigationService navigation, IImageService images)
            : this(model, changeModEnableState, progressDialogs, messages, dispatcher, navigation, images,
                  (source, progress, value) => ModsService.Update(source, progress, value),
                  ModsService.GetCollectionOptionalMods)
        {
        }

        internal ModViewModel(ModModel model, IChangeModEnableState changeModEnableState,
            IProgressDialogService progressDialogs, IMessageDialogService messages, IUiDispatcher dispatcher,
            INavigationService navigation, IImageService images,
            Func<string, Action<string>, Action<float>, Task> updateMod,
            Func<ModModel, IEnumerable<CollectionModModel>> getCollectionMods)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _changeModEnableState = changeModEnableState ?? throw new ArgumentNullException(nameof(changeModEnableState));
            _progressDialogs = progressDialogs ?? throw new ArgumentNullException(nameof(progressDialogs));
            _messages = messages ?? throw new ArgumentNullException(nameof(messages));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _images = images ?? throw new ArgumentNullException(nameof(images));
            _updateMod = updateMod ?? throw new ArgumentNullException(nameof(updateMod));
            _getCollectionMods = getCollectionMods ?? throw new ArgumentNullException(nameof(getCollectionMods));

            var nameIndex = Source.IndexOf('/');
            if (nameIndex > 0)
            {
                Author = Source[0..nameIndex];
                Name = Source[(nameIndex + 1)..];
            }
            else
            {
                Author = _model.Metadata?.OriginalAuthor;
                Name = Source;
            }

            if (IsCollection)
            {
                ReloadCollectionModsList();
                CollectionSelectedValue = CollectionModsList.FirstOrDefault();
            }

            if (Title != null)
                Name = Title;

            _updateCommand = new AsyncCommand(UpdateAsync, () => !IsUpdating);
            _collectionSettingsCommand = new AsyncCommand(OpenCollectionSettingsAsync,
                () => IsCollection && !IsOpeningCollectionSettings);
            UpdateCommand = _updateCommand;
            CollectionSettingsCommand = _collectionSettingsCommand;
            _ = ReadMetadataAsync();
        }

        public ColorThemeService ColorTheme => ColorThemeService.Instance;
        public ObservableCollection<CollectionSettingsViewModel> CollectionModsList { get; private set; }
        public AsyncCommand UpdateCommand { get; }
        public AsyncCommand CollectionSettingsCommand { get; }

        public bool Enabled
        {
            get => _model.IsEnabled;
            set
            {
                if (_model.IsEnabled == value)
                    return;
                _model.IsEnabled = value;
                _changeModEnableState.ModEnableStateChanged();
                OnPropertyChanged();
            }
        }

        public CollectionSettingsViewModel CollectionSelectedValue
        {
            get => _selectedCollectionValue;
            set
            {
                if (!SetProperty(ref _selectedCollectionValue, value))
                    return;
                OnPropertyChanged(nameof(IsModSelected));
                OnPropertyChanged(nameof(IsModUnselectedMessageVisible));
            }
        }

        public bool IsModSelected => CollectionSelectedValue != null;
        public ImageData IconImage { get => _iconImage; private set => SetProperty(ref _iconImage, value); }
        public ImageData PreviewImage
        {
            get => _previewImage;
            private set
            {
                if (SetProperty(ref _previewImage, value))
                    OnPropertyChanged(nameof(PreviewImageVisibility));
            }
        }
        public bool PreviewImageVisibility => PreviewImage != null;
        public bool IsHosted => _model.Name.Contains('/');
        public bool IsCollection => _model.Metadata?.IsCollection == true;
        public string Path => _model.Path;
        public bool SourceVisibility => IsHosted;
        public bool LocalVisibility => !IsHosted;
        public bool CollectionSettingsVisibility => IsCollection;
        public bool IsModUnselectedMessageVisible => !IsModSelected;
        public string Title => _model?.Metadata?.Title ?? Name;
        public string Name { get; private set; }
        public string Author { get; }
        public string Source => _model.Name;
        public string AuthorUrl => $"https://github.com/{Author}";
        public string SourceUrl => $"https://github.com/{Source}";
        public string ReportBugUrl => $"https://github.com/{Source}/issues";
        public string FilesToPatch => string.Join('\n', GetFilesToPatch());
        public string Description => _model.Metadata?.Description;

        public string Homepage
        {
            get
            {
                if (Source == null)
                    return null;
                var author = System.IO.Path.GetDirectoryName(Source);
                var project = System.IO.Path.GetFileName(Source);
                return $"https://{author}.github.io/{project}";
            }
        }

        public int UpdateCount
        {
            get => _updateCount;
            set
            {
                if (!SetProperty(ref _updateCount, value))
                    return;
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateVisibility));
            }
        }

        public bool IsUpdateAvailable => UpdateCount > 0;
        public bool UpdateVisibility => IsUpdateAvailable;
        public bool IsUpdating
        {
            get => _isUpdating;
            private set
            {
                if (SetProperty(ref _isUpdating, value))
                    _updateCommand?.RaiseCanExecuteChanged();
            }
        }

        public bool IsOpeningCollectionSettings
        {
            get => _isOpeningCollectionSettings;
            private set
            {
                if (SetProperty(ref _isOpeningCollectionSettings, value))
                    _collectionSettingsCommand?.RaiseCanExecuteChanged();
            }
        }

        private async Task UpdateAsync()
        {
            IsUpdating = true;
            try
            {
                await _progressDialogs.RunAsync(
                    new ProgressDialogRequest("Updating", "Initializing", IsIndeterminate: true, IsCancellable: false),
                    async (progress, cancellationToken) =>
                    {
                        await _updateMod(Source,
                            message => progress.Report(new ProgressDialogUpdate(Message: message)),
                            value => progress.Report(new ProgressDialogUpdate(Value: value, IsIndeterminate: false)));
                        progress.Report(new ProgressDialogUpdate(Message: "Reading latest changes", Value: 1, IsIndeterminate: false));
                    });
                await ReadMetadataAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to update the mod `{0}`: {1}\n", Source,
                    Log.FormatSecondaryLinesWithIndent(ex.ToString(), "  "));
                await _messages.ShowAsync(new MessageDialogRequest(ex.Message, "Generic error", MessageDialogKind.Error));
            }
            finally
            {
                IsUpdating = false;
            }
        }

        private async Task OpenCollectionSettingsAsync()
        {
            IsOpeningCollectionSettings = true;
            try
            {
                await _navigation.ShowAsync(new NavigationRequest(NavigationDestination.CollectionSettings,
                    new CollectionSettingsParameter(this), IsModal: true));
                EnsureCollectionConfiguration();
                _model.CollectionOptionalEnabledAssets = ConfigurationService.EnabledCollectionMods[_model.Name];
                OnPropertyChanged(nameof(FilesToPatch));
            }
            finally
            {
                IsOpeningCollectionSettings = false;
            }
        }

        private IEnumerable<string> GetFilesToPatch()
        {
            foreach (var asset in _model.Metadata?.Assets ?? Enumerable.Empty<Patcher.AssetFile>())
            {
                var isOptionalEnabled = false;
                if (asset.CollectionOptional == true)
                {
                    _model.CollectionOptionalEnabledAssets?.TryGetValue(asset.Name, out isOptionalEnabled);
                    if (!isOptionalEnabled)
                        continue;
                }
                if (IsCollection && asset.Game != ConfigurationService.LaunchGame)
                    continue;
                yield return isOptionalEnabled ? $"{asset.Name} (optional, enabled)" : asset.Name;
                if (asset.Multi != null)
                    foreach (var multiAsset in asset.Multi)
                        yield return multiAsset.Name;
            }
        }

        private async Task ReadMetadataAsync()
        {
            var icon = await LoadImageAsync(_model.IconImageSource, FallbackImage);
            var preview = await LoadImageAsync(_model.PreviewImageSource, null);
            await _dispatcher.InvokeAsync(() =>
            {
                IconImage = icon;
                PreviewImage = preview;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(Homepage));
                OnPropertyChanged(nameof(FilesToPatch));
                UpdateCount = 0;
            });
        }

        private async Task<ImageData> LoadImageAsync(string source, string fallback)
        {
            try
            {
                var image = await _images.LoadAsync(new ImageRequest(source));
                return image ?? (string.IsNullOrEmpty(fallback) ? null : await _images.LoadAsync(new ImageRequest(fallback)));
            }
            catch
            {
                return null;
            }
        }

        private void ReloadCollectionModsList()
        {
            CollectionModsList = new ObservableCollection<CollectionSettingsViewModel>(_getCollectionMods(_model).Select(Map));
            OnPropertyChanged(nameof(CollectionModsList));
        }

        public void CollectionModEnableStateChanged()
        {
            var holder = ConfigurationService.EnabledCollectionMods;
            if (!holder.TryGetValue(_model.Name, out var current))
                current = new Dictionary<string, bool>();
            foreach (var mod in CollectionModsList)
                current[mod.Name] = mod.Enabled;
            holder[_model.Name] = current;
            ConfigurationService.EnabledCollectionMods = holder;
        }

        private void EnsureCollectionConfiguration()
        {
            if (ConfigurationService.EnabledCollectionMods.ContainsKey(_model.Name))
                return;
            var holder = ConfigurationService.EnabledCollectionMods;
            holder[_model.Name] = new Dictionary<string, bool>();
            ConfigurationService.EnabledCollectionMods = holder;
        }

        private CollectionSettingsViewModel Map(CollectionModModel mod) => new(mod, this);
    }
}
