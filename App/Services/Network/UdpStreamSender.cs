using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Remotier.Services.Network;

public class UdpStreamSender : IDisposable
{
    private Socket _socket;
    private IPEndPoint _remoteEndPoint = null!;
    private int _sequenceId;
    private const int MaxPacketSize = 60000;

    public UdpStreamSender()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Optional: Increase buffer size
        _socket.SendBufferSize = 1024 * 1024; // 1MB
    }

    public void Connect(string ip, int port)
    {
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
        _socket.Connect(_remoteEndPoint);
    }

    public unsafe void SendFrame(byte[] frameData)
    {
        if (_remoteEndPoint == null) return;

        _sequenceId++;
        int totalSize = frameData.Length;
        int chunkCount = (int)Math.Ceiling((double)totalSize / MaxPacketSize);

        for (int i = 0; i < chunkCount; i++)
        {
            int offset = i * MaxPacketSize;
            int size = Math.Min(MaxPacketSize, totalSize - offset);

            byte[] packetBuffer = BufferPool.Rent();
            try
            {
                int headerSize = sizeof(FrameHeader);

                fixed (byte* pPacket = packetBuffer)
                {
                    FrameHeader* header = (FrameHeader*)pPacket;
                    header->Type = PacketType.Frame;
                    header->FrameId = _sequenceId;
                    header->PayloadSize = size;
                    header->ChunkIndex = i;
                    header->ChunkCount = chunkCount;
                    header->TotalFrameSize = totalSize;
                }

                Array.Copy(frameData, offset, packetBuffer, headerSize, size);

                // Send sync is usually fast enough for UDP, avoids async overhead for fire-and-forget
                _socket.Send(packetBuffer, 0, headerSize + size, SocketFlags.None);
            }
            finally
            {
                BufferPool.Return(packetBuffer);
            }
        }
    }

    public void Dispose()
    {
        _socket?.Close();
        _socket?.Dispose();
    }
}
