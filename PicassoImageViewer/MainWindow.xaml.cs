using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace PicassoImageViewer
{
    public partial class MainWindow : Window
    {
        public static string[] Args = Environment.GetCommandLineArgs();

        private ColorPickerWindow _pickerWindow;
        private AboutWindow _aboutWindow;
        private Core.Image _image;
        private double _baseImageWidth;
        private double _baseImageHeight;
        private readonly string[] _supportedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tiff", ".wmf" };
        private List<string> _folderImagePaths = new List<string>();
        private string _currentImagePath;
        private bool _updatingFolderSelection;
        private bool _isFolderPanelVisible;
        private readonly DispatcherTimer _zoomOverlayTimer;
        private readonly DispatcherTimer _navigationHintTimer;
        private readonly DispatcherTimer _zoomOverlayTextTimer;
        private readonly DispatcherTimer _slideshowTimer;
        private readonly TranslateTransform _imageTranslate = new TranslateTransform();
        private readonly ScaleTransform _imageScale = new ScaleTransform(1.0, 1.0);
        private bool _isDraggingImage;
        private Point _lastDragPosition;
        private double _zoomOverlayPercentFrom;
        private double _zoomOverlayPercentTo;
        private int _zoomOverlayAnimationStartTick;
        private readonly Duration _zoomAnimationDuration = new Duration(TimeSpan.FromMilliseconds(220));
        private readonly Dictionary<string, BitmapSource> _thumbnailCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private string _currentDirectory;
        private int _thumbnailLoadGeneration;
        private const double ZoomStepFactor = 1.15;
        private const double MinImageDimension = 10;
        private const double MinZoomPercent = 1;
        private const double MaxZoomPercent = 3200;
        private const int ZoomOverlayAnimationDurationMs = 140;
        private bool _isSlideshowActive;

        public MainWindow()
        {
            InitializeComponent();
            ApplyUiTranslations();

            _pickerWindow = new ColorPickerWindow();
            _pickerWindow.IsVisibleChanged += PickerWindowChanged;
            _aboutWindow = new AboutWindow();

            infoBtn.Click += (sender, eventArgs) => _aboutWindow.Show();

            var imageTransform = new TransformGroup();
            imageTransform.Children.Add(_imageScale);
            imageTransform.Children.Add(_imageTranslate);
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            image.RenderTransform = imageTransform;

            _zoomOverlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _zoomOverlayTimer.Tick += (sender, eventArgs) =>
            {
                _zoomOverlayTimer.Stop();
                zoomOverlay.Visibility = Visibility.Collapsed;
            };

            _zoomOverlayTextTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _zoomOverlayTextTimer.Tick += AnimateZoomOverlayTextTick;

            _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _slideshowTimer.Tick += (sender, eventArgs) => NavigateImages(1);

            _navigationHintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _navigationHintTimer.Tick += (sender, eventArgs) =>
            {
                _navigationHintTimer.Stop();
                ShowCurrentImageLabel();
            };

            SizeChanged += (sender, eventArgs) => ClampImageTranslation();

            if (Args.Length > 1)
                OpenImage(Args[1]);
        }

        public void OpenImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var fullPath = Path.GetFullPath(path);
            Core.Image loadedImage;
            try
            {
                loadedImage = new Core.Image(fullPath);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Unable to open selected image.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _currentImagePath = fullPath;
            _image = loadedImage;

            ShowCurrentImageLabel();
            SetCurrentImageSource(_image.GetBitmapSource());
            grid.Visibility = Visibility.Hidden;

            PopulateFolderImages(_currentImagePath);
        }

        private void WindowDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var imageFile = files.FirstOrDefault(file =>
                    _supportedImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));

                if (imageFile != null)
                {
                    StopSlideshow();
                    OpenImage(imageFile);
                }
            }
        }

        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _slideshowTimer.Stop();
            _zoomOverlayTimer.Stop();
            _zoomOverlayTextTimer.Stop();
            _navigationHintTimer.Stop();

            _pickerWindow.Close();
            _aboutWindow.Close();
        }

        private void WindowMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ApplyZoomFromWheel(e.Delta);
        }

        private void OpenButtonClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter =
                    "All supported (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.ico;*.tiff;*.wmf)|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.ico;*.tiff;*.wmf|" +
                    "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
                    "Portable Network Graphic (*.png)|*.png|" +
                    "Graphics Interchange Format (*.gif)|*.gif|" +
                    "Icon (*.ico)|*.ico|" +
                    "Other (*.tiff;*.wmf)|*.tiff;*.wmf|" +
                    "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() is true)
            {
                StopSlideshow();
                OpenImage(dialog.FileName);
            }
        }

        private void ApplyUiTranslations()
        {
            Properties.Resources.Culture = CultureInfo.CurrentUICulture;

            openBtn.Content = GetResourceString("Open");
            noSelectedImageLabel.Content = GetResourceString("NoSelectedImage");
            openPromptText.Content = GetResourceString("Open");
            infoBtn.ToolTip = GetResourceString("Information");
            closeBtn.ToolTip = GetResourceString("Close");
            prevImageBtn.ToolTip = GetResourceString("PreviousImage");
            slideshowBtn.ToolTip = GetResourceString("PlaySlideshow");
            nextImageBtn.ToolTip = GetResourceString("NextImage");
            zoomInBtn.ToolTip = GetResourceString("ZoomIn");
            zoomReloadBtn.ToolTip = GetResourceString("ZoomRefresh");
            zoomOutBtn.ToolTip = GetResourceString("ZoomOut");
            rotateCounterClockwiseBtn.ToolTip = GetResourceString("RotateLeft");
            rotateClockwiseBtn.ToolTip = GetResourceString("RotateRight");
            colorPickerBtn.ToolTip = GetResourceString("ColorPicker");
        }

        private string GetResourceString(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key, Properties.Resources.Culture) ?? key;
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ZoomInButtonClick(object sender, RoutedEventArgs e)
        {
            ApplyZoomFactor(ZoomStepFactor);
        }

        private void ResetZoomButtonClick(object sender, RoutedEventArgs e)
        {
            ResetZoomToFit();
        }

        private void OpenColorPicker(object sender, RoutedEventArgs e)
        {
            SetColorPickerActive(!_pickerWindow.IsVisible);
        }

        internal void SetColorPickerActive(bool active)
        {
            image.Cursor = active ? Cursors.Cross : Cursors.Hand;
            if (active)
            {
                _pickerWindow.Show();
            }
            else
            {
                _pickerWindow.Hide();
            }
        }

        private void ResetZoomToFit()
        {
            if (_image?.Bitmap == null)
                return;

            ApplyImageLayout(resetTransforms: false, updateZoomText: false);
            AnimateZoomTo(1.0, new Point(0, 0));
        }

        private void ZoomOutButtonClick(object sender, RoutedEventArgs e)
        {
            ApplyZoomFactor(1.0 / ZoomStepFactor);
        }

        private void RotateCounterClockwiseButtonClick(object sender, RoutedEventArgs e)
        {
            RotateImage(clockwise: false);
        }

        private void RotateClockwiseButtonClick(object sender, RoutedEventArgs e)
        {
            RotateImage(clockwise: true);
        }

        private void RotateImage(bool clockwise)
        {
            if (_image?.Bitmap == null)
                return;

            if (clockwise)
                _image.RotateClockwise();
            else
                _image.RotateCounterClockwise();

            TryPersistCurrentImage();

            RefreshCurrentThumbnail();
            ShowCurrentImageLabel();
            SetCurrentImageSource(_image.GetBitmapSource());
        }

        private void ApplyImageLayout(bool resetTransforms = true, bool updateZoomText = true)
        {
            if (_image?.Bitmap == null)
                return;

            StopAllImageAnimations();
            if (resetTransforms)
            {
                _imageScale.ScaleX = 1.0;
                _imageScale.ScaleY = 1.0;
                _imageTranslate.X = 0;
                _imageTranslate.Y = 0;
            }

            var fittedSize = GetFittedImageSize(_image.Bitmap.Width, _image.Bitmap.Height);
            _baseImageWidth = fittedSize.Width;
            _baseImageHeight = fittedSize.Height;
            image.Width = fittedSize.Width;
            image.Height = fittedSize.Height;

            if (updateZoomText)
                UpdateZoomTextForWidth(fittedSize.Width);
        }

        private void SetCurrentImageSource(BitmapSource source, bool resetTransforms = true, bool updateZoomText = true)
        {
            image.Source = source;
            ApplyImageLayout(resetTransforms, updateZoomText);
        }

        private void PopulateFolderImages(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                _folderImagePaths.Clear();
                folderImagesList.Items.Clear();
                HideFolderPanelAnimated();
                _currentDirectory = null;
                StopSlideshow();
                return;
            }

            var fullPath = Path.GetFullPath(path);

            if (string.Equals(_currentDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                var existingIndex = _folderImagePaths.FindIndex(filePath =>
                    string.Equals(filePath, fullPath, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    SelectFolderImage(existingIndex);
                    return;
                }
            }
            else
            {
                _thumbnailCache.Clear();
            }

            _folderImagePaths = Directory.EnumerateFiles(directory)
                .Where(file => _supportedImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .Select(Path.GetFullPath)
                .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _currentDirectory = directory;

            _updatingFolderSelection = true;
            folderImagesList.Items.Clear();

            foreach (var imagePath in _folderImagePaths)
            {
                folderImagesList.Items.Add(new ListBoxItem
                {
                    Content = CreateThumbnailPlaceholder(),
                    Tag = imagePath
                });
            }

            var selectedIndex = _folderImagePaths.FindIndex(filePath =>
                string.Equals(filePath, fullPath, StringComparison.OrdinalIgnoreCase));

            if (selectedIndex >= 0)
                folderImagesList.SelectedIndex = selectedIndex;

            if (_folderImagePaths.Count > 1)
            {
                CenterCurrentThumbnail();
                StartThumbnailLoading(selectedIndex >= 0 ? selectedIndex : 0);
                ShowFolderPanelAnimated();
            }
            else
            {
                HideFolderPanelAnimated();
                StopSlideshow();
            }

            _updatingFolderSelection = false;
        }

        private FrameworkElement CreateThumbnailPlaceholder()
        {
            return new Border
            {
                Width = 66,
                Height = 44,
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0))
            };
        }

        private void FolderImagesListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingFolderSelection || !(folderImagesList.SelectedItem is ListBoxItem selectedItem))
                return;

            StopSlideshow();
            CenterCurrentThumbnail();

            if (selectedItem.Tag is string selectedPath && !string.IsNullOrEmpty(selectedPath))
            {
                if (!string.Equals(selectedPath, _currentImagePath, StringComparison.OrdinalIgnoreCase))
                    OpenImage(selectedPath);
            }
        }

        private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.O)
            {
                OpenButtonClick(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (_image != null && e.Key == Key.Up)
            {
                ApplyZoomFactor(ZoomStepFactor);
                e.Handled = true;
            }
            else if (_image != null && e.Key == Key.Down)
            {
                ApplyZoomFactor(1.0 / ZoomStepFactor);
                e.Handled = true;
            }
            else if (_folderImagePaths.Count > 0 && e.Key == Key.Left)
            {
                NavigateImages(-1);
                e.Handled = true;
            }
            else if (_folderImagePaths.Count > 0 && e.Key == Key.Right)
            {
                NavigateImages(1);
                e.Handled = true;
            }
            else if (_image != null && (e.Key == Key.D0 || e.Key == Key.NumPad0))
            {
                ResetZoomToFit();
                e.Handled = true;
            }
            else if (_image != null && e.Key == Key.OemOpenBrackets)
            {
                RotateImage(clockwise: false);
                e.Handled = true;
            }
            else if (_image != null && e.Key == Key.OemCloseBrackets)
            {
                RotateImage(clockwise: true);
                e.Handled = true;
            }
            else if (_image != null && e.Key == Key.Delete)
            {
                DeleteCurrentImage();
                e.Handled = true;
            }
            else if (_image != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.P)
            {
                PrintCurrentImage();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SetColorPickerActive(false);
                e.Handled = true;
            }
        }

        private void NavigateImages(int offset)
        {
            var currentIndex = _folderImagePaths.FindIndex(path =>
                string.Equals(path, _currentImagePath, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
                return;

            var nextIndex = currentIndex + offset;
            if (_folderImagePaths.Count > 0)
            {
                if (nextIndex < 0)
                    nextIndex = _folderImagePaths.Count - 1;
                else if (nextIndex >= _folderImagePaths.Count)
                    nextIndex = 0;
            }
            else if (nextIndex < 0 || nextIndex >= _folderImagePaths.Count)
            {
                return;
            }

            SelectFolderImage(nextIndex);
        }

        private void SelectFolderImage(int index)
        {
            if (index < 0 || index >= folderImagesList.Items.Count)
                return;

            _updatingFolderSelection = true;
            folderImagesList.SelectedIndex = index;
            _updatingFolderSelection = false;
            CenterCurrentThumbnail();
            StartThumbnailLoading(index);

            var selectedPath = _folderImagePaths[index];
            if (!string.Equals(selectedPath, _currentImagePath, StringComparison.OrdinalIgnoreCase))
                OpenImage(selectedPath);
        }

        private void HideFolderPanelAnimated()
        {
            _isFolderPanelVisible = false;

            if (folderPanel.Visibility != Visibility.Collapsed)
            {
                var duration = new Duration(TimeSpan.FromMilliseconds(300));

                var fadeOut = new DoubleAnimation(0, duration);
                fadeOut.Completed += (sender, eventArgs) =>
                {
                    if (folderPanel.Opacity == 0 && !_isSlideshowActive)
                        folderPanel.Visibility = Visibility.Collapsed;
                };
                folderPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                folderPanelTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, duration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                });
            }

            UpdateNavigationButtons();
        }

        private void ShowFolderPanelAnimated()
        {
            folderPanel.Visibility = Visibility.Visible;
            _isFolderPanelVisible = true;

            var duration = new Duration(TimeSpan.FromMilliseconds(300));

            folderPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration));
            folderPanelTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            UpdateNavigationButtons();
        }

        private async void StartThumbnailLoading(int selectedIndex)
        {
            var generation = ++_thumbnailLoadGeneration;
            var loadOrder = BuildThumbnailLoadOrder(selectedIndex);
            var loadedInBatch = 0;

            foreach (var index in loadOrder)
            {
                if (generation != _thumbnailLoadGeneration)
                    return;

                if (!(folderImagesList.Items[index] is ListBoxItem item) || !(item.Tag is string imagePath) || string.IsNullOrEmpty(imagePath))
                    continue;

                if (!_thumbnailCache.TryGetValue(imagePath, out var thumbnailSource))
                {
                    thumbnailSource = await Task.Run(() => CreateThumbnailBitmapSource(imagePath));
                    if (generation != _thumbnailLoadGeneration)
                        return;

                    if (thumbnailSource != null)
                        _thumbnailCache[imagePath] = thumbnailSource;
                }

                if (thumbnailSource != null)
                    item.Content = CreateThumbnailContent(thumbnailSource);

                if (++loadedInBatch >= 8)
                {
                    loadedInBatch = 0;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
        }

        private List<int> BuildThumbnailLoadOrder(int selectedIndex)
        {
            var order = new List<int>();
            var total = _folderImagePaths.Count;
            if (total == 0)
                return order;

            var center = selectedIndex >= 0 ? selectedIndex : 0;
            order.Add(center);

            for (var offset = 1; offset < total; offset++)
            {
                var left = center - offset;
                var right = center + offset;

                if (left >= 0)
                    order.Add(left);
                if (right < total)
                    order.Add(right);
            }

            return order;
        }

        private BitmapSource CreateThumbnailBitmapSource(string imagePath)
        {
            try
            {
                return Core.ImageBitmapFactory.CreateBitmapSourceFromFile(imagePath, 110);
            }
            catch
            {
                return null;
            }
        }

        private FrameworkElement CreateThumbnailContent(BitmapSource thumbnailSource)
        {
            return new Border
            {
                Width = 66,
                Height = 44,
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                Child = new System.Windows.Controls.Image
                {
                    Width = 66,
                    Height = 44,
                    Stretch = Stretch.UniformToFill,
                    Source = thumbnailSource
                }
            };
        }

        private void CenterCurrentThumbnail()
        {
            CenterCurrentThumbnailWithRetry(3);
        }

        private void CenterCurrentThumbnailWithRetry(int remainingRetries)
        {
            if (folderImagesList.SelectedItem == null)
                return;

            folderImagesList.UpdateLayout();
            folderImagesList.ScrollIntoView(folderImagesList.SelectedItem);
            folderImagesList.UpdateLayout();

            var selectedItem = folderImagesList.ItemContainerGenerator.ContainerFromItem(folderImagesList.SelectedItem) as ListBoxItem;
            var scrollViewer = FindVisualChild<ScrollViewer>(folderImagesList);
            if (selectedItem == null || scrollViewer == null || scrollViewer.ViewportWidth <= 0)
            {
                if (remainingRetries > 0)
                    Dispatcher.BeginInvoke(new Action(() => CenterCurrentThumbnailWithRetry(remainingRetries - 1)), DispatcherPriority.Loaded);
                return;
            }

            var itemPoint = selectedItem.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
            var targetOffset = scrollViewer.HorizontalOffset + itemPoint.X + (selectedItem.ActualWidth / 2.0) - (scrollViewer.ViewportWidth / 2.0);
            var maxOffset = Math.Max(0, scrollViewer.ExtentWidth - scrollViewer.ViewportWidth);

            targetOffset = Math.Max(0, Math.Min(maxOffset, targetOffset));
            AnimateScrollTo(scrollViewer, targetOffset);
        }

        private void AnimateScrollTo(ScrollViewer scrollViewer, double targetOffset)
        {
            Controls.ScrollViewerBehavior.SetAnimatedHorizontalOffset(scrollViewer, scrollViewer.HorizontalOffset);

            var animation = new DoubleAnimation(scrollViewer.HorizontalOffset, targetOffset, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scrollViewer.BeginAnimation(Controls.ScrollViewerBehavior.AnimatedHorizontalOffsetProperty, animation);
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T correctlyTyped)
                    return correctlyTyped;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }

            return null;
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
            if (_image?.Bitmap == null || factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
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
            if (_image?.Bitmap == null || (_baseImageWidth > 0 && _baseImageHeight > 0))
                return;

            var fittedSize = GetFittedImageSize(_image.Bitmap.Width, _image.Bitmap.Height);
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

        private void UpdateNavigationButtons()
        {
            var hasImage = !string.IsNullOrEmpty(_currentImagePath);

            if (hasImage)
            {
                if (navigation.Visibility != Visibility.Visible)
                {
                    navigation.Visibility = Visibility.Visible;
                    navigation.Opacity = 0;
                    navigation.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(300))));
                }
                var isPanelVisible = _isFolderPanelVisible;
                UpdateNavigationPosition(isPanelVisible);
            }
            else
            {
                if (navigation.Visibility == Visibility.Visible)
                {
                    var fadeOut = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(200)));
                    fadeOut.Completed += (s, e) => navigation.Visibility = Visibility.Collapsed;
                    navigation.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
                StopSlideshow();
            }
        }

        private void UpdateNavigationPosition(bool folderPanelVisible)
        {
            if (navigation.Visibility != Visibility.Visible)
                return;

            var targetMargin = folderPanelVisible
                ? new Thickness(30, 0, 30, 74)
                : new Thickness(30, 0, 30, 45);

            navigation.BeginAnimation(FrameworkElement.MarginProperty, null);
            navigation.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(targetMargin, new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void PreviousImageButtonClick(object sender, RoutedEventArgs e)
        {
            NavigateImages(-1);
        }

        private void NextImageButtonClick(object sender, RoutedEventArgs e)
        {
            NavigateImages(1);
        }

        private void ShowCurrentImageLabel()
        {
            if (string.IsNullOrEmpty(_currentImagePath) || _image?.Bitmap == null)
            {
                currentImageNameLabel.Visibility = Visibility.Collapsed;
                return;
            }

            var fileInfo = new FileInfo(_currentImagePath);
            currentImageNameLabel.Content = $"{fileInfo.Name} ({_image.Bitmap.Width} x {_image.Bitmap.Height})";
            currentImageNameLabel.Visibility = Visibility.Visible;
        }

        private void NavigationButtonMouseEnter(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.ToolTip is string hint))
                return;

            _navigationHintTimer.Stop();
            currentImageNameLabel.Content = hint;
            currentImageNameLabel.Visibility = Visibility.Visible;
        }

        private void NavigationButtonMouseLeave(object sender, MouseEventArgs e)
        {
            _navigationHintTimer.Stop();
            _navigationHintTimer.Start();
        }

        private void PickerWindowChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SetColorPickerActive(_pickerWindow.IsVisible);
        }

        private void SlideshowButtonClick(object sender, RoutedEventArgs e)
        {
            if (_folderImagePaths.Count <= 1)
                return;

            if (_isSlideshowActive)
                StopSlideshow();
            else
                StartSlideshow();
        }

        private void StartSlideshow()
        {
            _isSlideshowActive = true;
            slideshowIconPath.Data = (Geometry)Application.Current.FindResource("IconPause");
            slideshowBtn.ToolTip = GetResourceString("PauseSlideshow");

            AnimateSlideshowBackdrop(show: true);
            HideFolderPanelAnimated();

            _slideshowTimer.Start();
        }

        private void StopSlideshow()
        {
            if (!_isSlideshowActive)
                return;

            slideshowIconPath.Data = (Geometry)Application.Current.FindResource("IconPlay");
            _isSlideshowActive = false;
            _slideshowTimer.Stop();
            slideshowBtn.ToolTip = GetResourceString("PlaySlideshow");

            AnimateSlideshowBackdrop(show: false);
            ShowFolderPanelAnimated();
        }

        private void AnimateSlideshowBackdrop(bool show)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(300));
            slideshowBackdrop.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(show ? 1 : 0, duration));
        }

        private void FolderPanelMouseEnter(object sender, MouseEventArgs e)
        {
            if (_isSlideshowActive)
                ShowFolderPanelAnimated();
        }

        private void FolderPanelMouseLeave(object sender, MouseEventArgs e)
        {
            if (_isSlideshowActive)
                HideFolderPanelAnimated();
        }

        private void FolderPanelPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollFolderListHorizontally(e.Delta);
            e.Handled = true;
            return;
        }

        private void ScrollFolderListHorizontally(int delta)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(folderImagesList);
            if (scrollViewer == null)
                return;

            var maxOffset = Math.Max(0, scrollViewer.ExtentWidth - scrollViewer.ViewportWidth);
            var targetOffset = scrollViewer.HorizontalOffset - delta;
            targetOffset = Math.Max(0, Math.Min(maxOffset, targetOffset));

            AnimateScrollTo(scrollViewer, targetOffset);
        }

        private void MainGridMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_image?.Bitmap == null || image.Source == null)
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

        private void SampleColorFromPoint(Point relativePoint)
        {
            if (_image?.Bitmap == null)
                return;

            var boxWidth = _baseImageWidth;
            var boxHeight = _baseImageHeight;
            if (boxWidth <= 0 || boxHeight <= 0)
                return;

            var clampedX = Math.Max(0, Math.Min(boxWidth, relativePoint.X));
            var clampedY = Math.Max(0, Math.Min(boxHeight, relativePoint.Y));
            var relativeX = clampedX / boxWidth;
            var relativeY = clampedY / boxHeight;
            var sourceX = (int)Math.Round(relativeX * Math.Max(0, _image.Bitmap.Width - 1));
            var sourceY = (int)Math.Round(relativeY * Math.Max(0, _image.Bitmap.Height - 1));
            var sampledPixel = _image.Bitmap.GetPixel(Math.Max(0, Math.Min(_image.Bitmap.Width - 1, sourceX)), Math.Max(0, Math.Min(_image.Bitmap.Height - 1, sourceY)));
            _pickerWindow.SetColor(Color.FromArgb(sampledPixel.A, sampledPixel.R, sampledPixel.G, sampledPixel.B));
        }

        private void ToggleActualSizeZoom()
        {
            if (_image?.Bitmap == null)
                return;

            EnsureBaseImageSize();
            StopAllImageAnimations();

            var currentScale = GetCurrentZoomScale();
            var actualSizeScale = _baseImageWidth > 0 ? _image.Bitmap.Width / _baseImageWidth : 1.0;
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

        private void RefreshCurrentThumbnail()
        {
            var currentIndex = _folderImagePaths.FindIndex(path =>
                string.Equals(path, _currentImagePath, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0 || currentIndex >= folderImagesList.Items.Count)
                return;

            var thumbnailSource = CreateThumbnailBitmapSource(_currentImagePath) ?? _image.GetBitmapSource();
            if (thumbnailSource == null)
                return;

            _thumbnailCache[_currentImagePath] = thumbnailSource;
            if (folderImagesList.Items[currentIndex] is ListBoxItem item)
                item.Content = CreateThumbnailContent(thumbnailSource);
        }

        private void TryPersistCurrentImage()
        {
            try
            {
                _image.Save();
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Something went wrong while saving.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DeleteCurrentImage()
        {
            if (_image == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            var deletedPath = _currentImagePath;
            var deletedIndex = _folderImagePaths.FindIndex(path =>
                string.Equals(path, deletedPath, StringComparison.OrdinalIgnoreCase));

            try
            {
                FileSystem.DeleteFile(
                    deletedPath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.DoNothing);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Something went wrong while deleting.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _thumbnailCache.Remove(deletedPath);

            var remainingPaths = _folderImagePaths
                .Where(path => !string.Equals(path, deletedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (remainingPaths.Count == 0)
            {
                CloseCurrentImage();
                return;
            }

            _folderImagePaths = remainingPaths;
            folderImagesList.Items.RemoveAt(folderImagesList.SelectedIndex);
            var nextIndex = Math.Min(Math.Max(deletedIndex, 0), remainingPaths.Count - 1);
            SelectFolderImage(nextIndex);
        }

        private void CloseCurrentImage()
        {
            _image = null;
            _currentImagePath = null;
            _currentDirectory = null;
            _folderImagePaths.Clear();
            _thumbnailCache.Clear();
            folderImagesList.Items.Clear();

            image.Source = null;
            ShowCurrentImageLabel();
            grid.Visibility = Visibility.Visible;

            StopSlideshow();
            HideFolderPanelAnimated();
            UpdateNavigationButtons();
        }

        private void PrintCurrentImage()
        {
            if (_image?.Bitmap == null || image.Source == null)
                return;

            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            var pageWidth = printDialog.PrintableAreaWidth;
            var pageHeight = printDialog.PrintableAreaHeight;
            var fittedSize = _image.GetFittedSize(_image.Bitmap.Width, _image.Bitmap.Height, pageWidth, pageHeight);
            var x = (pageWidth - fittedSize.Width) / 2.0;
            var y = (pageHeight - fittedSize.Height) / 2.0;

            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                context.DrawImage(image.Source, new Rect(x, y, fittedSize.Width, fittedSize.Height));
            }

            printDialog.PrintVisual(drawingVisual, GetResourceString("Print"));
        }
    }
}