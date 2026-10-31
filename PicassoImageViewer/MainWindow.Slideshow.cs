using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PicassoImageViewer
{
    public partial class MainWindow
    {
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
    }
}
