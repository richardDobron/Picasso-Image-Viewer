using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace PicassoImageViewer.Core
{
    public class Image
    {
        public string Path { get; }
        public Bitmap Bitmap { get; }
        private BitmapSource _bitmapSource;
        private bool _isModified;

        public Image(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            Path = path;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var loadedBitmap = new Bitmap(stream))
            {
                Bitmap = new Bitmap(loadedBitmap);
            }
        }

        public BitmapSource GetBitmapSource()
        {
            if (_bitmapSource == null)
                _bitmapSource = _isModified ? CreateBitmapSourceFromBitmap(Bitmap) : LoadBitmapSourceFromDisk();

            return _bitmapSource;
        }

        public System.Windows.Size GetFittedSize(double sourceWidth, double sourceHeight, double maxWidth, double maxHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return new System.Windows.Size(10, 10);

            var scale = Math.Min(1.0, Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight));
            return new System.Windows.Size(Math.Max(10, sourceWidth * scale), Math.Max(10, sourceHeight * scale));
        }

        public System.Windows.Size GetZoomSize(double targetWidth, double fallbackHeight, double minDimension, double minZoomPercent, double maxZoomPercent, double baseWidth)
        {
            var minimumWidth = Math.Max(minDimension, baseWidth * (minZoomPercent / 100.0));
            var maximumWidth = baseWidth * (maxZoomPercent / 100.0);
            var clampedWidth = Math.Max(minimumWidth, Math.Min(maximumWidth, targetWidth));
            var aspectRatio = GetAspectRatio();
            var targetHeight = aspectRatio > 0 ? clampedWidth / aspectRatio : fallbackHeight;

            return new System.Windows.Size(clampedWidth, Math.Max(minDimension, targetHeight));
        }

        public double GetAspectRatio()
        {
            return Bitmap.Height > 0 ? (double)Bitmap.Width / Bitmap.Height : 1.0;
        }

        public void RotateClockwise() => Rotate(RotateFlipType.Rotate90FlipNone);

        public void RotateCounterClockwise() => Rotate(RotateFlipType.Rotate270FlipNone);

        public void Save()
        {
            var extension = System.IO.Path.GetExtension(Path);
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");

            try
            {
                Bitmap.Save(tempPath, GetImageFormat(extension));
                File.Copy(tempPath, Path, true);
                _isModified = false;
                _bitmapSource = null;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private void Rotate(RotateFlipType type)
        {
            Bitmap.RotateFlip(type);
            _isModified = true;
            _bitmapSource = null;
        }

        private BitmapSource LoadBitmapSourceFromDisk()
        {
            return ImageBitmapFactory.CreateBitmapSourceFromFile(Path);
        }

        private static BitmapSource CreateBitmapSourceFromBitmap(Bitmap bitmap)
        {
            return ImageBitmapFactory.CreateBitmapSourceFromBitmap(bitmap);
        }

        private static ImageFormat GetImageFormat(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return ImageFormat.Jpeg;
                case ".png":
                    return ImageFormat.Png;
                case ".gif":
                    return ImageFormat.Gif;
                case ".bmp":
                    return ImageFormat.Bmp;
                case ".ico":
                    return ImageFormat.Icon;
                case ".tif":
                case ".tiff":
                    return ImageFormat.Tiff;
                case ".wmf":
                    return ImageFormat.Wmf;
                default:
                    return ImageFormat.Png;
            }
        }
    }
}
