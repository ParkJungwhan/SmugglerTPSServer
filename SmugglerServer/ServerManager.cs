using System;
using System.Diagnostics;
using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerServer.Lib;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmugglerServer;

public class ServerManager : IDisposable
{
    private Host server;
    private Address address;
    private bool m_isConnected = false;

    private Dictionary<int, int> m_playerSequenceToSessionKey = new Dictionary<int, int>();
    private Dictionary<int, string> m_playerSequenceToDeviceKey = new Dictionary<int, string>();
    private Dictionary<int, string> m_playerSequenceToUserName = new Dictionary<int, string>();
    private Dictionary<int, string> m_playerSequenceToRoomCode = new Dictionary<int, string>();
    private Dictionary<string, int> m_deviceKeyToPlayerSequence = new Dictionary<string, int>();
    private Dictionary<Peer, int> m_peerToPlayerSequence = new Dictionary<Peer, int>();
    private Queue<ReceivedPacket> ReceiveQueue = new Queue<ReceivedPacket>();

    private PacketHandler PacketHandler = new PacketHandler();

    //private Dictionary<int, Action<Event, IFlatbufferObject>> HandlerDic = new Dictionary<int, Action<Event, IFlatbufferObject>>();

    private const int CHANNEL_RELIABLE = 0;
    private const int CHANNEL_UNRELIABLE = 1;
    private int m_nextSessionKey;
    private int m_nextPlayerSequence;

    private object m_lock = new object();

    public struct ReceivedPacket
    {
        public Peer peer;
        public byte[] data;
    };

    public bool IsConnected
    {
        get { return m_isConnected; }
    }

    public ServerManager()
    {
        var intilib = Library.Initialize();
        if (false == intilib)
        {
            throw new Exception("Failed to initialize ENet library");
        }

        uint time = Library.Time;
        Log.PrintLog($"ENet Library.Time : {time}");
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

        //roomManager = new();
        //roomManager.SetHost(server);

        SetHandler();

        Debug.Assert(server != null);
        return server.IsSet;
    }

    public void Run(CancellationToken cancelToken)
    {
        const int TARGET_FPS = 30;
        const long FRAME_TIME_MS = 1000 / TARGET_FPS;  // 33ms

        Debug.Assert(server != null);

        while (!cancelToken.IsCancellationRequested)
        {
            var frameStart = SteadyClock.Now();
            long currentTimeMs = frameStart.TimeSinceEpochMs;

            // 1. 소켓에서 패킷 수신 → 큐에 저장
            PollNetworkEvents();

            // 2. 큐의 패킷들을 처리
            ProcessPacketQueue();

            // 3. Room Update → Move 브로드캐스트
            //roomManager.Update(currentTimeMs);

            // 3.5 일정 시간 이상 딜레이 킥
            //int[] expiredPlayers = roomManager.GetAndClearExpiredPlayers();
            //////for (int playerSequence = 0; playerSequence < expiredPlayers)
            //for (int i = 0; i < expiredPlayers.Length; i++)
            //{
            //    var playerSequence = expiredPlayers[i];
            //    // 확인해서 끊기
            //}

            // 4. 프레임 레이트 유지(min :30fps)
            var frameEnd = SteadyClock.Now();
            long elapsed = SteadyTimePoint.DiffMilliseconds(frameEnd, frameStart);

            int sleepTime = (int)Math.Max(0, FRAME_TIME_MS - elapsed);
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }

        server!.Flush();
        server.Dispose();
        Library.Deinitialize();
    }

    public void Stop()
    {
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
                    // 연결 처리라서 따로 처리 안함. 이후 패킷인 인증 등은 receive 쪽에서 처리
                    break;

                case EventType.Receive:
                    {
                        // CS_Ping은 즉시 처리 (RTT 측정 정확도), 로컬에서는 평균 0에서 10이하로 나와야 한다.
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

                        byte[] buffer = new byte[netEvent.Packet.Length];
                        netEvent.Packet.CopyTo(buffer);

                        Buffer.BlockCopy(buffer, 0, packet.data, 0, netEvent.Packet.Length);

                        lock (m_lock)
                        {
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
            CSPing msg = GetPingCheck(fbData.ToArray());
            return PingPong(peer, msg);
        }

        return false;
    }

    private void SetHandler()
    {
        // PING 부분에서 패킷 정상 처리 및 변환까지 처리하는 부분 확인됨
        // PacketHandler.RegisterHandler<CSPing>(PingPong, GetPingCheck, (int)EProtocol.CS_Ping); // 삭제 - 위에서 우선처리

        //PacketHandler.RegisterHandler<CLAuthRequest>(OnCLAuthRequest, GetRootAuth, (int)EProtocol.CL_AuthRequest);
        //PacketHandler.RegisterHandler<CLAuthRequest>(OnCLAuthRequest, (int)EProtocol.CL_AuthRequest);
        //PacketHandler.RegisterHandler<CLAuthRequest>(OnCSLoadCompleteRequest, (int)EProtocol.CS_LoadCompleteRequest);
        //PacketHandler.RegisterHandler<CLAuthRequest>(OnCSMoveNotification, (int)EProtocol.CS_MoveNotification);
        //PacketHandler.RegisterHandler<CLAuthRequest>(OnCSHeartbeat, (int)EProtocol.CS_Heartbeat);
        //PacketHandler.RegisterHandler<CSAttackRequest>(OnCSAttackRequest, this, (int)EProtocol.CS_AttackRequest);
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
                EProtocol.SC_Pong)
            .Value);

        server.Flush();

        var wrapper = PacketWrapper.Create(
            EProtocol.SC_Pong,
            builder.DataBuffer.ToSizedArray(),
            builder.Offset);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.None);

        if (!peer.Send(CHANNEL_UNRELIABLE, ref packet))
        {
            Log.PrintLog("Fail Send Pong");
        }

        server.Flush();

        return true;
    }

    //private CSPing GetPingCheck(ByteBuffer buffer)
    private CSPing GetPingCheck(byte[] buffer)
    {
        ByteBuffer csbuffer = new ByteBuffer(buffer);
        return CSPing.GetRootAsCSPing(csbuffer);
    }

    private CLAuthRequest GetRootAuth(byte[] buffer)
    {
        ByteBuffer csbuffer = new ByteBuffer(buffer);
        return CLAuthRequest.GetRootAsCLAuthRequest(csbuffer);
    }

    private bool OnCLAuthRequest(Peer peer, CLAuthRequest msg)
    {
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
        //m_roomManager.AddWaitingPlayer(peer, playerSequence, sessionKey, deviceKey, userName, appearanceId);

        return true;
    }

    //private bool VerifyCLAuth()
    //{
    //    return;

    //}

    private void OnCSAttackRequest(Event @event, CLAuthRequest request)
    {
        //@event.Peer.Send();
    }

    private void OnCSHeartbeat(Event @event, CLAuthRequest request)
    {
    }

    private void OnCSMoveNotification(Event @event, CLAuthRequest request)
    {
    }

    private void OnCSLoadCompleteRequest(Event @event, CLAuthRequest request)
    {
    }

    private void SendAuthResponse(Peer peer, int playerSequence, int sessionKey)
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        builder.Finish(LCAuthResponse.CreateLCAuthResponse(
            builder,
            sessionKey,
            playerSequence,
            EProtocol.LC_AuthResponse
        ).Value);

        server.Flush();

        var wrapper = PacketWrapper.Create(
            EProtocol.LC_AuthResponse,
            builder.DataBuffer.ToSizedArray(),
            builder.Offset);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.Reliable);

        if (!peer.Send(CHANNEL_RELIABLE, ref packet))
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
        Library.Deinitialize();
    }
}