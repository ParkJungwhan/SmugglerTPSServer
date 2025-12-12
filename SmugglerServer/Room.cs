using System.Diagnostics;
using System.Net.Sockets;
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
    private Dictionary<int, NPC> m_npcs = new Dictionary<int, NPC>();
    private Dictionary<int, Block> m_blocks = new Dictionary<int, Block>();
    private List<MoveAction> m_moveQueue = new List<MoveAction>();
    private List<int> m_playersToRemove = new List<int>();

    private Host m_host;
    private string m_roomCode;
    private int m_nextNPCSequence;
    private int m_nextBlockSequence;
    private const float PI_VALUE = 3.14159265f;

    private Random rand = new Random(32767);

    private Room()
    { }

    public Room(string roomCode)
    {
        m_host = null;
        m_nextNPCSequence = 2000;
        m_nextBlockSequence = 3000;
        m_roomCode = roomCode;

        SpawnRandomBlocks(45, -25.0f, 25.0f, -250f, 25.0f);

        Log.PrintLog($"[Room] Created room '{m_roomCode}' with {m_blocks.Count} blocks");
    }

    public void SetHost(Host host) => m_host = host;

    private void SpawnRandomBlocks(int count, float minX, float maxX, float minY, float maxY)
    {
        Log.PrintLog($"[Room] Spawning {count} block walls...");

        // -25 ~ 25
        if (minX < -25.0f) minX = -25.0f;
        if (maxX > 25.0f) maxX = 25.0f;
        if (minY < -25.0f) minY = -25.0f;
        if (maxY > 25.0f) maxY = 25.0f;

        int totalBlocksSpawned = 0;

        int blockDirCount = Enum.GetValues<eBlockDirection>().Length;

        for (int i = 0; i < count; i++)
        {
            //spawnX = -20.0f + (float)rand.NextDouble() * 40.0f;
            float startX = minX + (float)rand.NextDouble() * (maxX - minX);
            float startY = minY + (float)rand.NextDouble() * (maxY - minY);

            // 생성할 벽 길이
            int wallLength = 3 + rand.Next(0, 2);

            // 방향(4방향, eBlockDirection)
            int direction = rand.Next(0, blockDirCount);

            float dirX = 0.0f;
            float dirY = 0.0f;

            switch (direction)
            {
                case (int)eBlockDirection.North: // (+Y)
                    dirX = 0.0f; dirY = 1.0f;
                    break;

                case (int)eBlockDirection.East: // (+X)
                    dirX = 1.0f; dirY = 0.0f;
                    break;

                case (int)eBlockDirection.South: // (-Y)
                    dirX = 0.0f; dirY = -1.0f;
                    break;

                case (int)eBlockDirection.West: // (-X)
                    dirX = -1.0f; dirY = 0.0f;
                    break;
            }

            // 벽 길이만큼 붙여 만들어 던질 준비
            for (int j = 0; j < wallLength; j++)
            {
                float blockX = startX + dirX * j * 1.0f;
                float blockY = startY + dirY * j * 1.0f;

                if (blockX < minX) blockX = minX;
                if (blockX > maxX) blockX = maxX;
                if (blockY < minX) blockY = minY;
                if (blockY > maxX) blockY = maxY;

                // 일단은 1000으로 설정한다. 공격시 10씩 깎일 예정(부서짐 가능)
                int blockHp = 1000;

                int blockSeq = SpawnBlock(blockX, blockY, blockHp);
                BroadcastBlockAdd(blockSeq);
                totalBlocksSpawned++;
            }
        }

        Log.PrintLog($"[Room] Spawned {totalBlocksSpawned} blocks in {count} walls");
    }

    private void BroadcastBlockAdd(int blockSequence)
    {
        Block block = GetBlock(blockSequence);
        if (block is null)
        {
            Log.PrintLog("[Room] Broadcast BlockAdd failed : Block not found");
            return;
        }

        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        var moveInfo = MObjectMoveInfo.CreateMObjectMoveInfo(builder,
            blockSequence,
            block.GetPositionX(),
            block.GetPositionY(),
            0,  // 방향
            0,  // 속도
            0,  // 이동중인지
            0); // 조준 방향

        var nameOffset = builder.CreateString(block.GetName());

        var objectInfo = MObjectInfo.CreateMObjectInfo(builder,
            blockSequence,
            3,  // objecttype = block
            nameOffset,
            block.GetAppearanceID(),
            moveInfo);

        var syncListOffset = builder.AddBuilderOffsetToSingleMObj(objectInfo);

        var addNotification = SCAddNotification.CreateSCAddNotification(builder,
            syncListOffset,
            EProtocol.SC_AddNotification);

        builder.Finish(addNotification);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_AddNotification,
            builder);

        //Broadcast to all players
        int sentCount = 0;
        foreach (var pair in m_players)
        {
            var player = pair.Value;
            if (!player.IsDisconnected() &&
                player.GetPeer().NativeData != IntPtr.Zero &&
                player.IsLoadCompleted())
            {
                Packet packet = new Packet();
                packet.Create(
                    wrapper.GetRawData(),
                    wrapper.GetRawSize(),
                    PacketFlags.Reliable);

                if (false == player.GetPeer().Send(UDPConn.CHANNEL_RELIABLE, ref packet))
                {
                    Log.PrintLog("Fail Send Room - Block list", MsgLevel.Warning);
                }
                else
                    sentCount++;
            }
        }

        m_host?.Flush();
        Log.PrintLog($"[Room] Block Add broadcast: seq={blockSequence} send to {sentCount} players");
    }

    private Block GetBlock(int blockSequence) => m_blocks.TryGetValue(blockSequence, out Block block) ? block : null;

    private int SpawnBlock(float posX, float posY, int hp)
    {
        int blockSequence = m_nextBlockSequence++;

        Block block = new();
        block.Initialize(posX, posY, hp);
        block.SetSequenceID(blockSequence);
        block.SetName("Block_" + blockSequence);    // 일단은 설정하고 클라에서는 확인할수 있게(개발용)

        m_blocks[blockSequence] = block;
        Log.PrintLog($"[Room] Block spawned: Seq = {blockSequence} hp={hp} at ({posX}, {posY})");

        return blockSequence;
    }

    private void RemovePlayerBySequence(int playerSequence) => m_players.Remove(playerSequence);

    internal bool AddPlayer(Peer peer,
        int playerSequence,
        int sessionKey,
        string userName,
        int appearanceID)
    {
        if (IsFull()) return false;

        PC pc = new();
        pc.SetSequenceID(playerSequence);
        pc.SetName(userName);
        pc.SetPeer(peer);
        pc.SetSessionKey(sessionKey);
        pc.SetAppearanceId(appearanceID);

        float spawnX = 0.0f;
        float spawnY = 0.0f;
        bool foundSafeSpot = false;

        for (int attempt = 0; attempt < 100 && !foundSafeSpot; ++attempt)
        {
            spawnX = -20.0f + (float)rand.NextDouble() * 40.0f;
            spawnY = -20.0f + (float)rand.NextDouble() * 40.0f;

            // 같은 자리 중복생성 방지 확인(반경 1m)
            foundSafeSpot = !IsPositionBlockedByBlock(spawnX, spawnY, 1.0f);
        }

        if (!foundSafeSpot)
        {
            spawnX = spawnY = 0.0f;
        }

        pc.SetPosition(spawnX, spawnY);
        pc.SetDirection(0);
        pc.SetMoveFlag(0);
        pc.SetLoadCompleted(false);

        long currentTime = Library.Time;

        pc.UpdateLastReceived(currentTime);

        m_players[playerSequence] = pc;

        return true;
    }

    private bool IsPositionBlockedByBlock(float x, float y, float radius)
    {
        float BLOCK_RADIUS = 0.5f;
        foreach (var pair in m_blocks)
        {
            var block = pair.Value;

            if (block.GetState() != ObjectState.Normal) continue;

            float blockX = block.GetPositionX();
            float blockY = block.GetPositionY();

            float dx = x - blockX;
            float dy = y - blockY;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);

            float totalRadius = BLOCK_RADIUS + radius;
            if (distance < totalRadius)
            {
                return true;
            }
        }
        return false;
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

    internal void BroadcastAddNotification(
        int sourceSequence,
        string userName,
        int appearenceId,
        float posX,
        float posY,
        int dir)
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
        var syncListOffset = builder.AddBuilderOffsetToSingleMObj(objectInfo);

        // Create SC_AddNotification
        var addNotification = SCAddNotification.CreateSCAddNotification(builder,
            syncListOffset,
            EProtocol.SC_AddNotification);

        builder.Finish(addNotification);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_AddNotification,
            builder);

        foreach (var pair in m_players)
        {
            var player = pair.Value;

            if (false == player.IsDisconnected() &&
                IntPtr.Zero != player.GetPeer().NativeData &&
                true == player.IsLoadCompleted())
            {
                var pcpeer = player.GetPeer();

                Packet packet = new Packet();
                packet.Create(
                    wrapper.GetRawData(),
                    wrapper.GetRawSize(),
                    PacketFlags.None);

                if (!pcpeer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
                {
                    Log.PrintLog("Fail Send Room Info", MsgLevel.Warning);
                }
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
        List<Google.FlatBuffers.Offset<MObjectInfo>> syncList = new(m_blocks.Count + m_players.Count);
        foreach (var pair in m_blocks)
        {
            Block block = pair.Value;
            if (block.GetState() == ObjectState.Normal)
            {
                var moveInfo = MObjectMoveInfo.CreateMObjectMoveInfo(builder,
                    block.GetSequenceID(),
                    block.GetPositionX(),
                    block.GetPositionY(),
                    0,  // direction
                    0,  // move_speed
                    0,  // move_flag
                    0   // aim_direction
                    );

                var nameOffset = builder.CreateString(block.GetName());
                var objectInfo = MObjectInfo.CreateMObjectInfo(builder,
                    block.GetSequenceID(),
                    3,
                    nameOffset,
                    block.GetAppearanceID(),
                    moveInfo);

                syncList.Add(objectInfo);
            }
        }

        foreach (var pair in m_players)
        {
            PC player = pair.Value;

            var pcpeer = player.GetPeer();
            if (pcpeer.NativeData == peer.NativeData) continue;

            if (false == player.IsDisconnected())
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

        var addNotification = SCAddNotification.CreateSCAddNotification(
            builder,
            syncListOffset,
            EProtocol.SC_AddNotification);

        builder.Finish(addNotification);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_AddNotification,
            builder);

        Packet packet = new Packet();
        packet.Create(
            wrapper.GetRawData(),
            wrapper.GetRawSize(),
            PacketFlags.None);

        if (false == peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
        {
            Log.PrintLog("Fail Send Pong", MsgLevel.Warning);
        }
    }

    internal bool IsEmpty() => m_players.Count == 0;

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
                    builder);

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

        // Send remaining
        if (synclist.Count != 0)
        {
            var syncListOffset = builder.CreateVectorOfTables(synclist.ToArray());
            var syncTick = SteadyClock.Now().TimeSinceEpochMs;

            var scSyncMove = SCSyncMove.CreateSCSyncMove(builder,
                syncTick,
                0,
                syncListOffset,
                EProtocol.SC_SyncMove);

            builder.Finish(scSyncMove);

            PacketWrapper wrapper = PacketWrapper.Create(
                  EProtocol.SC_SyncMove,
                  builder);

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
            Debug.WriteLine($"[SEND] SC_SyncMove to {sentCount} player {synclist.Count} moves");
        }
    }

    private void SendChangeStateNotification(int playerSequence, int state, float posX, float posY)
    {
        if (false == m_players.TryGetValue(playerSequence, out PC player)) return;
        if (player.IsDisconnected() || player.GetPeer().NativeData == IntPtr.Zero) return;

        FlatBufferBuilder builder = new FlatBufferBuilder(1024);

        var stateNotification = SCChangeStateNotification.CreateSCChangeStateNotification(builder,
            state,
            posX,
            posY,
            EProtocol.SC_ChangeStateNotification);

        builder.Finish(stateNotification);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_ChangeStateNotification,
            builder);

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

    internal int[] GetExpiredPlayers() => m_playersToRemove.ToArray();

    private void CleanupExpiredPlayers(long currentTime)
    {
        m_playersToRemove.Clear();
        foreach (var pair in m_players)
        {
            PC player = pair.Value;

            if (false == player.IsDisconnected()) continue;
            //return;
            long timeSinceLastReceived = currentTime - player.GetDisConnectTime();
            if (timeSinceLastReceived > CLEANUP_TIMEOUT_MS)
            {
                m_playersToRemove.Add(player.GetSequenceID());

                Log.PrintLog($"[Room] Player {player.GetSequenceID()} expired (30s cleanup)", MsgLevel.Information);
            }
        }

        foreach (var pair in m_playersToRemove)
        {
            m_players.Remove(pair);
        }
    }

    private void CheckPlayerTimeout(long currentTime)
    {
        foreach (var pair in m_players)
        {
            PC player = pair.Value;
            if (player.IsDisconnected()) continue;

            //return;
            long timeSinceLastReceived = currentTime - player.GetLastReceivedTime();
            // 앞단에서 딜레이로 인해 30ms 안쪽은 제외
            if (timeSinceLastReceived >= DISCONNECT_TIMEOUT_MS)
            {
                player.SetDisconnected(true, currentTime);

                BroadcastRemoveNotification(player.GetSequenceID());

                Log.PrintLog($"[Room] Player {player.GetSequenceID()} disconnected (timeout) : {timeSinceLastReceived}", MsgLevel.Information);
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

        builder.Finish(removeNotification);

        PacketWrapper wrapper = PacketWrapper.Create(
            EProtocol.SC_RemoveNotification,
            builder);

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
                Log.PrintLog("Fail - Send Broadcast Remove Noti", MsgLevel.Warning);
            }
        }
    }

    internal AttackResult ProcessAttack(
        int attackerSequence,
        int attackId,
        float posX,
        float posY,
        int aimDirection)
    {
        AttackResult result = new AttackResult();
        result.attackerSequence = attackerSequence;
        result.attackId = attackId;
        result.isHit = false;
        result.targetSequence = 0;
        result.startX = posX;
        result.startY = posY;
        result.damage = 10; // 일단 기본값 10 깎는걸로 진행
        result.targetCurrentHp = 0;
        result.isDead = false;
        result.deathAnimId = 0;

        // Convert aim direction to radians
        float aimRad = aimDirection * PI_VALUE / 180.0f;
        float dirX = (float)Math.Sin(aimRad);
        float dirY = (float)Math.Cos(aimRad);

        // Default end position (miss)
        result.endX = posX + dirX * ATTACK_RANGE;
        result.endY = posY + dirY * ATTACK_RANGE;

        float closestDistance = ATTACK_RANGE;
        bool hitIsNPC = false;
        BaseObject hitTarget = null;

        foreach (var kvp in m_players)
        {
            PC target = kvp.Value;
            if (target == null) continue;

            if (target.GetSequenceID() == attackerSequence ||
                target.IsDisconnected() ||
                target.IsDeadState())
                continue;

            float targetX = target.GetPositionX();
            float targetY = target.GetPositionY();

            float toTagetX = targetX - posX;
            float toTagetY = targetY - posY;

            float dotProduct = toTagetX * dirX + toTagetY * dirY;

            if (dotProduct < 0) continue;

            // Calculate shortest distance from aim line to target
            float projX = posX + dirX * dotProduct;
            float projY = posY + dirY * dotProduct;
            float distToLine = (float)Math.Sqrt(
                (targetX - projX) * (targetX - projX) +
                (targetY - projY) * (targetY - projY)
            );

            // Check if within hit radius and range
            if (distToLine <= HIT_RADIUS && dotProduct <= ATTACK_RANGE)
            {
                // Select closest target
                if (dotProduct < closestDistance)
                {
                    closestDistance = dotProduct;
                    result.isHit = true;
                    result.targetSequence = target.GetSequenceID();
                    result.endX = targetX;
                    result.endY = targetY;
                    hitTarget = target;
                    hitIsNPC = false;
                }
            }
        }

        // npc를 때릴때의 처리부분
        //foreach (var kvp in m_npcs)
        //{
        //    NPC target = kvp.Value;

        //    // Skip self, dead NPCs, and invincible (ReturnToSpawn) NPCs
        //    if (target.GetSequenceID() == attackerSequence ||
        //        target.GetFSMState() == NPCState.Dead ||
        //        target.GetFSMState() == NPCState.ReturnToSpawn)
        //        continue;
        //}

        // Apply damage if hit
        if (true == result.isHit &&
            null != hitTarget &&
            true == hitIsNPC)
        {
            NPC npcTarget = hitTarget as NPC;
            if (npcTarget is null) { return result; }
        }

        Log.PrintLog($"[Room] Attack proccese - Attacker : {attackerSequence}, Hit: {result.isHit}, Target: {result.targetSequence}, HP: {result.targetCurrentHp}");

        return result;
    }

    internal void BroadcastAttackNotification(AttackResult result)
    {
        FlatBufferBuilder builder = new FlatBufferBuilder(1024);
        var syncAttack = SCSyncAttack.CreateSCSyncAttack(builder,
            result.attackerSequence,
            result.attackId,
            result.isHit,
            result.targetSequence,
            result.startX,
            result.startY,
            result.endX,
            result.endY,
            result.damage,
            result.targetCurrentHp,
            result.isDead,
            result.deathAnimId,
            EProtocol.SC_SyncAttack);

        builder.Finish(syncAttack);

        PacketWrapper wrapper = PacketWrapper.Create(EProtocol.SC_SyncAttack, builder);
        SendAllUserInRoom(wrapper, EProtocol.SC_SyncAttack);
    }

    private void SendAllUserInRoom(PacketWrapper wrapper, EProtocol protocol)
    {
        // 전체 유저에게 전송

        int sentCount = 0;
        foreach (var pair in m_players)
        {
            var player = pair.Value;
            if (!player.IsDisconnected() &&
                player.GetPeer().NativeData != IntPtr.Zero &&
                player.IsLoadCompleted())
            {
                Packet packet = new Packet();
                packet.Create(
                    wrapper.GetRawData(),
                    wrapper.GetRawSize(),
                    PacketFlags.Reliable);

                if (false == player.GetPeer().Send(UDPConn.CHANNEL_RELIABLE, ref packet))
                {
                    Log.PrintLog($"[Room] Fail Send user in Room {player.GetAppearanceID()}", MsgLevel.Warning);
                }
                else
                    sentCount++;
            }
        }
        Log.PrintLog($"[SEND] broadcast to all user in Room('{m_roomCode}') : {protocol.ToString()}");
    }
}