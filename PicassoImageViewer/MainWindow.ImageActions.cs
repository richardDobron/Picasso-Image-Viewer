using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;

namespace PicassoImageViewer
{
    public partial class MainWindow
    {
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
            if (_image == null)
                return;

            if (clockwise)
                _image.RotateClockwise();
            else
                _image.RotateCounterClockwise();

            if (_image.CanSaveRotation)
                TryPersistCurrentImage();
            else
                ShowRotationNotSavedNotice();

            RefreshCurrentThumbnail();
            ShowCurrentImageLabel();
            SetCurrentImageSource(_image.Source);
        }

        private void PickerWindowChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SetColorPickerActive(_pickerWindow.IsVisible);
        }

        private void SampleColorFromPoint(Point relativePoint)
        {
            if (_image == null)
                return;

            var boxWidth = _baseImageWidth;
            var boxHeight = _baseImageHeight;
            if (boxWidth <= 0 || boxHeight <= 0)
                return;

            var clampedX = Math.Max(0, Math.Min(boxWidth, relativePoint.X));
            var clampedY = Math.Max(0, Math.Min(boxHeight, relativePoint.Y));
            var relativeX = clampedX / boxWidth;
            var relativeY = clampedY / boxHeight;
            var sourceX = (int)Math.Round(relativeX * Math.Max(0, _image.PixelWidth - 1));
            var sourceY = (int)Math.Round(relativeY * Math.Max(0, _image.PixelHeight - 1));
            _pickerWindow.SetColor(_image.GetPixel(sourceX, sourceY));
        }

        private void ShowRotationNotSavedNotice()
        {
            if (_rotationNotSavedNoticeShown)
                return;

            _rotationNotSavedNoticeShown = true;
            MessageBox.Show(
                GetResourceString("RotationNotSaved"),
                GetResourceString("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void TryPersistCurrentImage()
        {
            try
            {
                _image.Save();
            }
            catch (Exception exception)
            {
                ShowError("UnableToSaveImage", "SaveFailed", exception);
            }
        }

        private void DeleteCurrentImage()
        {
            if (_image == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            var deletedPath = _currentImagePath;
            var confirmation = MessageBox.Show(
                string.Format(GetResourceString("DeleteImageConfirmation"), Path.GetFileName(deletedPath)),
                GetResourceString("DeleteImageTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;

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
            catch (Exception exception)
            {
                ShowError("UnableToDeleteImage", "Error", exception);
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

        private void PrintCurrentImage()
        {
            if (_image == null || image.Source == null)
                return;

            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            var pageWidth = printDialog.PrintableAreaWidth;
            var pageHeight = printDialog.PrintableAreaHeight;
            var fittedSize = _image.GetFittedSize(_image.PixelWidth, _image.PixelHeight, pageWidth, pageHeight);
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
