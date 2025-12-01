using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerServer.Lib;

namespace SmugglerServer;

public class ServerManager : IDisposable
{
    private Host server;
    private Address address;
    private bool m_isConnected = false;

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
                    break;

                case EventType.Receive:
                    {
                        // 패킷 데이터를 큐에 복사

                        //netEvent.Packet.Dispose();    // 큐에 넣을때는 바로 dispose 하지 말라고 한다
                        break;
                    }

                case EventType.Disconnect:
                    HandleDisconnect(netEvent);
                    break;

                default: break;
            }
        }
    }

    private void SetHandler()
    {
        RegisterHandler<CLAuthRequest>(EProtocol.CL_AuthRequest, OnCLAuthRequest);
        RegisterHandler<CLAuthRequest>(EProtocol.CS_LoadCompleteRequest, OnCSLoadCompleteRequest);
        RegisterHandler<CLAuthRequest>(EProtocol.CS_MoveNotification, OnCSMoveNotification);
        RegisterHandler<CLAuthRequest>(EProtocol.CS_Heartbeat, OnCSHeartbeat);
        RegisterHandler<CLAuthRequest>(EProtocol.CS_AttackRequest, OnCSAttackRequest);
    }

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

    private Dictionary<int, Action<Event, IFlatbufferObject>> HandlerDic =
        new Dictionary<int, Action<Event, IFlatbufferObject>>();

    public void RegisterHandler<T>(EProtocol protocol, Action<Event, T> action) where T : struct, IFlatbufferObject
    {
        HandlerDic.Add((int)protocol, (e, f) =>
        {
            byte[] buffer = new byte[1024];
            e.Packet.CopyTo(buffer);

            Google.FlatBuffers.Table tb = new Google.FlatBuffers.Table(0, new ByteBuffer(buffer, buffer.Length));
            action(e, tb.__union<T>(buffer.Length));
        });
    }

    private Dictionary<int, int> m_playerSequenceToSessionKey = new Dictionary<int, int>();
    private Dictionary<int, string> m_playerSequenceToDeviceKey = new Dictionary<int, string>();
    private Dictionary<int, string> m_playerSequenceToUserName = new Dictionary<int, string>();
    private Dictionary<int, string> m_playerSequenceToRoomCode = new Dictionary<int, string>();
    private Dictionary<string, int> m_deviceKeyToPlayerSequence = new Dictionary<string, int>();
    private Dictionary<Event, int> m_peerToPlayerSequence = new Dictionary<Event, int>();
    private int m_nextSessionKey;
    private int m_nextPlayerSequence;

    private void OnCLAuthRequest(Event peer, Protocol.CLAuthRequest msg)
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
    }

    private void SendAuthResponse(Event NetEvent, int playerSequence, int sessionKey)
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

        if (!NetEvent.Peer.Send(CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail SendAuthResponse");
        }

        server.Flush();
    }

    private const int CHANNEL_RELIABLE = 0;

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
    }

    public void Dispose()
    {
        Library.Deinitialize();
    }
}