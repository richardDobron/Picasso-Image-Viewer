using System.Reflection;
using System.Windows;

namespace PicassoImageViewer
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            versionLabel.Text = $"{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}";

            closeBtn.Click += (s, e) => Hide();
            githubLabel.Click += (s, e) => System.Diagnostics.Process.Start("https://github.com/richardDobron/PicassoImageViewer");
        }
    }
}