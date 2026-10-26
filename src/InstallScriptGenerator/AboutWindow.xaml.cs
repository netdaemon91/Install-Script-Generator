using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;

namespace InstallScriptGenerator;

public partial class AboutWindow : Window
{
    public AboutWindow(bool german, bool dark)
    {
        InitializeComponent();
        if (german)
        {
            Title = "Über Install Script Generator";
            AuthorLabel.Text = "Entwickelt von: NetDaemon";
            IdeaLabel.Text = "Ursprüngliche Idee: NetDaemon";
        }

        if (!dark)
        {
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 59));
        }
    }

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
