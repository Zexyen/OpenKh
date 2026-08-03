using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Globalization;
using System.IO;

namespace OpenKh.Tools.ModsManager.Converters
{
    public sealed class ImageDataToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ImageData image || image.Bytes.IsEmpty)
                return null;

            using var stream = new MemoryStream(image.ToArray(), writable: false);
            return new Bitmap(stream);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
