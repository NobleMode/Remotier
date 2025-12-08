using System.Runtime.InteropServices;

namespace Remotier.Services.Network;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FrameHeader
{
    public PacketType Type;
    public int FrameId;
    public int PayloadSize;
    public int ChunkIndex;
    public int ChunkCount;
    // Total size of the frame (across all chunks)
    public int TotalFrameSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ControlPacket
{
    public PacketType Type;
    public MouseAction Action;
    public int X;
    public int Y;
    public int Data; // e.g. wheel delta or key code
}
