using ENet;
using SmugglerServer.Lib;

namespace SmugglerServer;

internal class RoomManager
{
    private Dictionary<Peer, Room> m_playerRoomMap;
    private List<Room> m_rooms;

    private int ROOM_MAX_COUNT = 50;
    private Host m_host;
    private Room m_currentRoom;

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
        }
    }

    private string GenerateRoomCode()
    {
        // TODO : 암호값으로 생성된 4자리 수를 리턴
        return null;
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