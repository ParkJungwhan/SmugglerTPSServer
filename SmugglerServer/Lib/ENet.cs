using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ENet
{
    [Flags]
    public enum PacketFlags
    {
        None = 0,
        Reliable = 1 << 0,
        Unsequenced = 1 << 1,
        NoAllocate = 1 << 2,
        UnreliableFragment = 1 << 3,
        Sent = 1 << 8
    }

    public enum EventType
    {
        None = 0,
        Connect = 1,
        Disconnect = 2,
        Receive = 3
    }

    public enum PeerState
    {
        Disconnected = 0,
        Connecting = 1,
        AcknowledgingConnect = 2,
        ConnectionPending = 3,
        ConnectionSucceeded = 4,
        Connected = 5,
        DisconnectLater = 6,
        Disconnecting = 7,
        AcknowledgingDisconnect = 8,
        Zombie = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENetAddress
    {
        public uint host;
        public ushort port;
    }

    // ENetPacket structure layout (from enet.h)
    // size_t referenceCount
    // enet_uint32 flags
    // enet_uint8* data
    // size_t dataLength
    // ENetPacketFreeCallback freeCallback
    // void* userData
    [StructLayout(LayoutKind.Sequential)]
    internal struct ENetPacketNative
    {
        public IntPtr referenceCount;  // size_t
        public uint flags;             // enet_uint32
        public IntPtr data;            // enet_uint8*
        public IntPtr dataLength;      // size_t
        public IntPtr freeCallback;    // function pointer
        public IntPtr userData;        // void*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ENetEvent
    {
        public EventType type;
        public IntPtr peer;
        public byte channelID;
        public uint data;
        public IntPtr packet;
    }

    public struct Address
    {
        private ENetAddress nativeAddress;

        public ushort Port
        {
            get { return nativeAddress.port; }
            set { nativeAddress.port = value; }
        }

        public void SetHost(string hostName)
        {
            if (Native.enet_address_set_host(ref nativeAddress, hostName) < 0)
            {
                throw new InvalidOperationException("Failed to set host");
            }
        }

        public string GetIP()
        {
            StringBuilder ip = new StringBuilder(64);
            if (Native.enet_address_get_host_ip(ref nativeAddress, ip, (IntPtr)ip.Capacity) < 0)
            {
                return string.Empty;
            }
            return ip.ToString();
        }

        internal ENetAddress NativeData
        {
            get { return nativeAddress; }
            set { nativeAddress = value; }
        }
    }

    public struct Packet : IDisposable
    {
        private IntPtr nativePacket;

        internal IntPtr NativeData
        {
            get { return nativePacket; }
            set { nativePacket = value; }
        }

        public bool IsSet
        {
            get { return nativePacket != IntPtr.Zero; }
        }

        public int Length
        {
            get
            {
                if (nativePacket == IntPtr.Zero)
                    return 0;

                // Read dataLength from ENetPacket structure
                // Offset: referenceCount(8) + flags(4) + data(8) = 20 bytes on x64
                // But with alignment, it's actually at offset 24 (3 * sizeof(IntPtr) on x64)
                IntPtr dataLengthPtr = Marshal.ReadIntPtr(nativePacket, IntPtr.Size * 3);
                return (int)dataLengthPtr;
            }
        }

        public IntPtr Data
        {
            get
            {
                if (nativePacket == IntPtr.Zero)
                    return IntPtr.Zero;

                // Read data pointer from ENetPacket structure
                // Offset: referenceCount(8) + flags(4) = 12, but aligned to 16 on x64
                return Marshal.ReadIntPtr(nativePacket, IntPtr.Size * 2);
            }
        }

        public void Create(byte[] data, PacketFlags flags)
        {
            Create(data, data.Length, flags);
        }

        public void Create(byte[] data, int length, PacketFlags flags)
        {
            if (length > data.Length)
                throw new ArgumentOutOfRangeException("length");

            nativePacket = Native.enet_packet_create(data, (IntPtr)length, flags);

            if (nativePacket == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create packet");
        }

        public void CopyTo(byte[] destination)
        {
            if (nativePacket == IntPtr.Zero)
                throw new InvalidOperationException("Packet not created");

            int length = Length;
            IntPtr dataPtr = Data;

            if (destination.Length < length)
                throw new ArgumentOutOfRangeException("Destination array is too small");

            Marshal.Copy(dataPtr, destination, 0, length);
        }

        public void Dispose()
        {
            if (nativePacket != IntPtr.Zero)
            {
                Native.enet_packet_destroy(nativePacket);
                nativePacket = IntPtr.Zero;
            }
        }
    }

    public struct Peer
    {
        private IntPtr nativePeer;

        internal IntPtr NativeData
        {
            get { return nativePeer; }
            set { nativePeer = value; }
        }

        public bool IsSet
        {
            get { return nativePeer != IntPtr.Zero; }
        }

        public uint ID
        {
            get
            {
                if (nativePeer == IntPtr.Zero)
                    return 0;
                return 0; // Simplified
            }
        }

        public PeerState State
        {
            get
            {
                if (nativePeer == IntPtr.Zero)
                    return PeerState.Disconnected;

                // ENetPeer structure layout (x64):
                // ENetListNode dispatchList (16 bytes - 2 pointers)
                // ENetHost* host (8 bytes)
                // enet_uint16 outgoingPeerID (2 bytes)
                // enet_uint16 incomingPeerID (2 bytes)
                // enet_uint32 connectID (4 bytes)
                // enet_uint8 outgoingSessionID (1 byte)
                // enet_uint8 incomingSessionID (1 byte)
                // [2 bytes padding]
                // ENetAddress address (8 bytes: 4 host + 2 port + 2 padding)
                // void* data (8 bytes)
                // ENetPeerState state (4 bytes - enum)

                // Calculate offset to state field
                // dispatchList: 16, host: 8, outgoingPeerID: 2, incomingPeerID: 2, connectID: 4,
                // outgoingSessionID: 1, incomingSessionID: 1, padding: 2, address: 8, data: 8
                // Total before state: 16 + 8 + 2 + 2 + 4 + 1 + 1 + 2 + 8 + 8 = 52 bytes
                // With alignment to 8 bytes: 56 bytes

                int offset = 56; // Offset to state field on x64
                int state = Marshal.ReadInt32(nativePeer, offset);
                return (PeerState)state;
            }
        }

        public string IP
        {
            get
            {
                return ""; // Simplified - would need to read from peer->address
            }
        }

        public ushort Port
        {
            get
            {
                return 0; // Simplified
            }
        }

        public bool Send(byte channelID, ref Packet packet)
        {
            if (nativePeer == IntPtr.Zero)
                return false;

            return Native.enet_peer_send(nativePeer, channelID, packet.NativeData) == 0;
        }

        public void Disconnect(uint data)
        {
            if (nativePeer != IntPtr.Zero)
                Native.enet_peer_disconnect(nativePeer, data);
        }

        public void DisconnectNow(uint data)
        {
            if (nativePeer != IntPtr.Zero)
                Native.enet_peer_disconnect_now(nativePeer, data);
        }

        public void DisconnectLater(uint data)
        {
            if (nativePeer != IntPtr.Zero)
                Native.enet_peer_disconnect_later(nativePeer, data);
        }

        public void Reset()
        {
            if (nativePeer != IntPtr.Zero)
                Native.enet_peer_reset(nativePeer);
        }
    }

    public struct Event
    {
        private ENetEvent nativeEvent;

        internal ENetEvent NativeData
        {
            get { return nativeEvent; }
            set { nativeEvent = value; }
        }

        public EventType Type
        {
            get { return nativeEvent.type; }
        }

        public Peer Peer
        {
            get
            {
                Peer peer = new Peer();
                peer.NativeData = nativeEvent.peer;
                return peer;
            }
        }

        public byte ChannelID
        {
            get { return nativeEvent.channelID; }
        }

        public uint Data
        {
            get { return nativeEvent.data; }
        }

        public Packet Packet
        {
            get
            {
                Packet packet = new Packet();
                packet.NativeData = nativeEvent.packet;
                return packet;
            }
        }
    }

    public class Host : IDisposable
    {
        private IntPtr nativeHost;

        public bool IsSet
        {
            get { return nativeHost != IntPtr.Zero; }
        }

        public uint PeersCount
        {
            get
            {
                return 0; // Simplified
            }
        }

        public void Create(Address? address, int peerLimit, int channelLimit)
        {
            Create(address, peerLimit, channelLimit, 0, 0);
        }

        public void Create(Address? address, int peerLimit, int channelLimit, uint incomingBandwidth, uint outgoingBandwidth)
        {
            if (nativeHost != IntPtr.Zero)
                throw new InvalidOperationException("Host already created");

            if (address.HasValue)
            {
                ENetAddress nativeAddress = address.Value.NativeData;
                nativeHost = Native.enet_host_create(ref nativeAddress, (IntPtr)peerLimit, (IntPtr)channelLimit, incomingBandwidth, outgoingBandwidth);
            }
            else
            {
                nativeHost = Native.enet_host_create(IntPtr.Zero, (IntPtr)peerLimit, (IntPtr)channelLimit, incomingBandwidth, outgoingBandwidth);
            }

            if (nativeHost == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create host");
        }

        public Peer Connect(Address address, int channelLimit)
        {
            return Connect(address, channelLimit, 0);
        }

        public Peer Connect(Address address, int channelLimit, uint data)
        {
            if (nativeHost == IntPtr.Zero)
                throw new InvalidOperationException("Host not created");

            ENetAddress nativeAddress = address.NativeData;
            IntPtr nativePeer = Native.enet_host_connect(nativeHost, ref nativeAddress, (IntPtr)channelLimit, data);

            if (nativePeer == IntPtr.Zero)
                throw new InvalidOperationException("Failed to connect");

            Peer peer = new Peer();
            peer.NativeData = nativePeer;
            return peer;
        }

        public int Service(int timeout, out Event @event)
        {
            if (nativeHost == IntPtr.Zero)
            {
                @event = new Event();
                return -1;
            }

            ENetEvent nativeEvent = new ENetEvent();
            int result = Native.enet_host_service(nativeHost, ref nativeEvent, (uint)timeout);

            @event = new Event();
            @event.NativeData = nativeEvent;

            return result;
        }

        public int CheckEvents(out Event @event)
        {
            if (nativeHost == IntPtr.Zero)
            {
                @event = new Event();
                return -1;
            }

            ENetEvent nativeEvent = new ENetEvent();
            int result = Native.enet_host_check_events(nativeHost, ref nativeEvent);

            @event = new Event();
            @event.NativeData = nativeEvent;

            return result;
        }

        public void Flush()
        {
            if (nativeHost != IntPtr.Zero)
                Native.enet_host_flush(nativeHost);
        }

        public void Dispose()
        {
            if (nativeHost != IntPtr.Zero)
            {
                Native.enet_host_destroy(nativeHost);
                nativeHost = IntPtr.Zero;
            }
        }
    }

    public static class Library
    {
        public const uint version = (1 << 16) | (3 << 8) | 18; // 1.3.18 = 66326

        public static uint Time
        {
            get { return Native.enet_time_get(); }
        }

        public static bool Initialize()
        {
            return Native.enet_initialize() == 0;
        }

        public static void Deinitialize()
        {
            Native.enet_deinitialize();
        }
    }

    [SuppressUnmanagedCodeSecurity]
    internal static class Native
    {
        private const string NATIVE_LIBRARY = "enet";

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int enet_initialize();

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_deinitialize();

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint enet_linked_version();

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint enet_time_get();

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int enet_address_set_host(ref ENetAddress address, string hostName);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int enet_address_get_host_ip(ref ENetAddress address, StringBuilder hostName, IntPtr nameLength);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr enet_packet_create(byte[] data, IntPtr dataLength, PacketFlags flags);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_packet_destroy(IntPtr packet);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr enet_host_create(ref ENetAddress address, IntPtr peerCount, IntPtr channelLimit, uint incomingBandwidth, uint outgoingBandwidth);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr enet_host_create(IntPtr address, IntPtr peerCount, IntPtr channelLimit, uint incomingBandwidth, uint outgoingBandwidth);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_host_destroy(IntPtr host);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr enet_host_connect(IntPtr host, ref ENetAddress address, IntPtr channelCount, uint data);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int enet_host_service(IntPtr host, ref ENetEvent @event, uint timeout);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int enet_host_check_events(IntPtr host, ref ENetEvent @event);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_host_flush(IntPtr host);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int enet_peer_send(IntPtr peer, byte channelID, IntPtr packet);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_peer_disconnect(IntPtr peer, uint data);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_peer_disconnect_now(IntPtr peer, uint data);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_peer_disconnect_later(IntPtr peer, uint data);

        [DllImport(NATIVE_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void enet_peer_reset(IntPtr peer);
    }
}