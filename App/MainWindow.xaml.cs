using System.Windows;

namespace Remotier;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Host_Click(object sender, RoutedEventArgs e)
    {
        var hostWindow = new HostWindow();
        hostWindow.Show();
        // this.Close(); // Keep main window open?
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var connectWindow = new ConnectWindow();
        connectWindow.Show();
        // this.Close();
    }
}