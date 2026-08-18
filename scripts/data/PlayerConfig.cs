using Godot;

namespace MagicThunder.Data;

/// <summary>
/// 玩家数值配置（数据驱动示例）。
/// 数值生产纪律：数值一律走 .tres 资源（不在代码里散写），改数值只改 data/ 下资源，不动代码。
/// 规则见 docs/开发约束与架构入口.md。
/// </summary>
[GlobalClass]
public partial class PlayerConfig : Resource
{
    [Export] public float MoveSpeed { get; set; } = 320f;
    [Export] public float FocusSpeedRatio { get; set; } = 0.5f;
    [Export] public int MaxHp { get; set; } = 3;
    [Export] public int BombCount { get; set; } = 3;
    [Export] public float FireInterval { get; set; } = 0.15f;
    [Export] public float BulletSpeed { get; set; } = 520f;
}