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
    private List<TcpClient> _clients = new();
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
                _clients.Add(client);
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
            byte[] buffer = new byte[Marshal.SizeOf<ControlPacket>()];

            while (client.Connected && _isRunning)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    if (bytesRead == buffer.Length)
                    {
                        ControlPacket packet;
                        unsafe
                        {
                            fixed (byte* pBuffer = buffer)
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
        _clients.Remove(client);
        ClientDisconnected?.Invoke(client);
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();
        foreach (var c in _clients) c.Close();
        _clients.Clear();
    }

    public void Dispose()
    {
        Stop();
    }
}
