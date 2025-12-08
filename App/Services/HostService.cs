using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Remotier.Models;
using Remotier.Services.Network;

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
        // NOTE: We don't know Client IP yet for UDP until they tell us or we wait for a handshake.
        // Current architecture guide said "Host shows code/IP" and "ConnectWindow user enters IP".
        // Usually, Client Sends to Host UDP first to punch hole or Register, OR Host sends to Client.
        // If Host Sends to Client, Host needs Client IP.
        // TCP Host accepts connection, so we can get Client IP from TCP connection!

        _tcpHost = new TcpHost();
        _tcpHost.OnControlReceived += OnControlPacket;
        _tcpHost.Start(port);

        // Listen for new clients on TCP to set UDP target
        // For simplicity, we might just assume one client for now or handle it in OnControlReceived "Connect" packet

        _inputService = new InputService();
        _isHosting = true;

        _captureTask = Task.Run(CaptureLoop);
    }

    private void OnControlPacket(ControlPacket packet)
    {
        if (packet.Type == PacketType.Connect)
        {
            // Client sent a Hello packet. In a real app we'd need the IP from the TCP client socket.
            // But ControlPacket doesn't carry IP.
            // We need a way to link the TCP client to the UDP target.
            // For now, let's assume the UI/User flows allow us to know, or we modify logic.

            // HACK: We can ask the user to enter Client IP? No, Client connects to Host. 
            // So Host knows Client IP from the socket.
            // But TcpHost manages sockets. 
            // Let's defer "Connecting UDP" until we have a mechanism. 

            // ALTERNATIVE: TcpHost triggers an event "ClientConnected" with IP.
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
