using System.Buffers.Binary;
using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerLib.Commons;

namespace TestBot;

public class Bot
{
    private Random rand = new Random();
    private Host m_client;
    private Peer m_peer;
    private BotState m_state;
    private float m_stateTimer;
    private int id;
    private int m_sessionKey;
    private int m_playerSequence;
    private float m_heartbeatTimer;
    private string m_deviceKey;
    private string m_userName;
    private string m_roomCode;
    private float m_posX;
    private float m_posY;
    private bool m_enteredRoom;
    private bool m_connected;
    private bool m_loadComplete;
    private const int CONNECTION_TIMEOUT_MS = 5000;     // Connection timeout
    private const int AUTH_TIMEOUT_MS = 10000;          // Auth response timeout
    private const int HEARTBEAT_INTERVAL_MS = 5000;
    private List<BlockInfo> m_blocks;
    private int m_direction;
    private ObjectState m_objectState;

    public Bot(int index)
    {
        id = index;

        string botname = $"Bot{index}_{Library.Time}";
        m_deviceKey = m_userName = botname;

        m_blocks = new List<BlockInfo>();
    }

    public bool Connect(Address address, int channelcount)
    {
        m_client = new Host();
        m_client.Create(null, 1, channelcount);

        // 3. 서버에 연결
        m_peer = m_client.Connect(address, channelcount);
        if (m_peer.NativeData == IntPtr.Zero)
        {
            Log.PrintLog("Connect Fail");
            return false;
        }

        SetState(BotState.Connecting);

        return true;
    }

    public void UpdateRunning(float deltaTime)
    {
        if (m_state == BotState.Disconnected) return;

        //while (true)
        {
            ProcessNetwork();

            m_stateTimer += deltaTime;

            UpdateState(deltaTime);

            if (m_state == BotState.Playing)
            {
                m_heartbeatTimer += deltaTime;
                if (m_heartbeatTimer >= HEARTBEAT_INTERVAL_MS / 1000.0f)
                {
                    SendHeartbeat();
                    m_heartbeatTimer = 0.0f;
                }

                // Movement update
                UpdateMovement(deltaTime);

                // Chat update
                //UpdateChat(deltaTime);
            }
        }
    }

    private void UpdateChat(float deltaTime)
    {
    }

    private void UpdateMovement(float deltaTime)
    {
    }

    private void SendHeartbeat()
    {
        throw new NotImplementedException();
    }

    private void UpdateState(float deltaTime)
    {
        float connectTimeoutSec = CONNECTION_TIMEOUT_MS / 1000.0f;
        float authTimeoutSec = AUTH_TIMEOUT_MS / 1000.0f;

        switch (m_state)
        {
            case BotState.Connecting:
                if (m_stateTimer > connectTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Connection timeout");
                    if (false == m_peer.IsSet)
                    {
                        m_peer.Reset();
                    }
                    SetState(BotState.Disconnected);
                }
                break;

            case BotState.Authenticating:
                if (m_stateTimer > authTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Auth timeout");
                    Disconnect();
                }
                break;

            case BotState.WaitingRoom:
                if (m_stateTimer > authTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Room Entry timeout");
                    Disconnect();
                }
                break;

            case BotState.Loading:
                if (m_stateTimer > authTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Load complete timeout");
                    Disconnect();
                }
                break;

            case BotState.Playing: break;

            default: break;
        }
    }

    private void ProcessNetwork()
    {
        if (m_client is null) return;

        Event netEvent;

        while (0 < m_client.Service(0, out netEvent))
        {
            switch (netEvent.Type)
            {
                case EventType.Connect:
                    Log.PrintLog($"[Bot {id}] Cconnected =====");
                    SetState(BotState.Authenticating);

                    SendAuthRequest();

                    break;

                case EventType.Receive:

                    // 없거나
                    if (netEvent.Packet.IsSet == false || netEvent.Packet.Length < 4) return;

                    // 패킷 데이터를 큐에 복사
                    ReceivedPacket packet;
                    packet.peer = netEvent.Peer;
                    packet.data = new byte[netEvent.Packet.Length];
                    netEvent.Packet.CopyTo(packet.data);

                    HandlePacket(packet);
                    break;

                case EventType.Disconnect:
                    SetState(BotState.Disconnected);
                    Log.PrintLog($"[Bot {id}] Disconnected -----");
                    break;

                default: break;
            }
        }
    }

    private void SendAuthRequest()
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(256);
        var deviceKeyStr = builder.CreateString(m_deviceKey);
        var userNameStr = builder.CreateString(m_userName);

        var request = CLAuthRequest.CreateCLAuthRequest(builder, deviceKeyStr, userNameStr, 0, EProtocol.CL_AuthRequest);
        builder.Finish(request);

        PacketWrapper wrapper = PacketWrapper.Create(EProtocol.CL_AuthRequest, builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.None);

        if (!m_peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SendAuthRequest", MsgLevel.Warning);
        }

        m_client.Flush();
    }

    private void HandlePacket(ReceivedPacket packet)
    {
        int protocolId = BinaryPrimitives.ReadInt32LittleEndian(packet.data.AsSpan(0, 4));

        //if (false == Enum.IsDefined(typeof(EProtocol), protocolId.ToString()))
        //{
        //    Log.PrintLog($"[Packet Check] Protocol ID: {protocolId}", MsgLevel.Warning);
        //    return;
        //}

        EProtocol protocol = (EProtocol)Enum.Parse(typeof(EProtocol), protocolId.ToString());
        if (protocol == EProtocol.None) return;

        //queue.Enqueue(packet);

        switch (protocol)
        {
            case EProtocol.LC_AuthResponse:
                OnAuthResponse(packet);
                break;

            case EProtocol.SC_EnterRoom:
                OnEnterRoom(packet);
                break;

            case EProtocol.SC_LoadCompleteResponse:
                OnLoadCompleteResponse(packet);
                break;

            case EProtocol.SC_AddNotification:
                OnAddNotification(packet);
                break;

            case EProtocol.SC_RemoveNotification:
                OnRemoveNotification(packet);
                break;

            case EProtocol.SC_SyncMove:
                OnSyncMove(packet);
                break;

            case EProtocol.SC_ChangeStateNotification:
                OnChangeStateNotification(packet);
                break;
        }
    }

    private void OnChangeStateNotification(ReceivedPacket packet)
    {
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(packet.data, 4, packet.data.Length - 4);
        var notification = SCChangeStateNotification.GetRootAsSCChangeStateNotification(new ByteBuffer(fbData.ToArray()));

        int state = notification.CurrentState;
        m_objectState = (ObjectState)state;

        if (m_objectState == ObjectState.Normal)
        {
            float posX = notification.PositionX;
            float posY = notification.PositionY;

            Log.PrintLog($"[Bot {id}] Respawned at {posX},{posY})");

            m_posX = posX;
            m_posY = posY;

            PickRandomDirection();
        }

        Log.PrintLog($"[Bot {id}] State Changed to : {m_objectState.ToString()} ");
    }

    private void OnSyncMove(ReceivedPacket packet)
    {
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(packet.data, 4, packet.data.Length - 4);
        var syncMove = SCSyncMove.GetRootAsSCSyncMove(new ByteBuffer(fbData.ToArray()));
    }

    private void OnRemoveNotification(ReceivedPacket packet)
    {
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(packet.data, 4, packet.data.Length - 4);
        var removeNofi = SCRemoveNotification.GetRootAsSCRemoveNotification(new ByteBuffer(fbData.ToArray()));

        var removeArr = removeNofi.GetRemoveSequenceListArray();

        for (int i = 0; i < removeNofi.RemoveSequenceListLength; i++)
        {
            var sequence = removeNofi.RemoveSequenceList(i);

            var blockinfo = m_blocks.Find(x => x.sequence == sequence);

            m_blocks.Remove(blockinfo);
        }
    }

    private void OnAddNotification(ReceivedPacket packet)
    {
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(packet.data, 4, packet.data.Length - 4);
        var addnoti = SCAddNotification.GetRootAsSCAddNotification(new ByteBuffer(fbData.ToArray()));
        for (int i = 0; i < addnoti.SyncListLength; i++)
        {
            var obj = addnoti.SyncList(i);
            if (obj.HasValue == false) continue;

            var syncdata = obj.Value;
            int objType = syncdata.ObjectType;
            int sequnce = syncdata.SourceSequence;

            // block
            if (objType == 3)
            {
                var moveinfo = syncdata.MoveInfo;
                if (moveinfo.HasValue)
                {
                    BlockInfo block = new BlockInfo();
                    block.sequence = sequnce;
                    block.posX = moveinfo.Value.PositionX;
                    block.posY = moveinfo.Value.PositionY;

                    m_blocks.Add(block);
                }
            }
        }
    }

    private void OnLoadCompleteResponse(ReceivedPacket packet)
    {
        m_loadComplete = true;

        PickRandomDirection();

        SendMoveNotification();
    }

    private void SendMoveNotification()
    {
        if (false == m_connected ||
            m_peer.IsSet == false ||
            m_sessionKey == 0) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(1024);
        var request = CSMoveNotification.CreateCSMoveNotification(
            builder,
            m_sessionKey,
            m_posX,
            m_posY,
            m_direction,
            1,
            m_direction,
            EProtocol.CS_MoveNotification);

        builder.Finish(request);

        PacketWrapper wrapper =
            PacketWrapper.Create(EProtocol.CS_MoveNotification, builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            PacketFlags.Reliable);

        if (!m_peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SendMoveNotification", MsgLevel.Warning);
        }

        m_client.Flush();
    }

    private void PickRandomDirection()
    {
        m_direction = rand.Next(365);
    }

    private void OnEnterRoom(ReceivedPacket packet)
    {
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(packet.data, 4, packet.data.Length - 4);

        var msg = SCEnterRoom.GetRootAsSCEnterRoom(new ByteBuffer(fbData.ToArray()));

        m_roomCode = string.IsNullOrEmpty(msg.RoomCode) ? string.Empty : msg.RoomCode;
        m_posX = msg.PositionX;
        m_posY = msg.PositionY;
        m_enteredRoom = true;

        SetState(BotState.Loading);
        SendLoadCompleteRequest();
    }

    private void SendLoadCompleteRequest()
    {
        if (false == m_connected || m_peer.IsSet == false) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        var request = CSLoadCompleteRequest.CreateCSLoadCompleteRequest(builder,
            m_sessionKey,
            EProtocol.CS_LoadCompleteRequest);

        builder.Finish(request);

        PacketWrapper wrapper = PacketWrapper.Create(EProtocol.CS_LoadCompleteRequest, builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            PacketFlags.Reliable);

        if (!m_peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SendLoadCompleteRequest", MsgLevel.Warning);
        }

        m_client.Flush();
    }

    private void OnAuthResponse(ReceivedPacket packet)
    {
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(packet.data, 4, packet.data.Length - 4);
        var authResponse = LCAuthResponse.GetRootAsLCAuthResponse(new ByteBuffer(fbData.ToArray()));

        m_sessionKey = authResponse.SessionKey;
        m_playerSequence = authResponse.PlayerSequence;

        SetState(BotState.WaitingRoom);
    }

    public void Disconnect()
    {
        if (m_peer.NativeData != IntPtr.Zero)
        {
            m_peer.Disconnect(0);

            Event netEvent;
            int timeout = 500;

            while (0 < m_client.Service(timeout, out netEvent))
            {
                if (netEvent.Type == EventType.Receive)
                {
                    netEvent.Packet.Dispose();
                }
                else if (netEvent.Type == EventType.Disconnect)
                {
                    break;
                }
            }
            m_peer.Reset();
        }

        if (m_client is not null)
        {
            m_client.Dispose();
            m_client = null;
        }

        SetState(BotState.Disconnected);
    }

    public BotState GetState()
    {
        return m_state;
    }

    private void SetState(BotState newState)
    {
        if (m_state != newState)
        {
            m_state = newState;
            m_stateTimer = 0.0f;
        }
    }
}

public struct BlockInfo
{
    public int sequence;
    public float posX;
    public float posY;
};

public enum ObjectState
{
    Normal = 0,
    Dead = 1
};