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
        _isRunning = true;
        Task.Run(ReceiveLoop);
    }

    private async Task ReceiveLoop()
    {
        while (_isRunning)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                TotalBytesReceived += result.Buffer.Length;
                ProcessPacket(result.Buffer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UDP Receive Error: {ex.Message}");
            }
        }
    }

    private unsafe void ProcessPacket(byte[] data)
    {
        if (data.Length < sizeof(FrameHeader)) return;

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
            int offset = header->ChunkIndex * 60000; // Should match Sender MaxPacketSize
            // Or better, calculate based on payload size if variable, but here we assume fixed chunks except last.
            // Actually, we should just copy payload.
            int headerSize = sizeof(FrameHeader);

            // Using PayloadSize to support variable sized chunks if sender changes logic
            int writePos = header->ChunkIndex * 60000;

            if (writePos + header->PayloadSize <= frame.Data.Length)
            {
                Array.Copy(data, headerSize, frame.Data, writePos, header->PayloadSize);
                frame.ReceivedChunks++;
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
