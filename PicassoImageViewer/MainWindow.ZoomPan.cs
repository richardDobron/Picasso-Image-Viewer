using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PicassoImageViewer
{
    public partial class MainWindow
    {
        private void WindowMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ApplyZoomFromWheel(e.Delta);
        }

        private void ZoomInButtonClick(object sender, RoutedEventArgs e)
        {
            ApplyZoomFactor(ZoomStepFactor);
        }

        private void ResetZoomButtonClick(object sender, RoutedEventArgs e)
        {
            ResetZoomToFit();
        }

        private void ResetZoomToFit()
        {
            if (_image == null)
                return;

            ApplyImageLayout(resetTransforms: false, updateZoomText: false);
            AnimateZoomTo(1.0, new Point(0, 0));
        }

        private void ZoomOutButtonClick(object sender, RoutedEventArgs e)
        {
            ApplyZoomFactor(1.0 / ZoomStepFactor);
        }

        private void ShowZoomOverlayForWidth(double displayedWidth)
        {
            if (_image == null)
                return;

            var zoomPercent = GetZoomPercentForDisplayedWidth(displayedWidth);
            StartZoomOverlayTextAnimation(zoomPercent);
            zoomOverlay.Visibility = Visibility.Visible;
            _zoomOverlayTimer.Stop();
            _zoomOverlayTimer.Start();
        }

        private void StartZoomOverlayTextAnimation(double targetPercent)
        {
            _zoomOverlayPercentFrom = TryParseZoomPercent(zoomOverlayText.Text);
            _zoomOverlayPercentTo = Math.Max(MinZoomPercent, Math.Min(MaxZoomPercent, targetPercent));
            _zoomOverlayAnimationStartTick = Environment.TickCount;
            _zoomOverlayTextTimer.Stop();
            _zoomOverlayTextTimer.Start();
        }

        private void AnimateZoomOverlayTextTick(object sender, EventArgs e)
        {
            var elapsed = Environment.TickCount - _zoomOverlayAnimationStartTick;
            var progress = Math.Min(1.0, elapsed / (double)ZoomOverlayAnimationDurationMs);
            var easedProgress = 1.0 - Math.Pow(1.0 - progress, 3.0);
            var currentPercent = _zoomOverlayPercentFrom + (_zoomOverlayPercentTo - _zoomOverlayPercentFrom) * easedProgress;
            zoomOverlayText.Text = $"{Math.Round(currentPercent)}%";

            if (progress >= 1.0)
            {
                _zoomOverlayTextTimer.Stop();
                zoomOverlayText.Text = $"{Math.Round(_zoomOverlayPercentTo)}%";
            }
        }

        private double TryParseZoomPercent(string text)
        {
            if (double.TryParse(text?.Replace("%", string.Empty), out var percent))
                return percent;

            return 100.0;
        }

        private void ApplyZoomFromWheel(int delta)
        {
            if (_image == null || delta == 0)
                return;

            var factor = Math.Pow(ZoomStepFactor, delta / 120.0);
            ApplyZoomFactor(factor, Mouse.GetPosition(mainGrid));
        }

        private static Point ClampPointToBounds(Point point, double width, double height)
        {
            return new Point(
                Math.Max(0.0, Math.Min(width, point.X)),
                Math.Max(0.0, Math.Min(height, point.Y)));
        }

        private void ApplyZoomFactor(double factor, Point? zoomAnchor = null)
        {
            if (_image == null || factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
                return;

            EnsureBaseImageSize();
            StopAllImageAnimations();

            var currentScale = GetCurrentZoomScale();
            var targetScale = GetClampedZoomScale(currentScale * factor);
            if (Math.Abs(targetScale - currentScale) < 0.0001)
                return;

            var currentWidth = _baseImageWidth * currentScale;
            var currentHeight = _baseImageHeight * currentScale;
            var targetWidth = _baseImageWidth * targetScale;
            var targetHeight = _baseImageHeight * targetScale;

            var viewportWidth = Math.Max(1.0, mainGrid.ActualWidth);
            var viewportHeight = Math.Max(1.0, mainGrid.ActualHeight);

            Point? desiredTranslate = null;

            if (zoomAnchor.HasValue)
            {
                var anchor = ClampPointToBounds(zoomAnchor.Value, viewportWidth, viewportHeight);
                var currentOrigin = GetCurrentImageVisualOrigin();

                var relativeX = (anchor.X - currentOrigin.X) / Math.Max(1.0, currentWidth);
                var relativeY = (anchor.Y - currentOrigin.Y) / Math.Max(1.0, currentHeight);

                if (relativeX >= 0.0 && relativeX <= 1.0 && relativeY >= 0.0 && relativeY <= 1.0)
                {
                    var targetOriginX = (viewportWidth - targetWidth) / 2.0;
                    var targetOriginY = (viewportHeight - targetHeight) / 2.0;
                    desiredTranslate = new Point(
                        anchor.X - targetOriginX - (relativeX * targetWidth),
                        anchor.Y - targetOriginY - (relativeY * targetHeight));
                }
            }

            AnimateZoomTo(targetScale, desiredTranslate);
        }

        private Point GetCurrentImageVisualOrigin()
        {
            try
            {
                var imageOrigin = image.TransformToAncestor(mainGrid).Transform(new Point(0, 0));
                if (!double.IsNaN(imageOrigin.X) && !double.IsNaN(imageOrigin.Y))
                    return imageOrigin;
            }
            catch
            {
            }

            var viewportWidth = Math.Max(1.0, mainGrid.ActualWidth);
            var viewportHeight = Math.Max(1.0, mainGrid.ActualHeight);
            return new Point(
                (viewportWidth - GetCurrentImageWidth()) / 2.0 + _imageTranslate.X,
                (viewportHeight - GetCurrentImageHeight()) / 2.0 + _imageTranslate.Y);
        }

        private void AnimateZoomTo(double targetScale, Point? targetTranslate = null)
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            _imageScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, _zoomAnimationDuration) { EasingFunction = easing });
            _imageScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, _zoomAnimationDuration) { EasingFunction = easing });

            var targetWidth = _baseImageWidth * targetScale;
            var targetHeight = _baseImageHeight * targetScale;
            var desiredTranslate = targetTranslate ?? new Point(_imageTranslate.X, _imageTranslate.Y);
            var translateToApply = GetClampedTranslation(desiredTranslate, targetWidth, targetHeight);

            _imageTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateToApply.X, _zoomAnimationDuration) { EasingFunction = easing });
            _imageTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateToApply.Y, _zoomAnimationDuration) { EasingFunction = easing });

            ShowZoomOverlayForWidth(targetWidth);
        }

        private double GetCurrentZoomScale()
        {
            var scale = _imageScale.ScaleX;
            return scale > 0 && !double.IsNaN(scale) ? scale : 1.0;
        }

        private double GetCurrentImageWidth()
        {
            return _baseImageWidth * GetCurrentZoomScale();
        }

        private double GetCurrentImageHeight()
        {
            return _baseImageHeight * GetCurrentZoomScale();
        }

        private double GetZoomPercentForDisplayedWidth(double displayedWidth)
        {
            EnsureBaseImageSize();
            return _baseImageWidth > 0 ? (displayedWidth / _baseImageWidth) * 100.0 : 100.0;
        }

        private double GetClampedZoomScale(double targetScale)
        {
            var minScaleForMinDimension = _baseImageWidth > 0 ? MinImageDimension / _baseImageWidth : 0.0;
            var minScale = Math.Max(minScaleForMinDimension, MinZoomPercent / 100.0);
            var maxScale = MaxZoomPercent / 100.0;
            return Math.Max(minScale, Math.Min(maxScale, targetScale));
        }

        private Size GetFittedImageSize(double sourceWidth, double sourceHeight)
        {
            if (_image == null || sourceWidth <= 0 || sourceHeight <= 0)
                return new Size(MinImageDimension, MinImageDimension);

            var availableWidth = Math.Max(200.0, ActualWidth > 0 ? ActualWidth * 0.7 : SystemParameters.PrimaryScreenWidth * 0.7);
            var availableHeight = Math.Max(200.0, ActualHeight > 0 ? ActualHeight * 0.7 : SystemParameters.PrimaryScreenHeight * 0.7);
            return _image.GetFittedSize(sourceWidth, sourceHeight, availableWidth, availableHeight);
        }

        private void EnsureBaseImageSize()
        {
            if (_image == null || (_baseImageWidth > 0 && _baseImageHeight > 0))
                return;

            var fittedSize = GetFittedImageSize(_image.PixelWidth, _image.PixelHeight);
            _baseImageWidth = fittedSize.Width;
            _baseImageHeight = fittedSize.Height;
        }

        private void ClampImageTranslation()
        {
            ClampImageTranslation(GetCurrentImageWidth(), GetCurrentImageHeight());
        }

        private void ClampImageTranslation(double targetWidth, double targetHeight)
        {
            if (_image == null)
                return;

            var current = StopTranslateAnimationAndGetCurrentValue();
            var clamped = GetClampedTranslation(current, targetWidth, targetHeight);
            _imageTranslate.X = clamped.X;
            _imageTranslate.Y = clamped.Y;
        }

        private void StopAllImageAnimations()
        {
            var currentScale = GetCurrentZoomScale();
            _imageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _imageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _imageScale.ScaleX = currentScale;
            _imageScale.ScaleY = currentScale;
            StopTranslateAnimationAndGetCurrentValue();
        }

        private Point StopTranslateAnimationAndGetCurrentValue()
        {
            var current = new Point(_imageTranslate.X, _imageTranslate.Y);
            _imageTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _imageTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _imageTranslate.X = current.X;
            _imageTranslate.Y = current.Y;
            return current;
        }

        private Point GetClampedTranslation(Point translate, double targetWidth, double targetHeight)
        {
            var viewportWidth = mainGrid.ActualWidth;
            var viewportHeight = mainGrid.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
                return translate;

            var horizontalLimit = Math.Abs(targetWidth - viewportWidth) / 2.0;
            var verticalLimit = Math.Abs(targetHeight - viewportHeight) / 2.0;

            var x = Math.Max(-horizontalLimit, Math.Min(horizontalLimit, translate.X));
            var y = Math.Max(-verticalLimit, Math.Min(verticalLimit, translate.Y));

            return new Point(x, y);
        }

        private void UpdateZoomTextForWidth(double displayedWidth)
        {
            var zoomPercent = GetZoomPercentForDisplayedWidth(displayedWidth);
            zoomOverlayText.Text = $"{Math.Round(zoomPercent)}%";
        }

        private void MainGridMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_image == null || image.Source == null)
                return;

            if (_pickerWindow.IsVisible)
            {
                SampleColorFromPoint(e.GetPosition(image));
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 2)
            {
                ToggleActualSizeZoom();
                return;
            }

            StopAllImageAnimations();
            _isDraggingImage = true;
            _lastDragPosition = e.GetPosition(mainGrid);
            mainGrid.CaptureMouse();
            image.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void ToggleActualSizeZoom()
        {
            if (_image == null)
                return;

            EnsureBaseImageSize();
            StopAllImageAnimations();

            var currentScale = GetCurrentZoomScale();
            var actualSizeScale = _baseImageWidth > 0 ? _image.PixelWidth / _baseImageWidth : 1.0;
            var isNearActualSize = Math.Abs(currentScale - actualSizeScale) < 0.001;

            var targetScale = isNearActualSize ? 1.0 : GetClampedZoomScale(actualSizeScale);
            AnimateZoomTo(targetScale, new Point(0, 0));
        }

        private void MainGridMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingImage)
                return;

            var currentPosition = e.GetPosition(mainGrid);
            var deltaX = currentPosition.X - _lastDragPosition.X;
            var deltaY = currentPosition.Y - _lastDragPosition.Y;

            _imageTranslate.X += deltaX;
            _imageTranslate.Y += deltaY;
            ClampImageTranslation();
            _lastDragPosition = currentPosition;
        }

        private void MainGridMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndImageDrag();
        }

        private void MainGridMouseLeave(object sender, MouseEventArgs e)
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                EndImageDrag();
        }

        private void EndImageDrag()
        {
            if (!_isDraggingImage)
                return;

            _isDraggingImage = false;
            mainGrid.ReleaseMouseCapture();
            image.Cursor = _pickerWindow.IsVisible ? Cursors.Cross : Cursors.Hand;
        }
    }
}
