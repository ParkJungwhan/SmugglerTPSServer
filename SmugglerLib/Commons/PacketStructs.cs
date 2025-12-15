using ENet;

namespace SmugglerLib.Commons;

public struct ReceivedPacket
{
    public Peer peer;
    public byte[] data;
}

public class PacketData
{
    public byte[] Data;
    public int Size;

    public PacketData(byte[] data, int size)
    {
        Data = data;
        Size = size;
    }
}

public class SendData
{
    public byte[] Data;
    public bool Reliable;

    public SendData(byte[] data, bool reliable)
    {
        Data = data;
        Reliable = reliable;
    }
}