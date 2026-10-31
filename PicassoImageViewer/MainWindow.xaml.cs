using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        private readonly string[] _supportedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".wmf" };
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
        private int _folderLoadGeneration;
        private const double ZoomStepFactor = 1.15;
        private const double MinImageDimension = 10;
        private const double MinZoomPercent = 1;
        private const double MaxZoomPercent = 3200;
        private const int ZoomOverlayAnimationDurationMs = 140;
        private bool _isSlideshowActive;
        private bool _rotationNotSavedNoticeShown;

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

        public void OpenImageFromExternalRequest(string path)
        {
            StopSlideshow();
            OpenImage(path);
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
            catch (Exception exception)
            {
                ShowError("UnableToOpenImage", "Error", exception);
                return;
            }

            _currentImagePath = fullPath;
            _image = loadedImage;

            ShowCurrentImageLabel();
            SetCurrentImageSource(_image.Source);
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

        private void OpenButtonClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter =
                    "All supported (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.ico;*.tif;*.tiff;*.wmf)|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.ico;*.tif;*.tiff;*.wmf|" +
                    "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
                    "Portable Network Graphic (*.png)|*.png|" +
                    "Graphics Interchange Format (*.gif)|*.gif|" +
                    "Icon (*.ico)|*.ico|" +
                    "Other (*.tif;*.tiff;*.wmf)|*.tif;*.tiff;*.wmf|" +
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

        private void ShowError(string messageKey, string titleKey, Exception exception)
        {
            MessageBox.Show(
                string.Format(GetResourceString(messageKey), exception.Message),
                GetResourceString(titleKey),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplyImageLayout(bool resetTransforms = true, bool updateZoomText = true)
        {
            if (_image == null)
                return;

            StopAllImageAnimations();
            if (resetTransforms)
            {
                _imageScale.ScaleX = 1.0;
                _imageScale.ScaleY = 1.0;
                _imageTranslate.X = 0;
                _imageTranslate.Y = 0;
            }

            var fittedSize = GetFittedImageSize(_image.PixelWidth, _image.PixelHeight);
            _baseImageWidth = fittedSize.Width;
            _baseImageHeight = fittedSize.Height;
            image.Width = fittedSize.Width;
            image.Height = fittedSize.Height;

            if (updateZoomText)
                UpdateZoomTextForWidth(fittedSize.Width);
        }

        private void SetCurrentImageSource(BitmapSource source, bool resetTransforms = true, bool updateZoomText = true)
        {
            image.BeginAnimation(System.Windows.Controls.Image.SourceProperty, null);
            image.Source = source;

            if (_image?.AnimationFrames != null)
                image.BeginAnimation(System.Windows.Controls.Image.SourceProperty, Core.GifAnimation.CreateAnimation(_image.AnimationFrames));

            ApplyImageLayout(resetTransforms, updateZoomText);
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

        private void ShowCurrentImageLabel()
        {
            if (string.IsNullOrEmpty(_currentImagePath) || _image == null)
            {
                currentImageNameLabel.Visibility = Visibility.Collapsed;
                Title = GetResourceString("AppTitle");
                return;
            }

            var fileInfo = new FileInfo(_currentImagePath);
            Title = $"{fileInfo.Name} - {GetResourceString("AppTitle")}";
            currentImageNameLabel.Content = $"{fileInfo.Name} ({_image.PixelWidth} x {_image.PixelHeight})";
            currentImageNameLabel.Visibility = Visibility.Visible;
        }

        private void CloseCurrentImage()
        {
            _folderLoadGeneration++;
            _image = null;
            _currentImagePath = null;
            _currentDirectory = null;
            _folderImagePaths.Clear();
            _thumbnailCache.Clear();
            folderImagesList.Items.Clear();

            image.BeginAnimation(System.Windows.Controls.Image.SourceProperty, null);
            image.Source = null;
            ShowCurrentImageLabel();
            grid.Visibility = Visibility.Visible;

            StopSlideshow();
            HideFolderPanelAnimated();
            UpdateNavigationButtons();
        }
    }
}