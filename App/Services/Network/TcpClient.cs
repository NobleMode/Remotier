using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Remotier.Services.Network;

public class TcpControlClient : IDisposable // Renamed to avoid name clash with System.Net.Sockets.TcpClient
{
    private TcpClient _client = null!;
    private NetworkStream _stream = null!;

    public void Connect(string ip, int port)
    {
        _client = new TcpClient();
        _client.Connect(ip, port);
        _stream = _client.GetStream();
    }

    public void SendControl(ControlPacket packet)
    {
        if (_client == null || !_client.Connected) return;

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

    public void Dispose()
    {
        _client?.Close();
        _client?.Dispose();
    }
}
