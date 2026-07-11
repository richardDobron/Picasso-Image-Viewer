using System.Windows;
using System.Windows.Controls;

namespace PicassoImageViewer.Controls
{
    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty AnimatedHorizontalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "AnimatedHorizontalOffset",
                typeof(double),
                typeof(ScrollViewerBehavior),
                new PropertyMetadata(0.0, OnAnimatedHorizontalOffsetChanged));

        public static void SetAnimatedHorizontalOffset(DependencyObject element, double value) =>
            element.SetValue(AnimatedHorizontalOffsetProperty, value);

        public static double GetAnimatedHorizontalOffset(DependencyObject element) =>
            (double)element.GetValue(AnimatedHorizontalOffsetProperty);

        private static void OnAnimatedHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
                scrollViewer.ScrollToHorizontalOffset((double)e.NewValue);
        }
    }
}
