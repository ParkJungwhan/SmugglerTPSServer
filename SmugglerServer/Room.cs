using System.Diagnostics;
using System.Numerics;
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

    private Random rand = new Random(65535);

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
        if (IsFull()) return false;

        PC pc = new();
        pc.SetSequenceID(playerSequence);
        pc.SetName(userName);
        pc.SetPeer(peer);
        pc.SetSessionKey(sessionKey);
        pc.SetAppearanceId(appearanceID);
        pc.SetPosition(0.0f, 0.0f);
        pc.SetDirection(0);
        pc.SetMoveFlag(0);
        pc.SetLoadCompleted(false);

        var start = SteadyClock.Now();
        long currentTime = start.TimeSinceEpochMs;

        pc.UpdateLastReceived(currentTime);

        m_players[playerSequence] = pc;

        return true;
    }

    internal void RemovePlayer(Peer peer)
    {
        for (int i = 0; i < m_players.Count; i++)
        {
            if (m_players[i].GetPeer().NativeData == peer.NativeData)
            {
                m_players.Remove(i);
                return;
            }
        }
    }

    internal void BroadcastAddNotification(int sourceSequence, string? userName, int appearenceId, float posX, float posY, int dir)
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(1024);
        var moveInfo = MObjectMoveInfo.CreateMObjectMoveInfo(builder,
            sourceSequence,
            posX,
            posY,
            dir,
            0, 0, 0);

        // Create MObjectInfo

        var userNameOffSet = builder.CreateString(userName);
        var objectInfo = MObjectInfo.CreateMObjectInfo(builder,
            sourceSequence,
            1, // Player
            userNameOffSet,
            appearenceId,
            moveInfo);

        // Create sync_list
        List<Google.FlatBuffers.Offset<MObjectInfo>> syncList = new();
        syncList.Add(objectInfo);
        var syncListOffset = builder.CreateVectorOfTables(syncList.ToArray());

        // Create SC_AddNotification
        var addNotification = SCAddNotification.CreateSCAddNotification(builder,
            syncListOffset,
            EProtocol.SC_AddNotification);

        builder.Finish(addNotification.Value);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_AddNotification,
            builder.DataBuffer.ToSizedArray(),
            builder.Offset);

        foreach (var pair in m_players)
        {
            var player = pair.Value;

            if (false == player.IsDisconnected() &&
                player.GetPeer().IsSet == true &&
                player.IsLoadCompleted())
            {
                continue;
            }

            var pcpeer = player.GetPeer();

            Packet packet = new Packet();
            packet.Create(
                wrapper.GetRawData(),
                wrapper.GetRawSize(),
                PacketFlags.None);

            if (!pcpeer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
            {
                Log.PrintLog("Fail Send Room Info");
            }
        }
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

        foreach (var pair in m_players)
        {
            PC player = pair.Value;

            var pcpeer = player.GetPeer();
            if (pcpeer.NativeData == peer.NativeData)
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
            if (!player.IsDeadState()) continue;

            long elapsed = currentTime - player.GetDeathTime();

            if (elapsed >= 5000 && !player.IsRemoveSent())
            {
                BroadcastRemoveNotification(player.GetSequenceID());
                player.SetRemoveSent(true);

                Log.PrintLog($"[Room] Player {player.GetSequenceID()} removed (5s after death)");
            }

            if (elapsed >= 6000)
            {
                player.SetHP(100);
                player.SetDead(false, 0, 0);
                player.SetState(ObjectState.Normal);

                float respawnX = (float)(rand.Next() % 20 - 10);
                float respawnY = (float)(rand.Next() % 20 - 10);
                player.SetPosition(respawnX, respawnY);

                SendChangeStateNotification(
                    player.GetSequenceID(),
                    (int)ObjectState.Normal,
                    respawnX,
                    respawnY);

                BroadcastAddNotification(
                    player.GetSequenceID(),
                    player.GetName(),
                    player.GetAppearanceID(),
                    respawnX,
                    respawnY,
                    player.GetDirection());

                Log.PrintLog($"[Room] Player {player.GetSequenceID()} respawned ad ({respawnX}, {respawnY})");
            }
        }

        // broadcast 'move'
        if (0 != m_moveQueue.Count && 0 != m_players.Count)
        {
            BroadcastMoves();
            m_moveQueue.Clear();
        }
    }

    private void BroadcastMoves()
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(1024);
        List<Offset<MObjectMoveInfo>> synclist = new List<Offset<MObjectMoveInfo>>();

        foreach (var move in m_moveQueue)
        {
            var moveInfo = MObjectMoveInfo.CreateMObjectMoveInfo(builder,
                move.playerSequence,
                move.positionX,
                move.positionY,
                move.direction,
                0,  // move speed
                move.moveFlag,
                move.aimDirection
                );

            synclist.Add(moveInfo);

            if (builder.Offset >= SYNC_PACKET_SPLIT_LIMIT_SIZE)
            {
                var syncListOffset = builder.CreateVectorOfTables(synclist.ToArray());

                var syncTick = SteadyClock.Now().TimeSinceEpochMs;

                var scSyncMove = SCSyncMove.CreateSCSyncMove(builder,
                    syncTick,
                    0,
                    syncListOffset,
                    EProtocol.SC_SyncMove);

                PacketWrapper wrapper = PacketWrapper.Create(
                    EProtocol.SC_SyncMove,
                    builder.DataBuffer.ToSizedArray(),
                    builder.Offset);

                Packet packet = new Packet();
                packet.Create(
                    wrapper.GetRawData(),
                    wrapper.GetRawSize(),
                    PacketFlags.None);

                foreach (var pair in m_players)
                {
                    PC player = pair.Value;
                    if (false == player.IsDisconnected() &&
                        player.GetPeer().NativeData != IntPtr.Zero &&
                        player.IsLoadCompleted())
                    {
                        if (!player.GetPeer().Send(UDPConn.CHANNEL_UNRELIABLE, ref packet))
                        {
                            Log.PrintLog("Fail Send Pong");
                        }
                    }
                }
                builder.Clear();
                synclist.Clear();
            }
        }

        if (synclist.Count != 0)
        {
            var syncListOffset = builder.CreateVectorOfTables(synclist.ToArray());
            var syncTick = SteadyClock.Now().TimeSinceEpochMs;

            var scSyncMove = SCSyncMove.CreateSCSyncMove(builder,
                syncTick,
                0,
                syncListOffset,
                EProtocol.SC_SyncMove);

            builder.Finish(syncListOffset.Value);

            PacketWrapper wrapper = PacketWrapper.Create(
                  EProtocol.SC_SyncMove,
                  builder.DataBuffer.ToSizedArray(),
                  builder.Offset);

            Packet packet = new Packet();
            packet.Create(
                wrapper.GetRawData(),
                wrapper.GetRawSize(),
                PacketFlags.Unsequenced);

            int sentCount = 0;
            foreach (var pair in m_players)
            {
                PC player = pair.Value;
                if ((!player.IsDisconnected() &&
                    player.GetPeer().NativeData != IntPtr.Zero &&
                    player.IsLoadCompleted()))
                {
                    if (player.GetPeer().Send(UDPConn.CHANNEL_UNRELIABLE, ref packet))
                    {
                        sentCount++;
                    }
                }
            }
            //Log.PrintLog($"[SEND] SC_SyncMove to {sentCount} player {synclist.Count} moves");
        }
    }

    private void SendChangeStateNotification(int playerSequence, int state, float posX, float posY)
    {
        if (!m_players.TryGetValue(playerSequence, out PC player)) return;
        if (player.IsDisconnected() || player.GetPeer().NativeData == IntPtr.Zero) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        var stateNotification = SCChangeStateNotification.CreateSCChangeStateNotification(builder,
            state,
            posX,
            posY,
            EProtocol.SC_ChangeStateNotification);

        builder.Finish(stateNotification.Value);

        PacketWrapper wrapper = PacketWrapper.Create(
        EProtocol.SC_ChangeStateNotification,
        builder.DataBuffer.ToSizedArray(),
        builder.Offset);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.None);

        if (!player.GetPeer().Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail Send Pong");
        }

        Log.PrintLog($"[Room] Sent SCChangeStateNotification to player {playerSequence} state={state}");
    }

    private void CleanupExpiredPlayers(long currentTime)
    {
        m_playersToRemove.Clear();
        foreach (var pair in m_players)
        {
            PC player = pair.Value;

            if (false == player.IsDisconnected()) continue;

            long timeSinceLastReceived = currentTime - player.GetDisConnectTime();
            if (timeSinceLastReceived > CLEANUP_TIMEOUT_MS)
            {
                m_playersToRemove.Add(player.GetSequenceID());

                Log.PrintLog($"[Room] Player {player.GetSequenceID()} expired (30s cleanup)");
            }
        }
    }

    private void CheckPlayerTimeout(long currentTime)
    {
        foreach (var pair in m_players)
        {
            PC player = pair.Value;
            if (player.IsDisconnected()) continue;

            long timeSinceLastReceived = currentTime - player.GetLastReceivedTime();
            if (timeSinceLastReceived > DISCONNECT_TIMEOUT_MS)
            {
                player.SetDisconnected(true, currentTime);

                BroadcastRemoveNotification(player.GetSequenceID());

                Log.PrintLog($"[Room] Player {player.GetSequenceID()} disconnected (timeout)");
            }
        }
    }

    internal bool IsFull() => m_players.Count >= MAX_PLAYERS;

    internal string GetRoomCode() => m_roomCode;

    internal void QueueMoveAction(MoveAction action)
    {
        m_moveQueue.Add(action);

        if (m_players.TryGetValue(action.playerSequence, out PC player))
        {
            player.SetPosition(action.positionX, action.positionY);
            player.SetDirection(action.direction);
            player.SetMoveFlag(action.moveFlag);
        }
    }

    internal void BroadcastRemoveNotification(int playerSequence)
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        var offset = SCRemoveNotification.CreateRemoveSequenceListVector(
            builder,
            new[] { playerSequence });

        var removeNotification = SCRemoveNotification.CreateSCRemoveNotification(
            builder,
            offset,
            EProtocol.SC_RemoveNotification);

        builder.Finish(removeNotification.Value);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_RemoveNotification,
            builder.DataBuffer.ToSizedArray(),
            builder.Offset);

        foreach (var pair in m_players)
        {
            var player = pair.Value;

            if (player.GetSequenceID() != playerSequence &&
                player.IsDisconnected() == false &&
                player.GetPeer().IsSet == true &&
                player.IsLoadCompleted())
            {
                continue;
            }

            var pcpeer = player.GetPeer();

            Packet packet = new Packet();
            packet.Create(
                wrapper.GetRawData(),
                wrapper.GetRawSize(),
                PacketFlags.None);

            if (!pcpeer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
            {
                Log.PrintLog("Fail - Send Broadcast Remove Noti");
            }
        }
    }
}