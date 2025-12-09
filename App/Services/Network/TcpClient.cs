using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Remotier.Services.Network;

public class TcpControlClient : IDisposable
{
    private TcpClient _client = null!;
    private NetworkStream _stream = null!;
    private bool _isConnected;

    public event Action ConnectionLost;

    public async Task ConnectAsync(string ip, int port)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(ip, port);
        _stream = _client.GetStream();
        _isConnected = true;

        // Start a background read loop to detect disconnection (server closed socket)
        _ = Task.Run(ReadLoop);
    }

    private async Task ReadLoop()
    {
        byte[] buffer = new byte[1];
        try
        {
            while (_isConnected && _client.Connected)
            {
                // We don't expect data from Host on this channel yet, but reading 0 bytes means closed.
                int read = await _stream.ReadAsync(buffer, 0, 1);
                if (read == 0)
                {
                    // Disconnected
                    NotifyConnectionLost();
                    break;
                }
            }
        }
        catch (Exception)
        {
            NotifyConnectionLost();
        }
    }

    public void SendControl(ControlPacket packet)
    {
        if (_client == null || !_client.Connected || !_isConnected) return;

        try
        {
            byte[] buffer = new byte[Marshal.SizeOf<ControlPacket>()];
            unsafe
            {
                fixed (byte* pBuffer = buffer)
                {
                    *(ControlPacket*)pBuffer = packet;
                }
            }
            _stream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception)
        {
            NotifyConnectionLost();
        }
    }

    private void NotifyConnectionLost()
    {
        if (_isConnected)
        {
            _isConnected = false;
            ConnectionLost?.Invoke();
        }
    }

    public void Dispose()
    {
        _isConnected = false;
        _stream?.Dispose();
        _client?.Close();
        _client?.Dispose();
    }
}
