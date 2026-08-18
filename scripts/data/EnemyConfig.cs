using Godot;

namespace MagicThunder.Data;

/// <summary>
/// 最小敌人配置（MVP EnemyConfig，控制范围：暂只含战斗基础数值）。
/// 数值生产纪律：敌人数值一律走 .tres 资源（不在代码里散写），改数值只改 data/ 下资源。
/// 后续扩展（attack_pattern / death_effect / drop_exp / enemy_type / elite_modifier…）在此增量补充。
/// 规则见 docs/开发约束与架构入口.md。
/// </summary>
[GlobalClass]
public partial class EnemyConfig : Resource
{
    [Export] public int MaxHp { get; set; } = 3;
    [Export] public float MoveSpeed { get; set; } = 0f;
    [Export] public float ShootInterval { get; set; } = 1.2f;
    [Export] public float BulletSpeed { get; set; } = 180f;
    [Export] public float ContactDamage { get; set; } = 1f;
}
