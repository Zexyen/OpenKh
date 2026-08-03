using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;

namespace OpenKh.Tools.ModsManager.ViewModels
{
    public class CollectionSettingsViewModel : ObservableObject
    {
        private readonly CollectionModModel _model;
        private readonly IChangeCollectionModEnableState _changeModEnableState;

        public CollectionSettingsViewModel(
            CollectionModModel model,
            IChangeCollectionModEnableState changeModEnableState)
        {
            _model = model;
            _changeModEnableState = changeModEnableState;
        }

        public ColorThemeService ColorTheme => ColorThemeService.Instance;
        public string Name => _model.Name;
        public string Author => _model.Author;

        public bool Enabled
        {
            get => _model.IsEnabled;
            set
            {
                _model.IsEnabled = value;
                _changeModEnableState.CollectionModEnableStateChanged();
                OnPropertyChanged();
            }
        }
    }
}
