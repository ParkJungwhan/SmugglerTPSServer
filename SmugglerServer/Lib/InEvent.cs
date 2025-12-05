namespace SmugglerServer.Lib;

internal struct MoveAction
{
    private int playerSequence;
    private float positionX;
    private float positionY;
    private int direction;
    private int moveFlag;
    private int aimDirection;
};

internal struct AttackResult
{
    private int attackerSequence;
    private int attackId;
    private bool isHit;
    private int targetSequence;
    private float startX;
    private float startY;
    private float endX;
    private float endY;
    private int damage;
    private int targetCurrentHp;
    private bool isDead;
    private int deathAnimId;
};