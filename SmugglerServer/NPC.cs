namespace SmugglerServer;

// NPC FSM States
internal enum NPCState
{
    Idle = 0,
    Chase = 1,
    Attack = 2,
    ReturnToSpawn = 3,
    Dead = 4
};

internal class NPC : BaseObject
{
    private NPCState m_fsmState;

    internal NPCState GetFSMState() => m_fsmState;
}