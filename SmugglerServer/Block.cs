namespace SmugglerServer;

public enum eBlockDirection
{
    North = 0,
    East,
    South,
    West
}

internal class Block : BaseObject
{
    private int m_blockTemplateID;

    private void SetBlockTemplateID(int id) => m_blockTemplateID = id;

    private int GetBlockTemplateID() => m_blockTemplateID;

    public Block() : base()
    {
        m_blockTemplateID = 0;
        SetObjectType(3);
    }

    internal void Initialize(float posX, float posY, int hp)
    {
        SetPosition(posX, posY);
        SetDirection(0);
        SetMaxHP(hp);
        SetHP(hp);
        SetState(ObjectState.Normal);
        SetAppearanceId(0);
    }

    internal void Update(float deltaTime)
    {
        base.Update(deltaTime);
    }
}