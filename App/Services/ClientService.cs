using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using Remotier.Services.Network;
using Remotier.Services.Utils;

namespace Remotier.Services;

public class ClientService : IDisposable
{
    private UdpStreamReceiver _receiver;
    private TcpControlClient _tcpClient;

    public event Action<BitmapImage> OnFrameReady;

    public async Task ConnectAsync(string ip, int port)
    {
        _receiver = new UdpStreamReceiver();
        _receiver.OnFrameReceived += OnFrameReceived;
        _receiver.Start(port);

        _tcpClient = new TcpControlClient();
        await _tcpClient.ConnectAsync(ip, port);

        // Send Connect Packet
        var packet = new ControlPacket { Type = PacketType.Connect, Data = port };
        _tcpClient.SendControl(packet);
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

    public void SendInput(ControlPacket packet)
    {
        _tcpClient?.SendControl(packet);
    }

    public void Dispose()
    {
        _receiver?.Dispose();
        _tcpClient?.Dispose();
    }
}
