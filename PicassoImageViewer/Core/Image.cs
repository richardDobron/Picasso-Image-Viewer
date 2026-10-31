using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicassoImageViewer.Core
{
    public class Image
    {
        private const uint MetadataPadding = 4096;

        public string Path { get; }
        public BitmapSource Source { get; private set; }
        public int PixelWidth => Source.PixelWidth;
        public int PixelHeight => Source.PixelHeight;

        /// <summary>Composed frames of an animated GIF, or null for still images.</summary>
        public IReadOnlyList<AnimationFrame> AnimationFrames { get; private set; }

        /// <summary>
        /// Whether a rotation can be written back to the file without losing pixels,
        /// frames or the file format. Otherwise rotation only affects the view.
        /// </summary>
        public bool CanSaveRotation { get; }

        private readonly string _extension;
        private int _fileOrientation = ExifOrientation.Normal;
        private int _unsavedRotation;
        private BitmapSource _pixelSource;

        public Image(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            Path = path;
            _extension = (System.IO.Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            Source = ImageBitmapFactory.CreateBitmapSourceFromFile(path);

            var frameCount = 1;
            try
            {
                using (var stream = OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    frameCount = decoder.Frames.Count;
                    _fileOrientation = ExifOrientation.Read(decoder.Frames[0].Metadata as BitmapMetadata);
                }
            }
            catch (Exception)
            {
                frameCount = 0;
            }

            CanSaveRotation = frameCount == 1 && IsSavableFormat(_extension);

            if (frameCount > 1 && _extension == ".gif")
                AnimationFrames = GifAnimation.TryLoad(path);
        }

        public Size GetFittedSize(double sourceWidth, double sourceHeight, double maxWidth, double maxHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return new Size(10, 10);

            var scale = Math.Min(1.0, Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight));
            return new Size(Math.Max(10, sourceWidth * scale), Math.Max(10, sourceHeight * scale));
        }

        public Size GetZoomSize(double targetWidth, double fallbackHeight, double minDimension, double minZoomPercent, double maxZoomPercent, double baseWidth)
        {
            var minimumWidth = Math.Max(minDimension, baseWidth * (minZoomPercent / 100.0));
            var maximumWidth = baseWidth * (maxZoomPercent / 100.0);
            var clampedWidth = Math.Max(minimumWidth, Math.Min(maximumWidth, targetWidth));
            var aspectRatio = GetAspectRatio();
            var targetHeight = aspectRatio > 0 ? clampedWidth / aspectRatio : fallbackHeight;

            return new Size(clampedWidth, Math.Max(minDimension, targetHeight));
        }

        public double GetAspectRatio()
        {
            return PixelHeight > 0 ? (double)PixelWidth / PixelHeight : 1.0;
        }

        public Color GetPixel(int x, int y)
        {
            if (_pixelSource == null)
            {
                var converted = new FormatConvertedBitmap(Source, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                _pixelSource = converted;
            }

            x = Math.Max(0, Math.Min(PixelWidth - 1, x));
            y = Math.Max(0, Math.Min(PixelHeight - 1, y));

            var pixel = new byte[4];
            _pixelSource.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }

        public void RotateClockwise() => Rotate(90);

        public void RotateCounterClockwise() => Rotate(270);

        /// <summary>
        /// Writes pending rotation to disk. JPEG and TIFF only get their EXIF orientation
        /// tag updated, so pixels and metadata stay untouched.
        /// </summary>
        public void Save()
        {
            if (!CanSaveRotation)
                throw new NotSupportedException($"Saving {_extension} files is not supported.");

            if (_unsavedRotation == 0)
                return;

            if (UsesOrientationTag(_extension))
            {
                var orientation = ExifOrientation.Rotate(_fileOrientation, _unsavedRotation);
                var updated = TryWriteOrientationInPlace(orientation);
                if (updated != null)
                    ReplaceFile(stream => stream.Write(updated, 0, updated.Length));
                else
                    ReplaceFile(stream => WriteWithOrientation(stream, orientation));

                _fileOrientation = orientation;
            }
            else
            {
                ReplaceFile(WriteRotatedPixels);
            }

            _unsavedRotation = 0;
        }

        private void Rotate(int angle)
        {
            Source = RotateBitmap(Source, angle);
            _pixelSource = null;
            _unsavedRotation = (_unsavedRotation + angle) % 360;

            if (AnimationFrames != null)
                AnimationFrames = AnimationFrames.Select(frame => new AnimationFrame(RotateBitmap(frame.Source, angle), frame.Delay)).ToList();
        }

        private static BitmapSource RotateBitmap(BitmapSource source, int angle)
        {
            var rotated = new TransformedBitmap(source, new RotateTransform(angle));
            rotated.Freeze();
            return rotated;
        }

        /// <summary>
        /// Updates the orientation tag within the existing metadata space, leaving every other
        /// byte as is. Returns the updated file bytes, or null when there's no room for the tag.
        /// </summary>
        private byte[] TryWriteOrientationInPlace(int orientation)
        {
            // Works on an in-memory copy: WPF keeps its own handle to a FileStream's file
            // until finalization, which would block replacing the file afterwards.
            var bytes = File.ReadAllBytes(Path);

            try
            {
                using (var stream = new MemoryStream(bytes, true))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    var query = ExifOrientation.GetQuery((frame.Metadata as BitmapMetadata)?.Format);
                    if (query == null)
                        return null;

                    var writer = frame.CreateInPlaceBitmapMetadataWriter();
                    writer.SetQuery(query, (ushort)orientation);
                    return writer.TrySave() ? bytes : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void WriteWithOrientation(Stream output, int orientation)
        {
            var original = new MemoryStream(File.ReadAllBytes(Path));
            var decoder = BitmapDecoder.Create(original, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var isJpeg = _extension == ".jpg" || _extension == ".jpeg";

            var metadata = (frame.Metadata as BitmapMetadata)?.Clone() ?? new BitmapMetadata(isJpeg ? "jpg" : "tiff");
            metadata.SetQuery(isJpeg ? ExifOrientation.JpegQuery : ExifOrientation.TiffQuery, (ushort)orientation);
            if (isJpeg)
                AddMetadataPadding(metadata);

            BitmapEncoder encoder = isJpeg
                ? (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 95 }
                : new TiffBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame, frame.Thumbnail, metadata, frame.ColorContexts));
            encoder.Save(output);
        }

        private static void AddMetadataPadding(BitmapMetadata metadata)
        {
            foreach (var query in new[] { "/app1/ifd/PaddingSchema:Padding", "/app1/ifd/exif/PaddingSchema:Padding", "/xmp/PaddingSchema:Padding" })
            {
                try
                {
                    metadata.SetQuery(query, MetadataPadding);
                }
                catch (Exception)
                {
                    // Padding is an optimisation only.
                }
            }
        }

        private void WriteRotatedPixels(Stream output)
        {
            var encoder = CreateLosslessEncoder(_extension);
            encoder.Frames.Add(BitmapFrame.Create(Source));
            encoder.Save(output);
        }

        private void ReplaceFile(Action<Stream> write)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            var tempPath = System.IO.Path.Combine(directory ?? System.IO.Path.GetTempPath(), $".{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                    write(stream);

                File.Copy(tempPath, Path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        private static bool UsesOrientationTag(string extension)
        {
            return extension == ".jpg" || extension == ".jpeg" || extension == ".tif" || extension == ".tiff";
        }

        private static bool IsSavableFormat(string extension)
        {
            return UsesOrientationTag(extension) || extension == ".png" || extension == ".bmp" || extension == ".gif";
        }

        private static BitmapEncoder CreateLosslessEncoder(string extension)
        {
            switch (extension)
            {
                case ".png":
                    return new PngBitmapEncoder();
                case ".gif":
                    return new GifBitmapEncoder();
                case ".bmp":
                    return new BmpBitmapEncoder();
                default:
                    throw new NotSupportedException($"Saving {extension} files is not supported.");
            }
        }
    }
}
