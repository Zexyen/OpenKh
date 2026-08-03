using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using System;

namespace OpenKh.Tools.ModsManager.ViewModels
{
    public static class ModViewModelFactory
    {
        private static Func<ModModel, IChangeModEnableState, ModViewModel> _factory;

        public static void Configure(Func<ModModel, IChangeModEnableState, ModViewModel> factory) =>
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        public static ModViewModel Create(ModModel model, IChangeModEnableState changeModEnableState) =>
            (_factory ?? throw new InvalidOperationException("ModViewModelFactory has not been configured."))
            (model, changeModEnableState);
    }
}
