using System.Buffers.Binary;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.Intrinsics.X86;
using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerLib.Commons;

namespace TestBot;

public class Bot
{
    private const int CONNECTION_TIMEOUT_MS = 5000;     // Connection timeout
    private const int AUTH_TIMEOUT_MS = 10000;          // Auth response timeout
    private const int HEARTBEAT_INTERVAL_MS = 5000;
    private const float MOVE_SPEED = 5.0f;              // Units per second (same as Unity)
    private const float MAP_MIN = -25.0f;
    private const float MAP_MAX = 25.0f;
    private const int MOVE_NOTIFY_INTERVAL_MS = 100;       // MoveNotification interval (while moving)

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

    private List<BlockInfo> m_blocks;
    private int m_direction;
    private ObjectState m_objectState;
    private float M_PI = 3.14159265358979323846f;
    private float m_moveSpeed;

    private float slideX;
    private float slideY;
    private float slideY_x;
    private float m_moveNotifyTimer;
    private float m_lastPosX;
    private float m_lastPosY;
    private float m_stuckTimer;
    private float m_chatTimer;
    private float m_nextChatInterval;

    private readonly string[] chatmessage =
        { "안녕", "밀수","수원","판교","네오","위즈","우우","꺼져","간다","출발","그러나","있는데","안됀다","가즈아","비트코인" };

    public Bot(int index)
    {
        id = index;

        string botname = $"Bot{index}_{Library.Time}";
        m_deviceKey = m_userName = botname;

        m_blocks = new List<BlockInfo>();

        m_connected = false;
        m_stuckTimer = 0.0f;
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
            UpdateChat(deltaTime);
        }
    }

    private void UpdateChat(float deltaTime)
    {
        if (m_objectState == ObjectState.Dead) return;

        m_chatTimer += deltaTime;

        if (m_chatTimer >= m_nextChatInterval)
        {
            string chatMsg = GenerateRandomChatMessage();
            SendChatMessage(chatMsg);

            m_chatTimer = 0.0f;
            m_nextChatInterval = GetRandomChatInterval();
        }
    }

    private void SendChatMessage(string chatMsg)
    {
        if (false == m_connected ||
            m_peer.IsSet == false ||
            m_sessionKey == 0) return;

        if (m_objectState != ObjectState.Normal) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(256);
        var messageStr = builder.CreateString(chatMsg);

        var request = CSChatRequest.CreateCSChatRequest(builder, m_sessionKey, messageStr);
        builder.Finish(request);

        PacketWrapper wrapper =
            PacketWrapper.Create(EProtocol.CS_ChatRequest, builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.Reliable);

        if (!m_peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail Send ChatMessage", MsgLevel.Warning);
        }

        m_client.Flush();

        Log.PrintLog($"[Bot {id}] Send Chat: {chatMsg}");
    }

    private string GenerateRandomChatMessage()
    {
        var chatindex = rand.Next(chatmessage.Length - 1);
        return chatmessage[chatindex];
    }

    private float GetRandomChatInterval()
    {
        // 메시지 하나씩 보내기, 5~10초 랜덤으로 하나씩 보내자
        //return rand.NextSingle() * 10000;
        return (float)rand.Next(5000, 10000);
    }

    private void UpdateMovement(float deltaTime)
    {
        if (m_objectState == ObjectState.Dead) return;

        float radians = (float)m_direction * M_PI / 180.0f;
        float dx = (float)Math.Sin(radians) * m_moveSpeed * deltaTime;  // X movement
        float dy = (float)Math.Cos(radians) * m_moveSpeed * deltaTime;  // Z/Y movement (server uses Y for Z)

        float targetX = m_posX + dx;
        float targetY = m_posY + dy;

        if (true == IsBlockInPath(targetX, targetY, 0.6f))
        {
            float slideX = m_posX + dx;
            float slideY_x = m_posY; // Don't move in Y

            if (false == IsBlockInPath(slideX, slideY_x, 0.6f))
            {
                m_posX = slideX;
                m_posY = slideY_x;
            }
            else
            {
                float slideX_y = m_posX; // Don't move in X
                float slideY = m_posY + dy;

                if (false == IsBlockInPath(slideX_y, slideY, 0.6f))
                {
                    m_posX = slideX_y;
                    m_posY = slideY;
                }
                else
                {
                    float DIAG_DIST = m_moveSpeed * deltaTime * 0.7071f;    // 45 digree

                    float[,] diagonals = {
                        { DIAG_DIST, DIAG_DIST },
                        { -DIAG_DIST, DIAG_DIST },
                        { DIAG_DIST, -DIAG_DIST },
                        {-DIAG_DIST,-DIAG_DIST } };

                    bool foundDiagonal = false;

                    for (int i = 0; i < 4; i++)
                    {
                        float diagX = m_posX + diagonals[i, 0];
                        float diagY = m_posY + diagonals[i, 1];

                        if (!IsBlockInPath(diagX, diagY, 0.6f))
                        {
                            m_posX = diagX;
                            m_posY = diagY;
                            foundDiagonal = true;
                            break;
                        }
                    }

                    // If diagonal also failed, try reverse direction (back out)
                    if (!foundDiagonal)
                    {
                        // Try backing out (opposite direction)
                        float backX = m_posX - dx * 1.5f;
                        float backY = m_posY - dy * 1.5f;

                        if (!IsBlockInPath(backX, backY, 0.6f))
                        {
                            m_posX = backX;
                            m_posY = backY;
                            // Turn around 180 degrees
                            m_direction = (m_direction + 180) % 360;
                        }
                        else
                        {
                            // Completely stuck - pick random direction and stay in place
                            PickRandomDirection();
                        }

                        SendMoveNotification();
                        m_moveNotifyTimer = 0.0f;
                        m_lastPosX = m_posX;
                        m_lastPosY = m_posY;
                        m_stuckTimer = 0.0f;
                        return;
                    }
                }
            }
        }
        else
        {
            m_posX = targetX;
            m_posY = targetY;
        }

        bool hitBoundary = false;
        if (m_posX <= MAP_MIN)
        {
            m_posX = MAP_MIN;
            hitBoundary = true;
        }
        else if (m_posX >= MAP_MAX)
        {
            m_posX = MAP_MAX;
            hitBoundary = true;
        }

        if (m_posY <= MAP_MIN)
        {
            m_posY = MAP_MIN;
            hitBoundary = true;
        }
        else if (m_posY >= MAP_MAX)
        {
            m_posY = MAP_MAX;
            hitBoundary = true;
        }

        if (hitBoundary)
        {
            PickRandomDirection();
            SendMoveNotification();

            m_moveNotifyTimer = 0.0f;
            m_lastPosX = m_posX;
            m_lastPosY = m_posY;
            m_stuckTimer = 0.0f;
            return;
        }

        float deltaX = m_posX - m_lastPosX;
        float deltaY = m_posY - m_lastPosY;
        float distanceMoved = (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        if (distanceMoved < 0.3f)
        {
            m_stuckTimer += deltaTime;
            if (m_stuckTimer >= 500.0f)
            {
                Log.PrintLog($"[Bot {id}] Stuck detected (0.5s)! Picking new direction");

                int randomOffset = (rand.Next() % 91) + 45;
                m_direction = (m_direction + randomOffset) % 360;

                SendMoveNotification();
                m_moveNotifyTimer = 0.0f;
                m_stuckTimer = 0.0f;
                return;
            }
            else if (m_stuckTimer >= 2000.0f)
            {
                Log.PrintLog($"[Bot {id}] Severely stuck (2s)! Teleporting to random position");

                var newX = GetRandPos();
                var newY = GetRandPos();

                int maxAttempts = 10;
                while (IsBlockInPath(newX, newY, 0.6f) && maxAttempts > 0)
                {
                    newX = GetRandPos();
                    newY = GetRandPos();
                    maxAttempts--;
                }

                if (false == IsBlockInPath(newX, newY, 0.6f))
                {
                    m_posX = newX;
                    m_posY = newY;
                }

                PickRandomDirection();
                SendMoveNotification();
                m_moveNotifyTimer = 0.0f;
                m_lastPosX = m_posX;
                m_lastPosY = m_posY;
                m_stuckTimer = 0.0f;
                return;
            }
        }
        else
        {
            m_stuckTimer = 0.0f;
            m_lastPosX = m_posX;
            m_lastPosY = m_posY;
        }

        m_moveNotifyTimer += deltaTime;
        float notifyIntervalSec = MOVE_NOTIFY_INTERVAL_MS / 1000.0f;
        if (m_moveNotifyTimer >= notifyIntervalSec)
        {
            SendMoveNotification();
            m_moveNotifyTimer = 0.0f;
        }
    }

    private float GetRandPos()
    {
        return (float)rand.Next((int)(MAP_MIN + 10.0f), (int)(MAP_MAX - 10.0f));
    }

    private bool IsBlockInPath(float targetX, float targetY, float radius)
    {
        foreach (var block in m_blocks)
        {
            float dx = block.posX - targetX;
            float dy = block.posY - targetY;

            float distSq = dx * dx + dy * dy;

            if (distSq < (radius + 0.5f) * (radius + 0.5f)) return true;
        }

        return false;
    }

    private void SendHeartbeat()
    {
        if (false == m_connected || m_peer.IsSet == false) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(128);

        var heartbeat = CSHeartbeat.CreateCSHeartbeat(
            builder,
            m_sessionKey,
            EProtocol.CS_Heartbeat
        );

        builder.Finish(heartbeat.Value);

        PacketWrapper wrapper = PacketWrapper.Create(EProtocol.CS_Heartbeat, builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.Reliable);

        if (!m_peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SendHeartbeat", MsgLevel.Warning);
        }

        m_client.Flush();
    }

    private void UpdateState(float deltaTime)
    {
        float connectTimeoutSec = CONNECTION_TIMEOUT_MS / 1000.0f;
        //connectTimeoutSec = CONNECTION_TIMEOUT_MS / 500.0f;
        //connectTimeoutSec = 5.0f;
        float authTimeoutSec = AUTH_TIMEOUT_MS / 1000.0f;
        //authTimeoutSec = AUTH_TIMEOUT_MS / 500.0f;
        //authTimeoutSec = 30.0f;

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
                    //SetState(BotState.Disconnected);
                }
                break;

            case BotState.Authenticating:
                if (m_stateTimer > authTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Auth timeout");
                    //Disconnect();
                }
                break;

            case BotState.WaitingRoom:
                if (m_stateTimer > authTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Room Entry timeout");
                    //Disconnect();
                }
                break;

            case BotState.Loading:
                if (m_stateTimer > authTimeoutSec)
                {
                    Log.PrintLog($"[Bot {id}] Load complete timeout");
                    //Disconnect();
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
                    m_connected = true;
                    Log.PrintLog($"[Bot {id}] ===== Connected =====");
                    SetState(BotState.Authenticating);
                    SendAuthRequest();
                    break;

                case EventType.Receive:

                    // 없거나
                    //if (netEvent.Packet.IsSet == false || netEvent.Packet.Length < 4) return;

                    // 패킷 데이터를 큐에 복사
                    ReceivedPacket packet;
                    packet.peer = netEvent.Peer;
                    packet.data = new byte[netEvent.Packet.Length];
                    netEvent.Packet.CopyTo(packet.data);
                    HandlePacket(packet);
                    netEvent.Packet.Dispose();
                    break;

                case EventType.Disconnect:
                    m_connected = false;
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

        EProtocol protocol = (EProtocol)Enum.Parse(typeof(EProtocol), protocolId.ToString());
        if (protocol == EProtocol.None) return;

        switch (protocol)
        {
            case EProtocol.LC_AuthResponse:
                OnAuthResponse(packet);
                Log.PrintLog("OnAuthResponse", MsgLevel.Information);
                break;

            case EProtocol.SC_EnterRoom:
                OnEnterRoom(packet);
                Log.PrintLog("OnEnterRoom", MsgLevel.Information);
                break;

            case EProtocol.SC_LoadCompleteResponse:
                OnLoadCompleteResponse(packet);
                Log.PrintLog("OnLoadCompleteResponse", MsgLevel.Information);
                break;

            case EProtocol.SC_AddNotification:
                OnAddNotification(packet);
                Log.PrintLog("OnAddNotification", MsgLevel.Information);
                break;

            case EProtocol.SC_RemoveNotification:
                OnRemoveNotification(packet);
                Log.PrintLog("OnRemoveNotification", MsgLevel.Information);
                break;

            case EProtocol.SC_SyncMove:
                OnSyncMove(packet);
                Log.PrintLog("OnSyncMove", MsgLevel.Information);
                break;

            case EProtocol.SC_ChangeStateNotification:
                OnChangeStateNotification(packet);
                Log.PrintLog("OnChangeStateNotification", MsgLevel.Information);
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
        //
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

        SetState(BotState.Playing);

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