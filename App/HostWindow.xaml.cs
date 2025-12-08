using System.Net;
using System.Net.Sockets;
using System.Windows;
using Remotier.Services;
using Remotier.Models;

namespace Remotier;

public partial class HostWindow : Window
{
    private HostService _hostService;

    public HostWindow()
    {
        InitializeComponent();
        Loaded += HostWindow_Loaded;
        Closing += HostWindow_Closing;
    }

    private void HostWindow_Loaded(object sender, RoutedEventArgs e)
    {
        string localIP = GetLocalIPAddress();
        IpText.Text = $"IP: {localIP} (Port: 5000)";

        _hostService = new HostService();
        _hostService.Start(5000, new StreamOptions());
        StatusText.Text = "Service Started.";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HostWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hostService?.Dispose();
    }

    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }
}
