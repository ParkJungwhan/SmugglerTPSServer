namespace SmugglerLib.Commons;

public struct MoveAction
{
    public int playerSequence;
    public float positionX;
    public float positionY;
    public int direction;
    public int moveFlag;
    public int aimDirection;
};

public struct AttackResult
{
    public int attackerSequence;
    public int attackId;
    public bool isHit;
    public int targetSequence;
    public float startX;
    public float startY;
    public float endX;
    public float endY;
    public int damage;
    public int targetCurrentHp;
    public bool isDead;
    public int deathAnimId;
};