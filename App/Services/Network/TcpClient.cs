using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Remotier.Services.Network;

public class TcpControlClient : IDisposable
{
    private TcpClient _client = null!;
    private NetworkStream _stream = null!;
    private bool _isConnected;

    public event Action ConnectionLost;

    public async Task ConnectAsync(string ip, int port, string accountName, string password)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(ip, port);
        _stream = _client.GetStream();

        // --- Auth Handshake ---
        try
        {
            // 1. Read Challenge
            int challengeSize = Marshal.SizeOf<AuthChallengePacket>();
            byte[] challengeBytes = new byte[challengeSize];
            await _stream.ReadExactlyAsync(challengeBytes, 0, challengeSize);

            AuthChallengePacket challenge;
            GCHandle hChallenge = GCHandle.Alloc(challengeBytes, GCHandleType.Pinned);
            try { challenge = Marshal.PtrToStructure<AuthChallengePacket>(hChallenge.AddrOfPinnedObject()); }
            finally { hChallenge.Free(); }

            if (challenge.Type != PacketType.AuthChallenge) throw new Exception("Invalid Handshake Packet");

            // 2. Compute Hash
            // Hash(Hash(Plain) + Salt)
            string baseHash = ComputeSha256(password);
            string finalHash = ComputeSha256(baseHash + challenge.Salt);

            // 3. Send Response
            var response = new AuthResponsePacket
            {
                Type = PacketType.AuthResponse,
                DeviceId = Environment.MachineName, // Use MachineName as ID for now
                DeviceName = Environment.MachineName,
                AccountName = accountName ?? "",
                PasswordHash = finalHash
            };

            int respSize = Marshal.SizeOf(response);
            byte[] respBytes = new byte[respSize];
            IntPtr pResp = Marshal.AllocHGlobal(respSize);
            Marshal.StructureToPtr(response, pResp, false);
            Marshal.Copy(pResp, respBytes, 0, respSize);
            Marshal.FreeHGlobal(pResp);

            await _stream.WriteAsync(respBytes, 0, respBytes.Length);

            // 4. Read Result
            int resultSize = Marshal.SizeOf<AuthResultPacket>();
            byte[] resultBytes = new byte[resultSize];
            await _stream.ReadExactlyAsync(resultBytes, 0, resultSize);

            AuthResultPacket result;
            GCHandle hResult = GCHandle.Alloc(resultBytes, GCHandleType.Pinned);
            try { result = Marshal.PtrToStructure<AuthResultPacket>(hResult.AddrOfPinnedObject()); }
            finally { hResult.Free(); }

            if (!result.IsAuthenticated) throw new Exception("Authentication Failed");
        }
        catch (Exception ex)
        {
            _client.Close();
            throw new Exception($"Auth Error: {ex.Message}");
        }
        // --- End Handshake ---

        _isConnected = true;

        // Start a background read loop to detect disconnection (server closed socket)
        _ = Task.Run(ReadLoop);
    }

    private static string ComputeSha256(string input)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
    }

    public event Action<string> ChatReceived = delegate { };
    public event Action<string> ClipboardReceived = delegate { };
    public event Action<string, long> FileStartReceived = delegate { };
    public event Action<byte[]> FileChunkReceived = delegate { };
    public event Action FileEndReceived = delegate { };

    private async Task ReadLoop()
    {
        byte[] headerBuffer = new byte[1];
        try
        {
            using (var stream = _client.GetStream())
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
                    else if (type == PacketType.Clipboard)
                    {
                        byte[] lengthBuffer = new byte[4];
                        await _stream.ReadExactlyAsync(lengthBuffer, 0, 4);
                        int validLen = BitConverter.ToInt32(lengthBuffer, 0);
                        if (validLen > 0 && validLen < 1024 * 1024)
                        {
                            byte[] stringBuffer = new byte[validLen];
                            await _stream.ReadExactlyAsync(stringBuffer, 0, validLen);
                            string text = System.Text.Encoding.UTF8.GetString(stringBuffer);
                            ClipboardReceived?.Invoke(text);
                        }
                    }
                    else if (type == PacketType.FileStart)
                    {
                        byte[] nameLenBuffer = new byte[4];
                        await _stream.ReadExactlyAsync(nameLenBuffer, 0, 4);
                        int nameLen = BitConverter.ToInt32(nameLenBuffer, 0);
                        string fileName = "unknown";
                        if (nameLen > 0 && nameLen < 256)
                        {
                            byte[] nBytes = new byte[nameLen];
                            await _stream.ReadExactlyAsync(nBytes, 0, nameLen);
                            fileName = System.Text.Encoding.UTF8.GetString(nBytes);
                        }
                        byte[] szBytes = new byte[8];
                        await _stream.ReadExactlyAsync(szBytes, 0, 8);
                        long size = BitConverter.ToInt64(szBytes, 0);
                        FileStartReceived?.Invoke(fileName, size);
                    }
                    else if (type == PacketType.FileChunk)
                    {
                        byte[] lBytes = new byte[4];
                        await _stream.ReadExactlyAsync(lBytes, 0, 4);
                        int len = BitConverter.ToInt32(lBytes, 0);
                        if (len > 0 && len <= 1024 * 1024)
                        {
                            byte[] chunk = new byte[len];
                            await _stream.ReadExactlyAsync(chunk, 0, len);
                            FileChunkReceived?.Invoke(chunk);
                        }
                    }
                    else if (type == PacketType.FileEnd)
                    {
                        FileEndReceived?.Invoke();
                    }
                    else
                    {
                        // Ignore unknown packets
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

    public async Task SendClipboard(string text)
    {
        if (_client == null || !_client.Connected || !_isConnected) return;
        try
        {
            byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(text);
            byte[] lengthBytes = BitConverter.GetBytes(msgBytes.Length);
            byte[] data = new byte[1 + 4 + msgBytes.Length];
            data[0] = (byte)PacketType.Clipboard;
            Array.Copy(lengthBytes, 0, data, 1, 4);
            Array.Copy(msgBytes, 0, data, 5, msgBytes.Length);
            await _stream.WriteAsync(data, 0, data.Length);
        }
        catch { NotifyConnectionLost(); }
    }

    public async Task SendFileStart(string fileName, long size)
    {
        if (_client == null || !_client.Connected || !_isConnected) return;
        try
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(fileName);
            byte[] nameLenBytes = BitConverter.GetBytes(nameBytes.Length);
            byte[] sizeBytes = BitConverter.GetBytes(size);
            byte[] data = new byte[1 + 4 + nameBytes.Length + 8];
            data[0] = (byte)PacketType.FileStart;
            Array.Copy(nameLenBytes, 0, data, 1, 4);
            Array.Copy(nameBytes, 0, data, 5, nameBytes.Length);
            Array.Copy(sizeBytes, 0, data, 5 + nameBytes.Length, 8);
            await _stream.WriteAsync(data, 0, data.Length);
        }
        catch { NotifyConnectionLost(); }
    }

    public async Task SendFileChunk(byte[] chunk)
    {
        if (_client == null || !_client.Connected || !_isConnected) return;
        try
        {
            byte[] lenBytes = BitConverter.GetBytes(chunk.Length);
            await _stream.WriteAsync(new byte[] { (byte)PacketType.FileChunk }, 0, 1);
            await _stream.WriteAsync(lenBytes, 0, 4);
            await _stream.WriteAsync(chunk, 0, chunk.Length);
        }
        catch { NotifyConnectionLost(); }
    }

    public async Task SendFileEnd()
    {
        if (_client == null || !_client.Connected || !_isConnected) return;
        try
        {
            await _stream.WriteAsync(new byte[] { (byte)PacketType.FileEnd }, 0, 1);
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
