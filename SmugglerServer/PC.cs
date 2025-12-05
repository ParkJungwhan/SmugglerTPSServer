using System.Xml.Linq;
using ENet;

namespace SmugglerServer
{
    internal class PC : BaseObject
    {
        private Peer m_peer;
        private int m_sessionKey;
        private bool m_isDead;
        private long m_deathTime;
        private bool m_isDisconnected;

        internal void SetPeer(Peer peer) => m_peer = peer;

        internal Peer GetPeer() => m_peer;

        private void SetSessionKey(int key) => m_sessionKey = key;

        private int GetSessionKey() => m_sessionKey;

        internal bool IsDeadState() => m_isDead;

        internal long GetDeathTime() => m_deathTime;

        internal bool IsDisconnected() => m_isDisconnected;
    }
}