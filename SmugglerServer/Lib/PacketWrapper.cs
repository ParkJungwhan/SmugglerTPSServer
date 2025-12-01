using System.Xml.Linq;
using Protocol;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmugglerServer.Lib;

internal class PacketWrapper
{
    private byte[] m_data;

    public static PacketWrapper Create(EProtocol protocol, byte[] fbData, int fbSize)
    {
        var wrapper = new PacketWrapper();
        wrapper.m_data = new byte[4 + fbSize];

        // protocolId → 4바이트 little-endian 저장
        BitConverter.TryWriteBytes(wrapper.m_data.AsSpan(0, 4), (int)protocol);

        // FlatBuffer payload 복사
        fbData.AsSpan(0, fbSize).CopyTo(wrapper.m_data.AsSpan(4));

        return wrapper;
    }

    public static PacketWrapper Create(EProtocol protocol, ReadOnlySpan<byte> fbData)
    {
        var wrapper = new PacketWrapper();
        wrapper.m_data = new byte[4 + fbData.Length];

        BitConverter.TryWriteBytes(wrapper.m_data.AsSpan(0, 4), (int)protocol);
        fbData.CopyTo(wrapper.m_data.AsSpan(4));

        return wrapper;
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