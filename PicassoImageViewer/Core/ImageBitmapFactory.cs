using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace PicassoImageViewer.Core
{
    internal static class ImageBitmapFactory
    {
        public static BitmapSource CreateBitmapSourceFromFile(string path, int? decodePixelWidth = null)
        {
            try
            {
                return ExifOrientation.Apply(DecodeWithWic(path, decodePixelWidth), ExifOrientation.Read(path));
            }
            catch (Exception exception) when (exception is NotSupportedException || exception is FileFormatException)
            {
                try
                {
                    return DecodeWithGdi(path);
                }
                catch (Exception)
                {
                    throw exception;
                }
            }
        }

        public static BitmapSource CreateBitmapSourceFromBitmap(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }

        private static BitmapSource DecodeWithWic(string path, int? decodePixelWidth)
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

            if (decodePixelWidth.HasValue)
                bitmapImage.DecodePixelWidth = decodePixelWidth.Value;

            bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        private static BitmapSource DecodeWithGdi(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var bitmap = new Bitmap(stream))
            {
                return CreateBitmapSourceFromBitmap(bitmap);
            }
        }
    }
}
