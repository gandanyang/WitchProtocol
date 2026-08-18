using Godot;

namespace MagicThunder.Data;

/// <summary>
/// Boss 配置（MVP 垂直切片）：血量 / 移动 / 弹幕参数全部数据驱动。
/// 数值生产纪律：Boss 数值一律走 .tres 资源，改数值只改 data/ 下资源，不动代码。
/// 后续扩展（阶段数 / 特殊机制 / 演出…）在此增量补充。
/// </summary>
[GlobalClass]
public partial class BossConfig : Resource
{
    // 代码默认值 = 编辑器新建资源时的初始值，也用作加载失败回退；
    // 与 Boss.cs 回退默认值保持一致，且与 data/BossConfig.tres 实际值不同（探针区分度）。
    [Export] public int MaxHp { get; set; } = 50;
    [Export] public float MoveSpeed { get; set; } = 30f;
    [Export] public float BulletSpeed { get; set; } = 200f;

    /// 阶段一（HP>50%）圆环弹：数量 / 发射间隔。
    [Export] public int RingCount { get; set; } = 12;
    [Export] public float RingInterval { get; set; } = 2.0f;

    /// 阶段二（HP≤50%）螺旋弹：单次数量 / 发射间隔。
    [Export] public int SpiralCount { get; set; } = 6;
    [Export] public float SpiralInterval { get; set; } = 0.7f;

    /// 阶段二 瞄准 burst 发射间隔。
    [Export] public float AimedInterval { get; set; } = 3.0f;
}
