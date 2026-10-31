using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PicassoImageViewer.Core
{
    public sealed class AnimationFrame
    {
        public AnimationFrame(BitmapSource source, TimeSpan delay)
        {
            Source = source;
            Delay = delay;
        }

        public BitmapSource Source { get; }
        public TimeSpan Delay { get; }
    }

    /// <summary>
    /// Composes the frames of an animated GIF into full, ready-to-show images, since WPF
    /// only ever displays the first frame.
    /// </summary>
    internal static class GifAnimation
    {
        private const long MaxComposedBytes = 256L * 1024 * 1024;
        private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

        private const int DisposeToBackground = 2;
        private const int DisposeToPrevious = 3;

        public static IReadOnlyList<AnimationFrame> TryLoad(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count < 2)
                        return null;

                    var width = GetUShort(decoder.Metadata, "/logscrdesc/Width") ?? decoder.Frames[0].PixelWidth;
                    var height = GetUShort(decoder.Metadata, "/logscrdesc/Height") ?? decoder.Frames[0].PixelHeight;
                    if (width <= 0 || height <= 0 || (long)width * height * 4 * decoder.Frames.Count > MaxComposedBytes)
                        return null;

                    return Compose(decoder.Frames, width, height);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ObjectAnimationUsingKeyFrames CreateAnimation(IReadOnlyList<AnimationFrame> frames)
        {
            var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            var time = TimeSpan.Zero;

            foreach (var frame in frames)
            {
                animation.KeyFrames.Add(new DiscreteObjectKeyFrame(frame.Source, KeyTime.FromTimeSpan(time)));
                time += frame.Delay;
            }

            animation.Duration = time;
            animation.Freeze();
            return animation;
        }

        private static IReadOnlyList<AnimationFrame> Compose(IList<BitmapFrame> frames, int width, int height)
        {
            var stride = width * 4;
            var canvas = new byte[stride * height];
            var result = new List<AnimationFrame>(frames.Count);

            foreach (var frame in frames)
            {
                var metadata = frame.Metadata as BitmapMetadata;
                var left = GetUShort(metadata, "/imgdesc/Left") ?? 0;
                var top = GetUShort(metadata, "/imgdesc/Top") ?? 0;
                var delay = GetUShort(metadata, "/grctlext/Delay") ?? 0;
                var disposal = GetByte(metadata, "/grctlext/Disposal") ?? 0;

                var previous = disposal == DisposeToPrevious ? (byte[])canvas.Clone() : null;
                var frameRect = DrawFrame(canvas, width, height, frame, left, top);

                var composed = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, canvas, stride);
                composed.Freeze();
                result.Add(new AnimationFrame(composed, delay > 1 ? TimeSpan.FromMilliseconds(delay * 10) : DefaultDelay));

                if (disposal == DisposeToBackground)
                    ClearRect(canvas, width, frameRect);
                else if (previous != null)
                    canvas = previous;
            }

            return result;
        }

        private static Int32Rect DrawFrame(byte[] canvas, int width, int height, BitmapSource frame, int left, int top)
        {
            var frameWidth = Math.Min(frame.PixelWidth, width - left);
            var frameHeight = Math.Min(frame.PixelHeight, height - top);
            if (frameWidth <= 0 || frameHeight <= 0)
                return Int32Rect.Empty;

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var frameStride = frame.PixelWidth * 4;
            var pixels = new byte[frameStride * frame.PixelHeight];
            converted.CopyPixels(pixels, frameStride, 0);

            for (var y = 0; y < frameHeight; y++)
            {
                var source = y * frameStride;
                var target = ((top + y) * width + left) * 4;
                for (var x = 0; x < frameWidth; x++, source += 4, target += 4)
                {
                    if (pixels[source + 3] == 0)
                        continue;

                    canvas[target] = pixels[source];
                    canvas[target + 1] = pixels[source + 1];
                    canvas[target + 2] = pixels[source + 2];
                    canvas[target + 3] = pixels[source + 3];
                }
            }

            return new Int32Rect(left, top, frameWidth, frameHeight);
        }

        private static void ClearRect(byte[] canvas, int width, Int32Rect rect)
        {
            for (var y = rect.Y; y < rect.Y + rect.Height; y++)
                Array.Clear(canvas, (y * width + rect.X) * 4, rect.Width * 4);
        }

        private static int? GetUShort(BitmapMetadata metadata, string query)
        {
            try
            {
                return metadata?.GetQuery(query) is ushort value ? value : (int?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int? GetByte(BitmapMetadata metadata, string query)
        {
            try
            {
                return metadata?.GetQuery(query) is byte value ? value : (int?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
