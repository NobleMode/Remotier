using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Remotier.Services.Network;

public class TcpHost : IDisposable
{
    private TcpListener _listener;
    private TcpClient? _client;
    private bool _isRunning;

    public event Action<TcpClient> ClientConnected = delegate { };
    public event Action<TcpClient> ClientDisconnected = delegate { };
    public event Action<ControlPacket, TcpClient> OnControlReceived = delegate { };

    public async void Start(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _isRunning = true;

        System.Diagnostics.Debug.WriteLine($"TCP Host started on port {port}");

        while (_isRunning)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();

                if (_client != null && _client.Connected)
                {
                    client.Close();
                    continue;
                }

                _client = client;
                Task.Run(() => HandleClient(client));
            }
            catch (Exception ex)
            {
                if (_isRunning) System.Diagnostics.Debug.WriteLine($"Accept Error: {ex.Message}");
            }
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        ClientConnected?.Invoke(client);
        using (client)
        using (var stream = client.GetStream())
        {
            byte[] headerBuffer = new byte[1]; // Read Type

            while (client.Connected && _isRunning && _client == client)
            {
                try
                {
                    // Read Packet Type
                    int read = await stream.ReadAsync(headerBuffer, 0, 1);
                    if (read == 0) break;

                    PacketType type = (PacketType)headerBuffer[0];

                    if (type == PacketType.Chat)
                    {
                        // Protocol: [Type:1][Length:4][String:N]
                        byte[] lengthBuffer = new byte[4];
                        await stream.ReadExactlyAsync(lengthBuffer, 0, 4);
                        int validLen = BitConverter.ToInt32(lengthBuffer, 0);

                        if (validLen > 0 && validLen < 1024 * 10) // Limit 10KB
                        {
                            byte[] stringBuffer = new byte[validLen];
                            await stream.ReadExactlyAsync(stringBuffer, 0, validLen);
                            string msg = System.Text.Encoding.UTF8.GetString(stringBuffer);

                            // Re-use PacketType.Chat in ControlPacket for signaling event
                            // Or better: Add a specific event for Chat? 
                            // For simplicity, I'll invoke OnControlReceived with a dummy packet but the message is missing.
                            // I need to update OnControlReceived signature or add OnChatReceived.

                            OnChatReceived?.Invoke(msg, client);
                        }
                    }
                    else
                    {
                        // Fixed Size ControlPacket
                        // We already read 1 byte (Type). The struct has Type as first byte.
                        // We need to read SizeOf(ControlPacket) - 1.
                        int structSize = Marshal.SizeOf<ControlPacket>();
                        byte[] packetBuffer = new byte[structSize];
                        packetBuffer[0] = headerBuffer[0]; // Put back type

                        await stream.ReadExactlyAsync(packetBuffer, 1, structSize - 1);

                        ControlPacket packet;
                        unsafe
                        {
                            fixed (byte* pBuffer = packetBuffer)
                            {
                                packet = *(ControlPacket*)pBuffer;
                            }
                        }
                        OnControlReceived?.Invoke(packet, client);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Client Error: {ex.Message}");
                    break;
                }
            }
        }
        if (_client == client) _client = null;
        ClientDisconnected?.Invoke(client);
    }

    public event Action<string, TcpClient> OnChatReceived = delegate { };

    public async Task BroadcastChat(string message)
    {
        if (_client != null && _client.Connected)
        {
            await SendChatToClient(_client, message);
        }
    }

    public async Task SendChatToClient(TcpClient client, string message)
    {
        if (client == null || !client.Connected) return;

        try
        {
            byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(message);
            byte[] lengthBytes = BitConverter.GetBytes(msgBytes.Length);

            // [Type][Length][Body]
            byte[] data = new byte[1 + 4 + msgBytes.Length];
            data[0] = (byte)PacketType.Chat;
            Array.Copy(lengthBytes, 0, data, 1, 4);
            Array.Copy(msgBytes, 0, data, 5, msgBytes.Length);

            await client.GetStream().WriteAsync(data, 0, data.Length);
        }
        catch { }
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();

        if (_client != null)
        {
            _client.Close();
            _client = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
