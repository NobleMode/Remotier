using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Remotier.Models;
using Remotier.Services.Network;
using System.Net;
using System.Net.Sockets;

namespace Remotier.Services;

public class HostService : IDisposable
{
    private TcpHost _tcpHost = null!;
    private InputService _inputService = null!;
    private CaptureService _captureService = null!;
    private CompressionService _compressionService = null!;
    private UdpStreamSender _streamSender = null!;

    private bool _isHosting;
    private Task _captureTask;

    public void Start(int port, StreamOptions options)
    {
        _captureService = new CaptureService();
        _captureService.Initialize();

        _compressionService = new CompressionService(options.Quality);

        _streamSender = new UdpStreamSender();

        _tcpHost = new TcpHost();
        _tcpHost.OnControlReceived += OnControlPacket;
        _tcpHost.Start(port);

        _inputService = new InputService();
        _isHosting = true;

        _captureTask = Task.Run(CaptureLoop);
    }

    private void OnControlPacket(ControlPacket packet, TcpClient client)
    {
        if (packet.Type == PacketType.Connect)
        {
            if (client.Client.RemoteEndPoint is IPEndPoint remoteIp)
            {
                // Use the port sent in Data, or default to 5000 if 0
                int targetPort = packet.Data > 0 ? packet.Data : 5000;
                Debug.WriteLine($"Client Connected from {remoteIp.Address}:{targetPort}");
                _streamSender.Connect(remoteIp.Address.ToString(), targetPort);
            }
        }
        else if (packet.Type == PacketType.Mouse || packet.Type == PacketType.Keyboard)
        {
            _inputService.HandleInput(packet);
        }
    }

    // Temporary fix: Method to set client IP explicitly if needed, or we rely on TCP Host modification.
    public void SetClientIp(string ip, int port)
    {
        _streamSender.Connect(ip, port);
    }

    private async Task CaptureLoop()
    {
        while (_isHosting)
        {
            var bitmap = _captureService.CaptureFrame();
            if (bitmap != null)
            {
                byte[] data = _compressionService.Compress(bitmap);
                if (data != null)
                {
                    _streamSender.SendFrame(data);
                }
                bitmap.Dispose();
            }

            // Cap framerate roughly?
            await Task.Delay(16); // ~60fps
        }
    }

    public void Stop()
    {
        _isHosting = false;
        _tcpHost?.Stop();
        _captureService?.Dispose();
        _streamSender?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
