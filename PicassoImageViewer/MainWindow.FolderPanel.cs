using System;
using System.Collections.Generic;
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

namespace PicassoImageViewer
{
    public partial class MainWindow
    {
        private async void PopulateFolderImages(string path)
        {
            var generation = ++_folderLoadGeneration;
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

            List<string> folderImagePaths;
            try
            {
                // Listing large or network folders can take a while; keep the UI responsive.
                folderImagePaths = await Task.Run(() => Directory.EnumerateFiles(directory)
                    .Where(file => _supportedImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .Select(Path.GetFullPath)
                    .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
                    .ToList());
            }
            catch (Exception)
            {
                folderImagePaths = new List<string> { fullPath };
            }

            if (generation != _folderLoadGeneration)
                return;

            _folderImagePaths = folderImagePaths;
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

        private async void RefreshCurrentThumbnail()
        {
            var imagePath = _currentImagePath;
            var currentImage = _image;
            if (string.IsNullOrEmpty(imagePath) || currentImage == null)
                return;

            var thumbnailSource = await Task.Run(() => CreateThumbnailBitmapSource(imagePath)) ?? currentImage.Source;

            var index = _folderImagePaths.FindIndex(path =>
                string.Equals(path, imagePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= folderImagesList.Items.Count)
                return;

            _thumbnailCache[imagePath] = thumbnailSource;
            if (folderImagesList.Items[index] is ListBoxItem item)
                item.Content = CreateThumbnailContent(thumbnailSource);
        }
    }
}
