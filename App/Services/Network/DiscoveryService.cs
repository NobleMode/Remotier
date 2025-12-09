using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Remotier.Services.Network;

public class DiscoveredHost
{
    public string Name { get; set; } = "";
    public string IP { get; set; } = "";
    public int Port { get; set; }
    public DateTime LastSeen { get; set; }

    public override string ToString() => $"{Name} ({IP}:{Port})";
}

public class DiscoveryService : IDisposable
{
    private const int DiscoveryPort = 8889;
    private const string Header = "REMOTIER_HOST";

    private UdpClient? _udpClient;
    private bool _isRunning;
    private bool _isHost;

    public ObservableCollection<DiscoveredHost> DiscoveredHosts { get; } = new();

    // Beacon (Host Side)
    public void StartBeacon(int port)
    {
        Stop();
        _isHost = true;
        _isRunning = true;
        _udpClient = new UdpClient();
        _udpClient.EnableBroadcast = true;

        Task.Run(async () => await BeaconLoop(port));
    }

    private async Task BeaconLoop(int port)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        string machineName = Environment.MachineName;
        string message = $"{Header}|{machineName}|{port}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        while (_isRunning)
        {
            try
            {
                await _udpClient.SendAsync(data, data.Length, endpoint);
            }
            catch { /* Ignore send errors */ }

            await Task.Delay(2000);
        }
    }

    // Listener (Client Side)
    public void StartListening()
    {
        Stop();
        _isHost = false;
        _isRunning = true;

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            Task.Run(ListenLoop);

            // Start cleanup timer
            Task.Run(CleanupLoop);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discovery Listen Error: {ex.Message}");
        }
    }

    private async Task ListenLoop()
    {
        while (_isRunning && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                string message = Encoding.UTF8.GetString(result.Buffer);

                var parts = message.Split('|');
                if (parts.Length == 3 && parts[0] == Header)
                {
                    string name = parts[1];
                    if (int.TryParse(parts[2], out int port))
                    {
                        string ip = result.RemoteEndPoint.Address.ToString();

                        // Update on UI Thread
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var existing = DiscoveredHosts.FirstOrDefault(h => h.IP == ip && h.Port == port);
                            if (existing != null)
                            {
                                existing.LastSeen = DateTime.Now;
                            }
                            else
                            {
                                DiscoveredHosts.Add(new DiscoveredHost
                                {
                                    Name = name,
                                    IP = ip,
                                    Port = port,
                                    LastSeen = DateTime.Now
                                });
                            }
                        });
                    }
                }
            }
            catch { /* Ignore receive errors */ }
        }
    }

    private async Task CleanupLoop()
    {
        while (_isRunning)
        {
            await Task.Delay(5000);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var now = DateTime.Now;
                var expired = DiscoveredHosts.Where(h => (now - h.LastSeen).TotalSeconds > 10).ToList();
                foreach (var host in expired)
                {
                    DiscoveredHosts.Remove(host);
                }
            });
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;

        System.Windows.Application.Current?.Dispatcher.Invoke(() => DiscoveredHosts.Clear());
    }

    public void Dispose()
    {
        Stop();
    }
}
