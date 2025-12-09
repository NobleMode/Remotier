using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Remotier.Services.Network;

public class UdpStreamReceiver : IDisposable
{
    private UdpClient _udpClient;
    private bool _isRunning;
    private Dictionary<int, ReceivedFrame> _frameBuffer = new();

    public event Action<byte[]> OnFrameReceived;

    private class ReceivedFrame
    {
        public byte[] Data;
        public int ReceivedChunks;
        public int TotalChunks;
        public DateTime Timestamp;
    }

    public long TotalBytesReceived { get; private set; }

    public void Start(int port)
    {
        _udpClient = new UdpClient(port);
        // Optimization: Increase Receive Buffer
        _udpClient.Client.ReceiveBufferSize = 1024 * 1024 * 4; // 4MB
        _isRunning = true;
        Task.Run(ReceiveLoop);
    }

    private async Task ReceiveLoop()
    {
        // Reuse a single buffer for receiving, as process packet copies data immediately
        byte[] buffer = BufferPool.Rent();
        var receiveSegment = new ArraySegment<byte>(buffer);

        try
        {
            while (_isRunning)
            {
                try
                {
                    // UdpClient wrapper doesn't expose allocation-free ReceiveAsync easily without dropping down to Client (Socket)
                    // So we use the underlying Socket from UdpClient
                    int received = await _udpClient.Client.ReceiveAsync(receiveSegment, SocketFlags.None);

                    if (received > 0)
                    {
                        TotalBytesReceived += received;
                        ProcessPacket(buffer, received);
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning) Debug.WriteLine($"UDP Receive Error: {ex.Message}");
                }
            }
        }
        finally
        {
            BufferPool.Return(buffer);
        }
    }

    private unsafe void ProcessPacket(byte[] data, int length)
    {
        if (length < sizeof(FrameHeader)) return;

        fixed (byte* pData = data)
        {
            FrameHeader* header = (FrameHeader*)pData;

            if (header->Type != PacketType.Frame) return;

            if (!_frameBuffer.ContainsKey(header->FrameId))
            {
                _frameBuffer[header->FrameId] = new ReceivedFrame
                {
                    Data = new byte[header->TotalFrameSize],
                    TotalChunks = header->ChunkCount,
                    Timestamp = DateTime.Now
                };
            }

            var frame = _frameBuffer[header->FrameId];
            int headerSize = sizeof(FrameHeader);
            int writePos = header->ChunkIndex * 60000;

            // Validate write bounds
            if (writePos + header->PayloadSize <= frame.Data.Length)
            {
                // We must use header->PayloadSize or (length - headerSize)
                // They should be equal, but use header payload size for safety in logic
                int payloadSize = Math.Min(header->PayloadSize, length - headerSize);

                if (payloadSize > 0)
                {
                    Array.Copy(data, headerSize, frame.Data, writePos, payloadSize);
                    frame.ReceivedChunks++;
                }
            }

            if (frame.ReceivedChunks >= frame.TotalChunks)
            {
                OnFrameReceived?.Invoke(frame.Data);
                _frameBuffer.Remove(header->FrameId);
                CleanupOldFrames(header->FrameId);
            }
        }
    }

    private void CleanupOldFrames(int currentFrameId)
    {
        // Remove frames much older than current
        var keys = _frameBuffer.Keys.Where(k => k < currentFrameId - 5).ToList();
        foreach (var key in keys)
        {
            _frameBuffer.Remove(key);
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        _udpClient?.Close();
        _udpClient?.Dispose();
    }
}
