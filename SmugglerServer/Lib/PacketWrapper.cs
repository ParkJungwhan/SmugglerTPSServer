using Google.FlatBuffers;
using Protocol;

namespace SmugglerServer.Lib;

internal class PacketWrapper
{
    private byte[] m_data;

    /// <summary>
    /// Smuggler: Extend Method
    /// </summary>
    /// <param name="protocol"></param>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static PacketWrapper Create(EProtocol protocol, FlatBufferBuilder builder)
    {
        PacketWrapper wrapper = new PacketWrapper();
        wrapper.m_data = new byte[4 + builder.Offset];

        int protocolId = (int)protocol;
        Buffer.BlockCopy(BitConverter.GetBytes(protocolId), 0, wrapper.m_data, 0, 4);

        Buffer.BlockCopy(builder.DataBuffer.ToSizedArray(), 0, wrapper.m_data, 4, builder.Offset);

        return wrapper;
    }

    public static EProtocol ExtractProtocol(byte[] data, int dataSize)
    {
        return (data == null || dataSize < 4) ?
            EProtocol.None :
            (EProtocol)BitConverter.ToInt32(data, 0);
    }

    public static byte[] ExtractFlatBufferData(byte[] data, int dataSize)
    {
        if (data == null || dataSize <= 4) return null;

        byte[] fbData = new byte[dataSize - 4];
        Buffer.BlockCopy(data, 4, fbData, 0, fbData.Length);
        return fbData;
    }

    internal EProtocol GetProtocol()
    {
        if (m_data == null || m_data.Length < 4)
            return Protocol.EProtocol.None;

        int protocolId = BitConverter.ToInt32(m_data, 0); // little-endian 기준
        return (Protocol.EProtocol)protocolId;
    }

    internal Span<byte> GetDataOffset4() => m_data.AsSpan(4);

    internal byte[] GetFlatBufferData() => m_data.Length <= 4 ? null : GetDataOffset4().ToArray();

    internal int GetFlatBufferSize() => m_data.Length <= 4 ? 0 : m_data.Length - 4;

    internal byte[] GetRawData() => m_data;

    internal int GetRawSize() => m_data.Length;
}