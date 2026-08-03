using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Models.ViewHelper;
using OpenKh.Tools.ModsManager.ViewModels;
using OpenKh.Tools.ModsManager.Views;
using System.ComponentModel;
using System.Linq;
using Xunit;

namespace OpenKh.Tests.ModsManager
{
    public class CoreViewModelsTests
    {
        [Fact]
        public void CopySourceFilesSelectsFirstPrimarySourceAndNotifies()
        {
            var viewModel = new CopySourceFilesVM();
            var first = new PrimarySource("first");
            var second = new PrimarySource("second");
            string lastProperty = null;
            ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => lastProperty = e.PropertyName;

            viewModel.PrimarySourceList = new[] { first, second };

            Assert.Same(first, viewModel.SelectedPrimarySource);
            Assert.Equal(nameof(CopySourceFilesVM.SelectedPrimarySource), lastProperty);
        }

        [Fact]
        public void SelectModTargetFilesStoresFrameworkNeutralState()
        {
            var viewModel = new SelectModTargetFilesVM();
            var hits = new[] { new SearchHit("item", "item.bin", "C:/data/item.bin") };

            viewModel.SearchKeywords = "item";
            viewModel.SearchHits = hits;

            Assert.Equal("item", viewModel.SearchKeywords);
            Assert.Same(hits, viewModel.SearchHits);
        }

        [Fact]
        public void NotepadTextRaisesPropertyChanged()
        {
            var viewModel = new NotepadVM();
            var properties = Enumerable.Empty<string>().ToList();
            ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => properties.Add(e.PropertyName);

            viewModel.Text = "notes";

            Assert.Equal("notes", viewModel.Text);
            Assert.Contains(nameof(NotepadVM.Text), properties);
        }

        [Fact]
        public void CollectionSettingsExposesModelAndReportsEnabledChanges()
        {
            var model = new CollectionModModel
            {
                Name = "Optional files",
                Author = "OpenKH",
                IsEnabled = false,
            };
            var state = new ChangeCollectionModEnableState();
            var viewModel = new CollectionSettingsViewModel(model, state);
            string changedProperty = null;
            ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changedProperty = e.PropertyName;

            viewModel.Enabled = true;

            Assert.Equal("Optional files", viewModel.Name);
            Assert.Equal("OpenKH", viewModel.Author);
            Assert.True(viewModel.Enabled);
            Assert.True(model.IsEnabled);
            Assert.Equal(1, state.ChangeCount);
            Assert.Equal(nameof(CollectionSettingsViewModel.Enabled), changedProperty);
        }

        private sealed class ChangeCollectionModEnableState : IChangeCollectionModEnableState
        {
            public int ChangeCount { get; private set; }

            public void CollectionModEnableStateChanged() => ChangeCount++;
        }
    }
}
