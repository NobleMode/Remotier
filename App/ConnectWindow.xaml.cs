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
        string input = IpInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        string ip = input;
        int port = 5000;

        int colonIndex = input.LastIndexOf(':');
        if (colonIndex != -1)
        {
            ip = input.Substring(0, colonIndex);
            string portStr = input.Substring(colonIndex + 1);
            if (!int.TryParse(portStr, out port))
            {
                MessageBox.Show("Invalid port number.");
                return;
            }
        }

        var remoteView = new RemoteViewWindow(new ConnectionInfo { IP = ip, Port = port });
        remoteView.Closed += (s, args) => this.Close(); // Close ConnectWindow when RemoteView closes
        remoteView.Show();
        this.Hide(); // Hide ConnectWindow instead of Closing immediately
    }
}
