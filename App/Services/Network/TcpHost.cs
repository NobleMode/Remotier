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

    public event Action<string> OnAuthenticationFailed = delegate { };

    public event Func<string, string, string, string, string, (bool, bool, string)>? VerifyClientAuth;

    private async Task HandleClient(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            // --- Handshake Start ---
            try
            {
                // 1. Send Challenge
                string salt = Guid.NewGuid().ToString("N");
                var challenge = new AuthChallengePacket
                {
                    Type = PacketType.AuthChallenge,
                    Salt = salt
                };

                int challengeSize = Marshal.SizeOf(challenge);
                byte[] challengeBytes = new byte[challengeSize];

                IntPtr pChallenge = Marshal.AllocHGlobal(challengeSize);
                Marshal.StructureToPtr(challenge, pChallenge, false);
                Marshal.Copy(pChallenge, challengeBytes, 0, challengeSize);
                Marshal.FreeHGlobal(pChallenge);

                await stream.WriteAsync(challengeBytes, 0, challengeBytes.Length);

                // 2. Receive Response
                int responseSize = Marshal.SizeOf<AuthResponsePacket>();
                byte[] responseBytes = new byte[responseSize];

                // Read exact size
                await stream.ReadExactlyAsync(responseBytes, 0, responseSize);

                AuthResponsePacket response;
                GCHandle handle = GCHandle.Alloc(responseBytes, GCHandleType.Pinned);
                try
                {
                    response = Marshal.PtrToStructure<AuthResponsePacket>(handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }

                if (response.Type != PacketType.AuthResponse)
                {
                    Debug.WriteLine("Handshake Failed: Invalid Response Type");
                    return;
                }

                // 3. Verify
                bool isAuth = false;
                bool isTrusted = false;
                string failReason = "Unknown Error";

                if (VerifyClientAuth != null)
                {
                    // Invoke delegate
                    foreach (Func<string, string, string, string, string, (bool, bool, string)> handler in VerifyClientAuth.GetInvocationList())
                    {
                        (isAuth, isTrusted, failReason) = handler(response.DeviceId, response.DeviceName, response.AccountName, response.PasswordHash, salt);
                        if (isAuth) break;
                    }
                }

                // 4. Send Result
                var result = new AuthResultPacket
                {
                    Type = PacketType.AuthResult,
                    IsAuthenticated = isAuth,
                    IsTrusted = isTrusted
                };

                int resultSize = Marshal.SizeOf(result);
                byte[] resultBytes = new byte[resultSize];
                IntPtr pResult = Marshal.AllocHGlobal(resultSize);
                Marshal.StructureToPtr(result, pResult, false);
                Marshal.Copy(pResult, resultBytes, 0, resultSize);
                Marshal.FreeHGlobal(pResult);

                await stream.WriteAsync(resultBytes, 0, resultBytes.Length);

                if (!isAuth)
                {
                    Debug.WriteLine($"Handshake Failed: {failReason}");
                    OnAuthenticationFailed?.Invoke($"Auth Failed ({client.Client.RemoteEndPoint}): {failReason}");
                    return;
                }

                // Handshake Success
                // Proceed to Main Loop but we need to notify that client is "fully connected" (Authenticated)
                // We can pass isTrusted to ClientConnected?
                // The event signature is Action<TcpClient>.
                // Maybe we can attach IsTrusted to the client tag or handle it in HostService via the Verification step (which we already did).
                // HostService.VerifyAuth ALREADY calls RegisterConnection etc.
                // But HostService needs to know when the socket is actually ready to be added to _currentClient?
                // The Main Loop fires ClientConnected?.Invoke(client).
                // We should only fire this IF auth success.
                // So strictly:
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Handshake Exception: {ex.Message}");
                return;
            }
            // --- Handshake End ---

            ClientConnected?.Invoke(client); // Notify ONLY after success

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
                            OnChatReceived?.Invoke(msg, client);
                        }
                    }
                    else if (type == PacketType.Clipboard)
                    {
                        byte[] lengthBuffer = new byte[4];
                        await stream.ReadExactlyAsync(lengthBuffer, 0, 4);
                        int validLen = BitConverter.ToInt32(lengthBuffer, 0);

                        if (validLen > 0 && validLen < 1024 * 1024) // Limit 1MB for clipboard text
                        {
                            byte[] stringBuffer = new byte[validLen];
                            await stream.ReadExactlyAsync(stringBuffer, 0, validLen);
                            string text = System.Text.Encoding.UTF8.GetString(stringBuffer);
                            OnClipboardReceived?.Invoke(text, client);
                        }
                    }
                    else if (type == PacketType.FileStart)
                    {
                        // [Type][NameLength:4][Name:N][Size:8]
                        byte[] nameLenBuffer = new byte[4];
                        await stream.ReadExactlyAsync(nameLenBuffer, 0, 4);
                        int nameLen = BitConverter.ToInt32(nameLenBuffer, 0);

                        string fileName = "unknown";
                        if (nameLen > 0 && nameLen < 256)
                        {
                            byte[] nameStats = new byte[nameLen];
                            await stream.ReadExactlyAsync(nameStats, 0, nameLen);
                            fileName = System.Text.Encoding.UTF8.GetString(nameStats);
                        }

                        byte[] sizeBuffer = new byte[8];
                        await stream.ReadExactlyAsync(sizeBuffer, 0, 8);
                        long fileSize = BitConverter.ToInt64(sizeBuffer, 0);

                        OnFileStartReceived?.Invoke(fileName, fileSize, client);
                    }
                    else if (type == PacketType.FileChunk)
                    {
                        // [Type][Len:4][Data:N]
                        byte[] lenBuffer = new byte[4];
                        await stream.ReadExactlyAsync(lenBuffer, 0, 4);
                        int len = BitConverter.ToInt32(lenBuffer, 0);

                        if (len > 0 && len <= 1024 * 1024) // Max chunk 1MB sanity check
                        {
                            byte[] chunk = new byte[len];
                            await stream.ReadExactlyAsync(chunk, 0, len);
                            OnFileChunkReceived?.Invoke(chunk, client);
                        }
                    }
                    else if (type == PacketType.FileEnd)
                    {
                        OnFileEndReceived?.Invoke(client);
                    }
                    else
                    {
                        // Fixed Size ControlPacket
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
    public event Action<string, TcpClient> OnClipboardReceived = delegate { };
    public event Action<string, long, TcpClient> OnFileStartReceived = delegate { };
    public event Action<byte[], TcpClient> OnFileChunkReceived = delegate { };
    public event Action<TcpClient> OnFileEndReceived = delegate { };

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

    public async Task SendClipboardToClient(TcpClient client, string text)
    {
        if (client == null || !client.Connected) return;
        try
        {
            byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(text);
            byte[] lengthBytes = BitConverter.GetBytes(msgBytes.Length);

            byte[] data = new byte[1 + 4 + msgBytes.Length];
            data[0] = (byte)PacketType.Clipboard;
            Array.Copy(lengthBytes, 0, data, 1, 4);
            Array.Copy(msgBytes, 0, data, 5, msgBytes.Length);
            await client.GetStream().WriteAsync(data, 0, data.Length);
        }
        catch { }
    }

    public async Task SendFileStartToClient(TcpClient client, string fileName, long fileSize)
    {
        if (client == null || !client.Connected) return;
        try
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(fileName);
            byte[] nameLenBytes = BitConverter.GetBytes(nameBytes.Length);
            byte[] sizeBytes = BitConverter.GetBytes(fileSize);

            // [Type:1][NameLen:4][Name:N][Size:8]
            byte[] data = new byte[1 + 4 + nameBytes.Length + 8];
            data[0] = (byte)PacketType.FileStart;
            Array.Copy(nameLenBytes, 0, data, 1, 4);
            Array.Copy(nameBytes, 0, data, 5, nameBytes.Length);
            Array.Copy(sizeBytes, 0, data, 5 + nameBytes.Length, 8);

            await client.GetStream().WriteAsync(data, 0, data.Length);
        }
        catch { }
    }

    public async Task SendFileChunkToClient(TcpClient client, byte[] chunk)
    {
        if (client == null || !client.Connected) return;
        try
        {
            byte[] lenBytes = BitConverter.GetBytes(chunk.Length);
            // [Type:1][Len:4][Data:N]
            await client.GetStream().WriteAsync(new byte[] { (byte)PacketType.FileChunk }, 0, 1);
            await client.GetStream().WriteAsync(lenBytes, 0, 4);
            await client.GetStream().WriteAsync(chunk, 0, chunk.Length);
        }
        catch { }
    }

    public async Task SendFileEndToClient(TcpClient client)
    {
        if (client == null || !client.Connected) return;
        try
        {
            await client.GetStream().WriteAsync(new byte[] { (byte)PacketType.FileEnd }, 0, 1);
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
