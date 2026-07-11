using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PicassoImageViewer
{
    public partial class ColorPickerWindow : Window
    {
        public ColorPickerWindow()
        {
            InitializeComponent();

            ApplyUiTranslations(new Color());
        }


        public void SetColor(Color color)
        {
            var brush = swatchBorder.Background as SolidColorBrush ?? new SolidColorBrush(Colors.Black);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            swatchBorder.Background = brush;
            ApplyUiTranslations(color);
        }

        private void WindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void ApplyUiTranslations(Color color)
        {
            titleLabel.Text = GetResourceString("ColorPickerTitle");
            instructionsLabel.Text = GetResourceString("ColorPickerInstructions");
            valueLabel.Text = string.Format(CultureInfo.CurrentUICulture, GetResourceString("ColorPickerValueFormat"), color.R, color.G, color.B);
            rgbLabel.Text = string.Format(CultureInfo.CurrentUICulture, GetResourceString("ColorPickerRgbFormat"), color.R, color.G, color.B);
            hexLabel.Text = string.Format(CultureInfo.CurrentUICulture, GetResourceString("ColorPickerHexFormat"), color.R, color.G, color.B);
        }

        private static string GetResourceString(string key)
        {
            return Properties.Resources.ResourceManager.GetString(key, Properties.Resources.Culture) ?? key;
        }
    }
}
