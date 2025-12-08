namespace SmugglerServer;

internal enum ObjectState
{
    Normal = 0,
    Dead = 1
};

internal class BaseObject
{
    private float m_positionX;
    private float m_positionY;
    private int m_direction;

    private int m_sequenceID;              // object_sequence (player_sequence)
    private string m_name;

    // Object Type (1=Player, 2=NPC)
    private int m_objectType;

    // Appearance
    private int m_appearanceId;

    // State
    private int m_moveFlag;

    // HP
    private int m_hp;

    private int m_maxHp;
    private ObjectState m_state;

    internal int GetAppearanceID() => m_appearanceId;

    internal int GetSequenceID() => m_sequenceID;

    internal float GetPositionX() => m_positionX;

    internal float GetPositionY() => m_positionY;

    internal int GetDirection() => m_direction;

    internal int GetMoveFlag() => m_moveFlag;

    internal string GetName() => m_name;

    internal string SetName(string name) => m_name = name;

    internal void SetAppearanceId(int id) => m_appearanceId = id;

    internal void SetSequenceID(int playerSequence) => m_sequenceID = playerSequence;

    internal void SetPosition(float x, float y)
    {
        m_positionX = x;
        m_positionY = y;
    }

    internal void SetMoveFlag(int flag) => m_moveFlag = flag;

    internal void SetDirection(int dir) => m_direction = dir;

    internal void SetState(ObjectState state) => m_state = state;

    internal void SetHP(int hp)
    {
        m_hp = hp;
        if (m_hp < 0) m_hp = 0;
        if (m_hp > m_maxHp) m_hp = m_maxHp;
    }
}