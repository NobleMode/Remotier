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

    public event Action<string> ChatReceived = delegate { };

    private async Task ReadLoop()
    {
        byte[] headerBuffer = new byte[1];
        try
        {
            using (var stream = _client.GetStream()) // Do not dispose stream as it closes client? No, keep it open.
            {
                while (_isConnected && _client.Connected)
                {
                    int read = await _stream.ReadAsync(headerBuffer, 0, 1);
                    if (read == 0)
                    {
                        NotifyConnectionLost();
                        break;
                    }

                    PacketType type = (PacketType)headerBuffer[0];
                    if (type == PacketType.Chat)
                    {
                        byte[] lengthBuffer = new byte[4];
                        await _stream.ReadExactlyAsync(lengthBuffer, 0, 4);
                        int validLen = BitConverter.ToInt32(lengthBuffer, 0);

                        if (validLen > 0 && validLen < 1024 * 10)
                        {
                            byte[] stringBuffer = new byte[validLen];
                            await _stream.ReadExactlyAsync(stringBuffer, 0, validLen);
                            string msg = System.Text.Encoding.UTF8.GetString(stringBuffer);
                            ChatReceived?.Invoke(msg);
                        }
                    }
                    else
                    {
                        // Client currently doesn't expect other packets from Host.
                        // But if Host sends ControlPacket back? Unlikely in this version.
                        // Just consume it if necessary or ignore?
                        // For now, assume only Chat is sent from Host -> Client.
                    }
                }
            }
        }
        catch (Exception)
        {
            NotifyConnectionLost();
        }
    }

    public async Task SendChat(string message)
    {
        if (_client == null || !_client.Connected || !_isConnected) return;
        try
        {
            byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(message);
            byte[] lengthBytes = BitConverter.GetBytes(msgBytes.Length);

            byte[] data = new byte[1 + 4 + msgBytes.Length];
            data[0] = (byte)PacketType.Chat;
            Array.Copy(lengthBytes, 0, data, 1, 4);
            Array.Copy(msgBytes, 0, data, 5, msgBytes.Length);

            await _stream.WriteAsync(data, 0, data.Length);
        }
        catch { NotifyConnectionLost(); }
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
