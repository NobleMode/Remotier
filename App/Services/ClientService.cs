using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using Remotier.Services.Network;
using Remotier.Services.Utils;

namespace Remotier.Services;

public class ClientService : IDisposable
{
    private UdpStreamReceiver _receiver;
    private TcpControlClient _tcpClient;

    public event Action<BitmapImage> OnFrameReady;

    public void Connect(string ip, int port)
    {
        _receiver = new UdpStreamReceiver();
        _receiver.OnFrameReceived += OnFrameReceived;
        _receiver.Start(port); // Listen on same port? Or dynamic? 
                               // Host sends to Client Port. Client needs to listen on 'port'. 
                               // But Host needs to know to send to this 'port'.

        _tcpClient = new TcpControlClient();
        _tcpClient.Connect(ip, port);

        // Send Connect Packet
        var packet = new ControlPacket { Type = PacketType.Connect };
        _tcpClient.SendControl(packet);
    }

    private void OnFrameReceived(byte[] data)
    {
        // Decompress
        using var bitmap = CompressionService.Decompress(data);
        if (bitmap != null)
        {
            // Convert System.Drawing.Bitmap to WPF BitmapImage
            // This needs to happen on UI thread or return suitable type
            // We'll return logic to convert in ViewModel/UI to avoid threading hell here if possible
            // OR we do memory stream conversion here

            var image = ScreenUtils.BitmapToImageSource(bitmap);
            image.Freeze(); // Make it cross-thread accessible
            OnFrameReady?.Invoke(image);
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
