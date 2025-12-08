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
        hostWindow.Closed += (s, args) => this.Show();
        hostWindow.Show();
        this.Hide();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var connectWindow = new ConnectWindow();
        connectWindow.Closed += (s, args) => this.Show();
        connectWindow.Show();
        this.Hide();
    }
}