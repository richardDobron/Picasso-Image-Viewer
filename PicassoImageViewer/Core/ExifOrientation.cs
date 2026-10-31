using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicassoImageViewer.Core
{
    /// <summary>
    /// EXIF orientation (tag 274). Values 1-8 describe how the stored pixels must be
    /// mirrored horizontally and then rotated clockwise to be displayed upright.
    /// </summary>
    internal static class ExifOrientation
    {
        public const int Normal = 1;

        public const string JpegQuery = "/app1/ifd/{ushort=274}";
        public const string TiffQuery = "/ifd/{ushort=274}";

        private static readonly (int Angle, bool Mirrored)[] Transforms =
        {
            (0, false),   // unused
            (0, false),   // 1
            (0, true),    // 2: mirror horizontal
            (180, false), // 3
            (180, true),  // 4: mirror vertical
            (270, true),  // 5: transpose
            (90, false),  // 6
            (90, true),   // 7: transverse
            (270, false), // 8
        };

        public static int Read(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    return Read(decoder.Frames[0].Metadata as BitmapMetadata);
                }
            }
            catch (Exception)
            {
                return Normal;
            }
        }

        public static int Read(BitmapMetadata metadata)
        {
            if (metadata == null)
                return Normal;

            try
            {
                var query = GetQuery(metadata.Format);
                if (query != null && metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort value && value >= 1 && value <= 8)
                    return value;
            }
            catch (Exception)
            {
            }

            return Normal;
        }

        public static string GetQuery(string metadataFormat)
        {
            switch (metadataFormat)
            {
                case "jpg":
                    return JpegQuery;
                case "tiff":
                    return TiffQuery;
                default:
                    return null;
            }
        }

        public static BitmapSource Apply(BitmapSource source, int orientation)
        {
            if (orientation <= Normal || orientation >= Transforms.Length)
                return source;

            var (angle, mirrored) = Transforms[orientation];
            var transform = new TransformGroup();
            if (mirrored)
                transform.Children.Add(new ScaleTransform(-1, 1));
            if (angle != 0)
                transform.Children.Add(new RotateTransform(angle));

            var oriented = new TransformedBitmap(source, transform);
            oriented.Freeze();
            return oriented;
        }

        public static int Rotate(int orientation, int clockwiseAngle)
        {
            if (orientation < Normal || orientation >= Transforms.Length)
                orientation = Normal;

            var (angle, mirrored) = Transforms[orientation];
            var targetAngle = ((angle + clockwiseAngle) % 360 + 360) % 360;

            for (var value = Normal; value < Transforms.Length; value++)
            {
                if (Transforms[value].Angle == targetAngle && Transforms[value].Mirrored == mirrored)
                    return value;
            }

            return Normal;
        }
    }
}
