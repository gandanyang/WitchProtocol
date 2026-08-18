using Godot;

namespace MagicThunder.Data;

/// <summary>
/// 敌人行为类型（M2 割草爽感：不同攻击逻辑，不做数量堆砌）。
/// 每个类型一个 .tres（data/enemy_*.tres），Enemy 按 Kind 选配置加载。
/// </summary>
public enum EnemyKind
{
    /// 基础飞行怪：朝玩家缓移 + 周期瞄准吐弹（教学，数量最多）。
    Chaser = 0,
    /// 自爆怪：高速冲脸，接触玩家爆炸（范围伤害，逼移动）。
    Bomber = 1,
    /// 狙击怪：停下 → 红色锁定线预警 → 高速瞄准弹（观察能力）。
    Sniper = 2,
    /// 弹幕怪：不追人，制造环形弹幕（场面压力）。
    Sprayer = 3,
}

/// <summary>
/// 敌人配置（M2 数据驱动扩展）：基础战斗数值 + 类型特有参数。
/// 数值生产纪律：敌人数值一律走 .tres 资源（不在代码里散写），改数值只改 data/ 下资源。
/// 规则见 docs/开发约束与架构入口.md。
/// </summary>
[GlobalClass]
public partial class EnemyConfig : Resource
{
    [Export] public EnemyKind Kind { get; set; } = EnemyKind.Chaser;
    [Export] public int MaxHp { get; set; } = 3;
    [Export] public float MoveSpeed { get; set; } = 0f;
    [Export] public float ShootInterval { get; set; } = 1.2f;
    [Export] public float BulletSpeed { get; set; } = 180f;
    [Export] public float ContactDamage { get; set; } = 1f;
    // —— 类型特有参数（默认值 = 未配置时的安全回退）——
    /// Bomber 自爆判定半径（px）。
    [Export] public float ExplosionRadius { get; set; } = 60f;
    /// Sniper 锁定线预警时长（秒）。
    [Export] public float LockTime { get; set; } = 0.6f;
    /// Sprayer 环形弹幕数量。
    [Export] public int PatternCount { get; set; } = 10;
    /// 掉落经验（星之残片数量）。
    [Export] public int DropExp { get; set; } = 1;
}
