using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Remotier.Services.Network;

public class UdpStreamSender : IDisposable
{
    private UdpClient _udpClient;
    private IPEndPoint _remoteEndPoint = null!;
    private int _sequenceId;
    private const int MaxPacketSize = 60000; // Safe below 65535

    public UdpStreamSender()
    {
        _udpClient = new UdpClient();
    }

    public void Connect(string ip, int port)
    {
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
        _udpClient.Connect(_remoteEndPoint);
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

            byte[] packet = new byte[sizeof(FrameHeader) + size];

            fixed (byte* pPacket = packet)
            {
                FrameHeader* header = (FrameHeader*)pPacket;
                header->Type = PacketType.Frame;
                header->FrameId = _sequenceId;
                header->PayloadSize = size;
                header->ChunkIndex = i;
                header->ChunkCount = chunkCount;
                header->TotalFrameSize = totalSize;
            }

            Array.Copy(frameData, offset, packet, sizeof(FrameHeader), size);
            _udpClient.Send(packet, packet.Length);
        }
    }

    public void Dispose()
    {
        _udpClient?.Close();
        _udpClient?.Dispose();
    }
}
