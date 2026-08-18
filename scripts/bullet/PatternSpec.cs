using Godot;

namespace MagicThunder.Bullet;

/// <summary>弹型枚举：新增弹型在 BulletPatterns 加纯函数 + 在此登记。</summary>
public enum BulletPatternKind { Aimed, Spread, Ring, Spiral }

/// <summary>
/// 一次发射的规格（数据驱动雏形：后续可整体下沉为 .tres 资源，避免数值散写代码）。
/// 用可读字段而不是冗长位置参数，便于在编辑器/Boss 表里配置。
/// </summary>
public class PatternSpec
{
    public BulletPatternKind Kind = BulletPatternKind.Aimed;

    /// 发射原点（战场局部坐标 ≈ 世界坐标，因为弹幕层挂在原点）。
    public Vector2 From;
    /// Aimed 专属：目标点。
    public Vector2 Target;

    public float Speed = 120f;
    public int Count = 1;
    /// Spread 专属：两发间夹角（度）。
    public float GapDeg = 10f;
    /// Spread 专属：扇面中轴角（度，0 = 指向右）。
    public float BaseAngleDeg = 0f;
    /// Ring/Spiral 专属：旋转错位（度）。
    public float SpinDeg = 0f;
    /// Ring 专属：第几环。
    public int RingIndex = 0;

    public int Damage = 1;

    /// 是否玩家弹（决定碰撞层与命中目标）。敌弹由敌人发射器留默认 false。
    public bool IsPlayerBullet;
}