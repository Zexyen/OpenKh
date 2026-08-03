using System;
using System.Collections.Generic;

namespace OpenKh.Tools.ModsManager.ExtensionMethods
{
    internal static class ReactiveExtensions
    {
        public static T AddTo<T>(this T self, ICollection<T> collection) where T : IDisposable
        {
            collection.Add(self);
            return self;
        }
    }
}
