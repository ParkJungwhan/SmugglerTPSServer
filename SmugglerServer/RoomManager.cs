using System.Diagnostics;
using System.Security.Cryptography;
using ENet;
using Google.FlatBuffers;
using SmugglerServer.Lib;
using TPSServer.Lib;

namespace SmugglerServer;

internal class RoomManager
{
    private Dictionary<Peer, Room> m_playerRoomMap;
    private List<Room> m_rooms;

    private int ROOM_MAX_COUNT = 50;
    private Host m_host;
    private Room m_currentRoom;

    private static readonly char[] Charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public RoomManager()
    {
        m_playerRoomMap = new Dictionary<Peer, Room>();
        m_rooms = new List<Room>(ROOM_MAX_COUNT);
    }

    internal void AddWaitingPlayer(Peer peer, int playerSequence, int sessionKey, string deviceKey, string userName, int appearanceId)
    {
        AddPlayerToRoom(peer, playerSequence, sessionKey, userName, appearanceId);
    }

    private void AddPlayerToRoom(Peer peer, int playerSequence, int sessionKey, string userName, int appearanceId)
    {
        Room targetRoom = null;

        if (m_currentRoom is not null && !m_currentRoom.IsFull())
        {
            targetRoom = m_currentRoom;
        }
        else
        {
            string roomCode = GenerateRoomCode();
            Debug.WriteLine($"{DateTime.Now}\tMake Room Code : {roomCode}");
            Room newRoom = new Room(roomCode);
            newRoom.SetHost(m_host);
            m_rooms.Add(newRoom);
            m_currentRoom = newRoom;
            targetRoom = newRoom;
        }

        if (targetRoom.AddPlayer(peer, playerSequence, sessionKey, userName, appearanceId))
        {
            m_playerRoomMap[peer] = targetRoom;

            PC player = targetRoom.GetPlayer(playerSequence);
            float startX = player is null ? 0.0f : player.GetPositionX();
            float startY = player is null ? 0.0f : player.GetPositionY();

            FlatBufferBuilder builder = new FlatBufferBuilder(1024);
            var roomCodeOffset = builder.CreateString(targetRoom.GetRoomCode());
            var enterRoom = Protocol.SCEnterRoom.CreateSCEnterRoom(builder,
                sessionKey,
                playerSequence,
                roomCodeOffset,
                appearanceId,
                startX,
                startY,
                Protocol.EProtocol.SC_EnterRoom);
            builder.Finish(enterRoom.Value);

            PacketWrapper wrapper = PacketWrapper.Create(
                Protocol.EProtocol.SC_EnterRoom,
                builder.DataBuffer.ToFullArray(),
                builder.Offset);

            Packet packet = new Packet();
            packet.Create(
                wrapper.GetRawData(),
                wrapper.GetRawSize(),
                PacketFlags.Reliable);

            if (!peer.Send(UDPConn.CHANNEL_RELIABLE, ref packet))
            {
                Log.PrintLog("Fail SCEnterRoom");
            }

            m_host?.Flush();

            Log.PrintLog($"[SEND] SC_EnterRoom (Seq: {playerSequence}, Room: {targetRoom.GetRoomCode()}, Appearance: {appearanceId}, Pos: {startX},{startY})");
        }
    }

    private string GenerateRoomCode()
    {
        Span<byte> buffer = stackalloc byte[4]; // 4자리 코드
        RandomNumberGenerator.Fill(buffer);

        char[] code = new char[4];

        for (int i = 0; i < 4; i++)
        {
            int index = buffer[i] % Charset.Length;
            code[i] = Charset[index];
        }

        return new string(code);
    }

    internal int[] GetAndClearExpiredPlayers()
    {
        return new int[] { };
    }

    internal Room GetPlayerRoom(Peer peer)
    {
        if (!m_playerRoomMap.TryGetValue(peer, out var room))
        {
            Log.PrintLog($"no find room : {peer.ID}", MsgLevel.Warning);
        }
        return room;
    }

    internal void SetHost(Host host)
    {
        m_host = host;
    }

    internal void Update(long currentTime)
    {
        foreach (var room in m_rooms)
        {
            if (room is not null && !room.IsEmpty())
            {
                //room->Update(currentTime);
                room.Update(currentTime);
            }
        }
    }
}