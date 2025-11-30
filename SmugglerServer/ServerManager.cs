using System.Diagnostics;
using ENet;
using SmugglerServer.Lib;

namespace SmugglerServer;

public class ServerManager : IDisposable
{
    private Host server;
    private Address address;
    private RoomManager roomManager;

    public delegate void UDPEvent(object sender, Event netEvent);

    public event UDPEvent evNetEvent;

    public ServerManager()
    {
        if (!ENet.Library.Initialize())
        {
            Log.PrintLog("Failed to initialize ENet", MsgLevel.Error);
            throw new Exception("Failed to initialize ENet library");
        }
    }

    public bool Initialize(string ip, ushort port, int maxClients = 32, int channels = 2)
    {
        if (string.IsNullOrEmpty(ip)) return false;

        server = new Host();
        address = new Address();
        address.SetIP(ip);
        address.Port = port;

        roomManager = new();

        server.Create(address, maxClients, channels);

        roomManager.SetHost(server);

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
            roomManager.Update(currentTimeMs);

            // 3.5 일정 시간 이상 딜레이 킥
            int[] expiredPlayers = roomManager.GetAndClearExpiredPlayers();
            //for (int playerSequence = 0; playerSequence < expiredPlayers)
            for (int i = 0; i < expiredPlayers.Length; i++)
            {
                var playerSequence = expiredPlayers[i];
                // 확인해서 끊기
            }

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
        ENet.Library.Deinitialize();
    }

    public void Stop()
    {
    }

    internal void PollNetworkEvents()
    {
        Event netEvent;
        var polled = false;

        while (!polled)
        {
            if (server!.CheckEvents(out netEvent) <= 0)
            {
                if (server.Service(15, out netEvent) <= 0) break;
                polled = true;
            }

            switch (netEvent.Type)
            {
                case EventType.Connect:
                    HandleConnect(netEvent);
                    break;

                case EventType.Receive:
                    {
                        // 패킷 데이터를 큐에 복사

                        netEvent.Packet.Dispose();
                        break;
                    }

                case EventType.Disconnect:
                    HandleDisconnect(netEvent);
                    break;

                default: break;
            }
        }
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
        ENet.Library.Deinitialize();
    }
}