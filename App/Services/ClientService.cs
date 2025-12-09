using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using Remotier.Services.Network;
using Remotier.Services.Utils;

namespace Remotier.Services;

public class ClientService : IDisposable
{
    private UdpStreamReceiver? _receiver;
    private TcpControlClient? _tcpClient;

    public event Action<BitmapImage>? OnFrameReady;
    public event Action? Reconnecting;
    public event Action? Reconnected;
    public event Action? Disconnected;

    private string? _lastIp;
    private int _lastPort;
    private bool _isReconnecting;
    private bool _intentionalDisconnect;

    public async Task ConnectAsync(string ip, int port)
    {
        _lastIp = ip;
        _lastPort = port;
        _intentionalDisconnect = false;

        await InitializeConnection(ip, port);
    }

    private async Task InitializeConnection(string? ip, int port)
    {
        if (string.IsNullOrEmpty(ip)) throw new ArgumentNullException(nameof(ip));
        // Cleanup existing if any (e.g. during reconnect)
        DisposeResources();

        _receiver = new UdpStreamReceiver();
        _receiver.OnFrameReceived += OnFrameReceived;
        _receiver.Start(port);

        _tcpClient = new TcpControlClient();
        _tcpClient.ConnectionLost += OnConnectionLost;

        try
        {
            await _tcpClient.ConnectAsync(ip, port);

            // Send Connect Packet
            var packet = new ControlPacket { Type = PacketType.Connect, Data = port };
            _tcpClient.SendControl(packet);
        }
        catch
        {
            // If initial connect fails, rethrow so UI knows
            DisposeResources();
            throw;
        }
    }

    private void OnConnectionLost()
    {
        if (_intentionalDisconnect) return;

        // Trigger Auto-Reconnect
        _ = ReconnectLoop();
    }

    private async Task ReconnectLoop()
    {
        if (_isReconnecting) return;
        _isReconnecting = true;
        Reconnecting?.Invoke();

        while (!_intentionalDisconnect)
        {
            try
            {
                await InitializeConnection(_lastIp, _lastPort);

                // Re-connected!
                _isReconnecting = false;
                Reconnected?.Invoke();
                return;
            }
            catch
            {
                // Wait and try again
                await Task.Delay(3000);
            }
        }

        _isReconnecting = false;
    }

    private void OnFrameReceived(byte[] data)
    {
        // Decompress
        using var bitmap = CompressionService.Decompress(data);
        if (bitmap != null)
        {
            // Convert System.Drawing.Bitmap to WPF BitmapImage
            var image = ScreenUtils.BitmapToImageSource(bitmap);
            if (image != null)
            {
                image.Freeze(); // Make it cross-thread accessible
                OnFrameReady?.Invoke(image);
            }
        }
    }

    public long TotalBytesReceived => _receiver?.TotalBytesReceived ?? 0;

    public void SendInput(ControlPacket packet)
    {
        _tcpClient?.SendControl(packet);
    }

    private void DisposeResources()
    {
        if (_tcpClient != null)
        {
            _tcpClient.ConnectionLost -= OnConnectionLost;
            _tcpClient.Dispose();
            _tcpClient = null;
        }
        _receiver?.Dispose();
        _receiver = null;
    }

    public void Dispose()
    {
        _intentionalDisconnect = true;
        DisposeResources();
    }
}
