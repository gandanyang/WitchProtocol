namespace MagicThunder.Bullet;

/// <summary>
/// 碰撞层/掩码协议常量表（BUG-006）：所有层数值集中于此，禁止各处硬编码。
/// 层约定：1 玩家本体 / 2 敌人 / 3 玩家弹 / 4 敌弹。
/// 掩码语义：玩家弹(3) mask=敌人(2)；敌弹(4) mask=玩家本体(1)。
/// </summary>
public static class CollisionLayers
{
    public const uint Player = 1;
    public const uint Enemy = 2;
    public const uint PlayerBullet = 3;
    public const uint EnemyBullet = 4;
}
