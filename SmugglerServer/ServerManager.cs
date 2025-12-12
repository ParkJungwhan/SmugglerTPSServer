using System.ComponentModel.Design.Serialization;
using System.Diagnostics;
using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerServer.Lib;
using TPSServer.Lib;

namespace SmugglerServer;

/// <summary>
///
/// </summary>
public class ServerManager : IDisposable
{
    private Host server;
    private Address address;

    private Dictionary<int, int> m_playerSequenceToSessionKey = new Dictionary<int, int>();
    private Dictionary<int, string> m_playerSequenceToDeviceKey = new Dictionary<int, string>();
    private Dictionary<int, string> m_playerSequenceToUserName = new Dictionary<int, string>();
    private Dictionary<int, string> m_playerSequenceToRoomCode = new Dictionary<int, string>();
    private Dictionary<string, int> m_deviceKeyToPlayerSequence = new Dictionary<string, int>();
    private Dictionary<Peer, int> m_peerToPlayerSequence = new Dictionary<Peer, int>();
    private Queue<ReceivedPacket> ReceiveQueue = new Queue<ReceivedPacket>();

    private RoomManager roomManager;

    private PacketHandler PacketHandler = new PacketHandler();

    private int m_nextSessionKey;
    private int m_nextPlayerSequence;

    private object m_lock = new object();   // only lock to input stream packet

    public struct ReceivedPacket
    {
        public Peer peer;
        public byte[] data;
    };

    public ServerManager()
    {
        var intilib = Library.Initialize();
        if (false == intilib)
        {
            throw new Exception("Failed to initialize ENet library");
        }

        m_nextSessionKey = 10000;
        m_nextPlayerSequence = 1000;

        Log.PrintLog($"ENet Library.Time: \t{Library.Time}", MsgLevel.Information);
        Log.PrintLog($"SteadyClock.Time: \t{SteadyClock.Now().Timestamp}", MsgLevel.Information);
    }

    public bool Initialize(string ip, ushort port)
    {
        if (string.IsNullOrEmpty(ip)) return false;

        int maxClients = 32;
        int channels = 2;

        address = new Address();
        address.SetHost(ip);
        address.Port = port;

        server = new Host();
        server.Create(address, maxClients, channels);

        roomManager = new();
        roomManager.SetHost(server);

        SetHandler();

        Log.PrintLog($"Server initialized on port {port} (max clients: {maxClients}) ");

        Debug.Assert(server != null);
        return server.IsSet;
    }

    public void Run(CancellationToken cancelToken)
    {
        const int TARGET_FPS = 30;
        const long FRAME_TIME_MS = 1000 / TARGET_FPS;  // 33ms
        const int POLL_INTERVAL_MS = 1;  // 1ms polling for low latency

        Debug.Assert(server != null);

        TimeUtil.TimeBeginPeriod(POLL_INTERVAL_MS);
        Log.PrintLog("Server is running at 30fps with 1ms polling. Press Ctrl+C to stop.");

        var lastFrameTime = SteadyClock.Now();

        while (!cancelToken.IsCancellationRequested)
        {
            var now = SteadyClock.Now();
            long currentTimeMs = Library.Time;

            // 1. 소켓에서 패킷 수신 → 큐에 저장
            PollNetworkEvents();

            // 패킷을 받아서 처리하는 주기는 FRAME_TIME_MS(33)로 진행
            var elapsedSinceFrame = SteadyTimePoint.DiffMilliseconds(now, lastFrameTime);
            if (elapsedSinceFrame >= FRAME_TIME_MS)
            {
                // 33ms 마다 아래의 게임 로직들을 일괄 처리
                lastFrameTime = now;

                // 큐의 패킷들을 처리
                ProcessPacketQueue();

                // Room Update → Move 브로드캐스트
                roomManager.Update(currentTimeMs);

                // 일정 시간 이상 딜레이 킥
                var expiredPlayers = roomManager.GetAndClearExpiredPlayers();
                if (expiredPlayers.Length > 0)
                {
                    Log.PrintLog($"[ServerManager] removing session key for expired player count : {expiredPlayers.Length}");
                    for (int i = 0; i < expiredPlayers.Length; i++)
                    {
                        var playerSequence = expiredPlayers[i];
                        m_playerSequenceToSessionKey.Remove(playerSequence);
                        if (m_playerSequenceToDeviceKey.TryGetValue(playerSequence, out string playerseq))
                        {
                            m_deviceKeyToPlayerSequence.Remove(playerseq);
                            m_playerSequenceToDeviceKey.Remove(playerSequence);
                        }
                        m_playerSequenceToUserName.Remove(playerSequence);
                        m_playerSequenceToRoomCode.Remove(playerSequence);
                    }
                }

                // 1ms 대기 (CPU 부하 감소)
                Thread.Sleep(1);
            }
        }

        TimeUtil.TimeEndPeriod(POLL_INTERVAL_MS); // 종료시에는 이걸로 진행

        Stop();
    }

    public void Stop()
    {
        ReleaseMemory();

        server!.Flush();
        server.Dispose();
        Library.Deinitialize();
    }

    public void ReleaseMemory()
    {
        m_playerSequenceToSessionKey?.Clear();
        m_playerSequenceToSessionKey?.Clear();
        m_playerSequenceToDeviceKey?.Clear();
        m_playerSequenceToUserName?.Clear();
        m_playerSequenceToRoomCode?.Clear();
        m_deviceKeyToPlayerSequence?.Clear();
        m_peerToPlayerSequence?.Clear();
        ReceiveQueue?.Clear();
    }

    internal void PollNetworkEvents()
    {
        ENet.Event netEvent;

        while (server.Service(15, out netEvent) > 0)
        {
            switch (netEvent.Type)
            {
                case EventType.Connect:
                    HandleConnect(netEvent);
                    // 연결 처리라서 따로 처리 안함. 이후 패킷인증 등은 receive 쪽에서 처리
                    break;

                case EventType.Receive:
                    {
                        // CS_Ping은 즉시 처리 (RTT 측정 정확도)
                        // 이외 모든 패킷은 큐에서 돌린다
                        if (TryHandlePingImmediate(netEvent.Peer, netEvent.Packet))
                        {
                            netEvent.Packet.Dispose();
                            break;
                        }

                        // 패킷 데이터를 큐에 복사
                        ReceivedPacket packet;
                        packet.peer = netEvent.Peer;
                        packet.data = new byte[netEvent.Packet.Length];
                        netEvent.Packet.CopyTo(packet.data);

                        Debug.WriteLine($"[Recv Packet] Length : {packet.data.Length}, Protocol :{(EProtocol)((int)packet.data[0])} ");

                        lock (m_lock)
                        {
                            // 큐에 넣고 내부 핸들러에서 처리함
                            ReceiveQueue.Enqueue(packet);
                        }

                        netEvent.Packet.Dispose();    // 큐에 넣을때는 바로 dispose 하지 말라고 한다. 그러나 packet을 buffer에 넣었으니...
                        break;
                    }

                case EventType.Disconnect:
                    HandleDisconnect(netEvent);
                    break;

                default: break;
            }
        }
    }

    private bool TryHandlePingImmediate(Peer peer, Packet packet)
    {
        if (packet.Length < 8) return false;

        byte[] buffer = new byte[packet.Length];
        packet.CopyTo(buffer);

        int messageID = PacketHandler.ExtractMessageId(buffer, buffer.Length);
        if (messageID == (int)EProtocol.CS_Ping)
        {
            ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(buffer, 4, packet.Length - 4);
            CSPing msg = CSPing.GetRootAsCSPing(new ByteBuffer(fbData.ToArray()));
            return PingPong(peer, msg);
        }

        return false;
    }

    private void SetHandler()
    {
        // PING 부분에서 패킷 정상 처리 및 변환까지 처리하는 부분 확인됨
        // PacketHandler.RegisterHandler<CSPing>(PingPong, GetPingCheck, (int)EProtocol.CS_Ping); // 삭제 - 위에서 우선처리

        // 1 인증. (지금은 단순처리)
        PacketHandler.RegisterHandler<CLAuthRequest>(
            OnCLAuthRequest,
            CLAuthRequest.GetRootAsCLAuthRequest,
            EProtocol.CL_AuthRequest);

        // 2 완료 (로딩 다 됐다고 클라가 알리면 그때부터 동기화 진행)
        PacketHandler.RegisterHandler<CSLoadCompleteRequest>(
            OnCSLoadCompleteRequest,
            CSLoadCompleteRequest.GetRootAsCSLoadCompleteRequest,
            EProtocol.CS_LoadCompleteRequest);

        // 3 move noti
        PacketHandler.RegisterHandler<CSMoveNotification>(
            OnCSMoveNotification,
            CSMoveNotification.GetRootAsCSMoveNotification,
            EProtocol.CS_MoveNotification);

        // 4 heartbeat(주기적)
        PacketHandler.RegisterHandler<CSHeartbeat>(
            OnCSHeartbeat,
            CSHeartbeat.GetRootAsCSHeartbeat,
            EProtocol.CS_Heartbeat);

        // 5 attack
        PacketHandler.RegisterHandler<CSAttackRequest>(
            OnCSAttackRequest,
            CSAttackRequest.GetRootAsCSAttackRequest,
            EProtocol.CS_AttackRequest);

        // 6 chat
        PacketHandler.RegisterHandler<CSChatRequest>(
            OnCSChatRequest,
            CSChatRequest.GetRootAsCSChatRequest,
            EProtocol.CS_ChatRequest);
    }

    private bool OnCSChatRequest(Peer peer, CSChatRequest request)
    {
        // 채팅 받으면 전체로 다 쏘기
        //Log.PrintLog($"[Chat] Player:{request.SessionKey}, Msg: {request.Message}");

        if (false == m_peerToPlayerSequence.TryGetValue(peer, out int playerSequence))
        {
            Log.PrintLog("[Chat] Player not found for peer", MsgLevel.Warning);
            return false;
        }

        if (false == ValidateSessionKey(playerSequence, request.SessionKey))
        {
            Log.PrintLog("[Chat] Player not found for peer", MsgLevel.Warning);
            return false;
        }

        Room room = roomManager.GetPlayerRoom(peer);
        if (room is null)
        {
            Log.PrintLog("[Chat] Not found room");
            return false;
        }

        string playerName = "Unknown";
        if (false == m_playerSequenceToUserName.TryGetValue(playerSequence, out playerName))
        {
            Log.PrintLog("[Chat] Not found room");
        }

        PC player = room.GetPlayer(playerSequence);
        if (player is null)
        {
            Log.PrintLog($"[Chat] Player not found in room");
            return false;
        }

        float posX = player.GetPositionX();
        float posY = player.GetPositionY();

        string msg = string.IsNullOrEmpty(request.Message) ? string.Empty : request.Message;

        return room.BroadcastChat(playerSequence, playerName, msg, posX, posY);
    }

    private bool PingPong(Peer peer, CSPing msg)
    {
        long clientTick = msg.ClientTick;
        long serverTick = TimeUtil.GetSystemTimeInMilliseconds();

        FlatBufferBuilder builder = new FlatBufferBuilder(256);

        builder.Finish(
            SCPong.CreateSCPong(
                builder,
                clientTick,
                serverTick,
                EProtocol.SC_Pong));

        var wrapper = PacketWrapper.Create(
            EProtocol.SC_Pong,
            builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.None);

        if (!peer.Send(UDPConn.CHANNEL_UNRELIABLE, ref packet))
        {
            Log.PrintLog("Fail Send Pong", MsgLevel.Warning);
        }

        server.Flush();

        return true;
    }

    private bool OnCLAuthRequest(Peer peer, CLAuthRequest msg)
    {
        Debug.WriteLine($"{DateTime.Now}\t[OnCLAuthRequest] Process Start : {msg.UserName}");

        string deviceKey = string.IsNullOrEmpty(msg.DeviceKey) ? string.Empty : msg.DeviceKey;
        string userName = string.IsNullOrEmpty(msg.UserName) ? string.Empty : msg.UserName;
        int appearanceId = msg.AppearanceId;

        Log.PrintLog($"[RECV] CL_AuthRequest (Player:{userName}, Appearance: {appearanceId}) ");

        int playerSequence = GetOrCreatePlayerSequence(deviceKey);
        int sessionKey = m_nextSessionKey++;

        m_playerSequenceToSessionKey[playerSequence] = sessionKey;
        m_playerSequenceToDeviceKey[playerSequence] = deviceKey;
        m_playerSequenceToUserName[playerSequence] = userName;
        m_peerToPlayerSequence[peer] = playerSequence;

        SendAuthResponse(peer, playerSequence, sessionKey);
        roomManager.AddWaitingPlayer(peer, playerSequence, sessionKey, deviceKey, userName, appearanceId);

        Log.PrintLog($"[SEND] LC_AuthResponse (Seq: {playerSequence}, Session: {sessionKey}) ");

        Debug.WriteLine($"{DateTime.Now}\t[OnCLAuthRequest] Process Complete : {msg.UserName}, SessionKey : {msg.AppearanceId}");
        return true;
    }

    private bool OnCSLoadCompleteRequest(Peer peer, CSLoadCompleteRequest request)
    {
        Debug.WriteLine($"{DateTime.Now}\t[OnCSLoadCompleteRequest] Process Start : {request.SessionKey}");

        if (false == m_peerToPlayerSequence.TryGetValue(peer, out int playerSequence))
        {
            Log.PrintLog("[ServerManager] No find in m_peerToPlayerSequence", MsgLevel.Warning);
            return false;
        }

        if (false == ValidateSessionKey(playerSequence, request.SessionKey))
        {
            Log.PrintLog($"ValidateSessionKey fail : {playerSequence} == {request.SessionKey}", MsgLevel.Warning);
            return false;
        }

        Log.PrintLog($"[RECV] CS_LoadCompleteRequest (Seq:{playerSequence})");

        // 1 패킷 lodacomplete 전달
        // 2 룸 매니저에 플레이어 로드 완료 알림

        // SC_LoadCompleteResponse 패킷 만들기
        FlatBufferBuilder builder = new FlatBufferBuilder(256);

        builder.Finish(
            SCLoadCompleteResponse.CreateSCLoadCompleteResponse(
                builder,
                request.SessionKey,
                EProtocol.SC_LoadCompleteResponse));

        var wrapper = PacketWrapper.Create(
            EProtocol.SC_LoadCompleteResponse,
            builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.Reliable);

        if (!peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SC_LoadCompleteResponse");
        }

        server.Flush();

        Log.PrintLog($"[SEND] SC_LoadCompleteResponse (Seq:{playerSequence}) ");

        // 룸에 세팅
        Room room = roomManager.GetPlayerRoom(peer);
        if (room is not null)
        {
            PC player = room.GetPlayer(playerSequence);
            if (player is not null)
            {
                player.SetLoadCompleted(true);
                Log.PrintLog($"[LoadComplete] Player {playerSequence} load completed");
            }

            if (false == m_playerSequenceToUserName.TryGetValue(playerSequence, out string userName))
            {
                return false;
            }
            Debug.Assert(false == string.IsNullOrEmpty(userName));

            room.SendExsitingPlayers(peer);

            int appearenceId = player is null ? 0 : player.GetAppearanceID();

            room.BroadcastAddNotification(
                playerSequence,
                userName,
                appearenceId,
                0.0f,
                0.0f,
                0);

            Log.PrintLog($"[Send] SC_AddNotification broadcasted (Seq: {playerSequence}) ");
        }

        Debug.Assert(room is not null);
        Debug.WriteLine($"{DateTime.Now}\t[OnCSLoadCompleteRequest] Process Complete : {request.SessionKey}");

        return true;
    }

    private bool ValidateSessionKey(int playerSequence, int sessionKey)
    {
        if (!m_playerSequenceToSessionKey.TryGetValue(playerSequence, out int validSessionKey)) return false;

        return validSessionKey == sessionKey;
    }

    private bool OnCSMoveNotification(Peer peer, CSMoveNotification request)
    {
        if (false == m_peerToPlayerSequence.TryGetValue(peer, out int playerSequence))
        {
            Log.PrintLog("[ERROR] CS_MoveNotification - peer not found");
            return false;
        }

        if (false == ValidateSessionKey(playerSequence, request.SessionKey))
        {
            Log.PrintLog($"[ERROR] CS_MoveNotification - invalid session key for player {playerSequence}");
            return false;
        }

        Room room = roomManager.GetPlayerRoom(peer);
        if (room is null)
        {
            Log.PrintLog($"[ERROR] CS_MoveNotification - room is NULL for player {playerSequence}");
            return false;
        }

        PC player = room.GetPlayer(playerSequence);
        if (player is null)
        {
            Log.PrintLog($"[ERROR] CS_MoveNotification - player {playerSequence} not found in room");
            return false;
        }

        MoveAction action = new MoveAction();
        action.playerSequence = playerSequence;
        action.playerSequence = playerSequence;
        action.positionX = request.PositionX;
        action.positionY = request.PositionY;
        action.direction = request.Direction;
        action.moveFlag = request.MoveFlag;
        action.aimDirection = request.AimDirection;

        room.QueueMoveAction(action);

        long currentTime = Library.Time;

        player.UpdateLastReceived(currentTime);

        return true;
    }

    private bool OnCSAttackRequest(Peer peer, CSAttackRequest request)
    {
        if (false == m_peerToPlayerSequence.TryGetValue(peer, out int playerSequence))
        {
            Log.PrintLog("[OnCSAttackRequest] Not found peer user", MsgLevel.Warning);
            return false;
        }
        if (false == ValidateSessionKey(playerSequence, request.SessionKey))
        {
            Log.PrintLog("[OnCSAttackRequest] No validation CheckUser: {playerSequence}", MsgLevel.Warning);
            return false;
        }

        Log.PrintLog($"[RECV] CS_ATTACKRequest (Seq: {playerSequence}, AttackID : {request.AttackId}, Pos: {request.PositionX},{request.PositionY}, Aim: {request.AimDirection})");

        Room room = roomManager.GetPlayerRoom(peer);
        if (room is not null)
        {
            AttackResult result = room.ProcessAttack(
                playerSequence,
                request.AttackId,
                request.PositionX,
                request.PositionY,
                request.AimDirection);

            room.BroadcastAttackResult(result);

            Log.PrintLog($"[SEND] SC_ATTACK broadcasted (Hit: {result.isHit})");
        }

        return true;
    }

    private bool OnCSHeartbeat(Peer peer, CSHeartbeat request)
    {
        if (false == m_peerToPlayerSequence.TryGetValue(peer, out int playerSequence)) return false;
        if (false == ValidateSessionKey(playerSequence, request.SessionKey)) return false;

        long currentTime = Library.Time;

        Room room = roomManager.GetPlayerRoom(peer);
        if (room is not null)
        {
            PC player = room.GetPlayerByPeer(peer);
            if (player is not null)
            {
                player.UpdateLastReceived(currentTime);
                Debug.WriteLine($"{DateTime.Now}\tOnCSHeartbeat : {request.SessionKey}");
            }
        }

        return true;
    }

    private void SendAuthResponse(Peer peer, int playerSequence, int sessionKey)
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        builder.Finish(
            LCAuthResponse.CreateLCAuthResponse(
                builder,
                sessionKey,
                playerSequence,
                EProtocol.LC_AuthResponse));

        var wrapper = PacketWrapper.Create(
            EProtocol.LC_AuthResponse,
            builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.Reliable);

        if (!peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SendAuthResponse");
        }

        server.Flush();
    }

    private int GetOrCreatePlayerSequence(string deviceKey)
    {
        if (m_deviceKeyToPlayerSequence.TryGetValue(deviceKey, out int value))
        {
            return value;
        }

        int newSequence = m_nextPlayerSequence++;
        m_deviceKeyToPlayerSequence[deviceKey] = newSequence;
        return newSequence;
    }

    private void HandleDisconnect(Event netEvent)
    {
        // 연결 끊었을때 생기는 이벤트 처리
    }

    private void HandleConnect(Event netEvent)
    {
        // 커넥션 맺었을떄 생기는 이벤트 처리
    }

    private void ProcessPacketQueue()
    {
        if (ReceiveQueue.Count == 0) return;

        Queue<ReceivedPacket> localQueue;

        lock (m_lock)
        {
            localQueue = ReceiveQueue;
            ReceiveQueue = new Queue<ReceivedPacket>();
        }

        while (localQueue.Count > 0)
        {
            var packet = localQueue.Dequeue();

            PacketHandler.Dispatch(
                packet.peer,
                packet.data,
                packet.data.Length);
        }
    }

    public void Dispose()
    {
        // 모든 큐들 다 dispose 처리
        Library.Deinitialize();
    }
}