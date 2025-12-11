using ENet;

namespace SmugglerServer;

internal class PC : BaseObject
{
    private Peer m_peer;
    private int m_sessionKey;
    private bool m_isDead;
    private long m_deathTime;
    private int m_deathAnimId;
    private bool m_isDisconnected;
    private long m_lastReceivedTime;
    private bool m_loadCompleted;
    private int m_moveFlag;
    private bool m_removeSent;
    private long m_disconnectTime;

    public PC()
    {
        // PC는 object_type = 1
        SetObjectType(1);
    }

    internal void SetPeer(Peer peer) => m_peer = peer;

    internal Peer GetPeer() => m_peer;

    internal void SetSessionKey(int sessionKey) => m_sessionKey = sessionKey;

    internal int GetSessionKey() => m_sessionKey;

    internal bool IsDeadState() => m_isDead;

    internal long GetDeathTime() => m_deathTime;

    internal bool IsDisconnected() => m_isDisconnected;

    internal void UpdateLastReceived(long currentTime) => m_lastReceivedTime = currentTime;

    internal void SetLoadCompleted(bool completed) => m_loadCompleted = completed;

    internal bool IsLoadCompleted() => m_loadCompleted;

    internal bool IsRemoveSent() => m_removeSent;

    internal void SetRemoveSent(bool sent) => m_removeSent = sent;

    internal void SetDead(bool dead, long time, int animId)
    {
        m_isDead = dead;
        m_deathTime = time;
        m_deathAnimId = animId;
        if (!dead)
        {
            m_removeSent = false;
        }
    }

    internal long GetLastReceivedTime() => m_lastReceivedTime;

    internal void SetDisconnected(bool disconnected, long currentTime)
    {
        m_isDisconnected = disconnected;
        if (disconnected)
        {
            m_disconnectTime = currentTime;
        }
    }

    internal long GetDisConnectTime() => m_disconnectTime;
}