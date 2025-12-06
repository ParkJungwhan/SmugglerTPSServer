using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerServer.Lib;
using TPSServer.Lib;

namespace SmugglerServer;

internal class Room
{
    private const int MAX_PLAYERS = 1000;
    private const int SYNC_PACKET_SPLIT_LIMIT_SIZE = 1400;
    private const long DISCONNECT_TIMEOUT_MS = 10000;
    private const long CLEANUP_TIMEOUT_MS = 30000;
    private const float ATTACK_RANGE = 50.0f;
    private const float HIT_RADIUS = 1.0f;

    private Dictionary<int, PC> m_players = new Dictionary<int, PC>();
    private List<MoveAction> m_moveQueue = new List<MoveAction>();
    private List<int> m_playersToRemove = new List<int>();

    private Host m_host;
    private string m_roomCode;

    public Room(string roomCode)
    {
        m_roomCode = roomCode;
    }

    public void SetHost(Host host)
    {
        m_host = host;
    }

    private void RemovePlayerBySequence(int playerSequence) => m_players.Remove(playerSequence);

    internal bool AddPlayer(Peer peer, int playerSequence, int sessionKey, string userName, int appearanceID)
    {
        return true;
    }

    internal void RemovePlayer(Peer peer)
    {
    }

    internal void BroadcastAddNotification(int playerSequence, string? userName, int appearenceId, float v1, float v2, int v3)
    {
    }

    internal PC GetPlayer(int playerSequence)
    {
        if (m_players.TryGetValue(playerSequence, out PC pc))
        {
            return pc;
        }
        return null;
    }

    internal PC GetPlayerByPeer(Peer peer)
    {
        foreach (var pair in m_players)
        {
            if (pair.Value.GetPeer().NativeData == peer.NativeData)
            {
                return pair.Value;
            }
        }
        return null;
    }

    internal void SendExsitingPlayers(Peer peer)
    {
        if (m_players.Count == 0) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(1024);
        List<Google.FlatBuffers.Offset<MObjectInfo>> syncList = new();
        //Google.FlatBuffers.Offset<MObjectInfo> syncList = new();

        foreach (var pair in m_players)
        {
            PC player = pair.Value;

            if (player.GetPeer().NativeData == peer.NativeData)
            {
                continue;
            }

            if (!player.IsDisconnected())
            {
                var moveinfo =
                    MObjectMoveInfo.CreateMObjectMoveInfo(builder,
                        player.GetSequenceID(),
                        player.GetPositionX(),
                        player.GetPositionY(),
                        player.GetDirection(),
                        0,
                        player.GetMoveFlag(),
                        0
                    );

                var userNameOffset = builder.CreateString(player.GetName());

                var objectInfo = MObjectInfo.CreateMObjectInfo(builder,
                    player.GetSequenceID(),
                    1,      // object type : Player
                    userNameOffset,
                    player.GetAppearanceID(),
                    moveinfo
                );

                syncList.Add(objectInfo);
            }
        }

        if (syncList.Count == 0) return;

        // 패킷만들어서 던지기
        var syncListOffset = builder.CreateVectorOfTables(syncList.ToArray());

        var addNotification =
            SCAddNotification.CreateSCAddNotification(builder,
                SCAddNotification.CreateSyncListVector(builder, syncList.ToArray()),
                EProtocol.SC_AddNotification
        );

        builder.Finish(addNotification.Value);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_AddNotification,
            builder.DataBuffer.ToSizedArray(),
            builder.Offset);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.None);

        if (!peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail Send Pong");
        }
    }

    internal bool IsEmpty()
    {
        return m_players.Count == 0;
    }

    internal void Update(long currentTime)
    {
        // 1. Timeout check (10s disconnect, 30s cleanup)
        CheckPlayerTimeout(currentTime);
        CleanupExpiredPlayers(currentTime);

        // 2. Death/Respawn processing
        foreach (var pair in m_players)
        {
            var player = pair.Value;
            if (!player.IsDeadState())
            {
                continue;
            }

            long elapsed = currentTime - player.GetDeathTime();
        }
    }

    private void CleanupExpiredPlayers(long currentTime)
    {
    }

    private void CheckPlayerTimeout(long currentTime)
    {
    }

    internal bool IsFull() => m_players.Count >= MAX_PLAYERS;

    internal string GetRoomCode() => m_roomCode;
}