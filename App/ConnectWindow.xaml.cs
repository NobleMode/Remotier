using System.Windows;
using Remotier.Models;

namespace Remotier;

public partial class ConnectWindow : Window
{
    public ConnectWindow()
    {
        InitializeComponent();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        string ip = IpInput.Text;
        if (string.IsNullOrWhiteSpace(ip)) return;

        var remoteView = new RemoteViewWindow(new ConnectionInfo { IP = ip, Port = 5000 });
        remoteView.Show();
        Close();
    }
}
