namespace SmugglerServer;

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

    internal int GetAppearanceID() => m_appearanceId;

    internal int GetSequenceID() => m_sequenceID;

    internal float GetPositionX() => m_positionX;

    internal float GetPositionY() => m_positionY;

    internal int GetDirection() => m_direction;

    internal int GetMoveFlag() => m_moveFlag;

    internal string GetName() => m_name;
}