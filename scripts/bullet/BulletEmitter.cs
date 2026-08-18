using Godot;

namespace MagicThunder.Bullet;

/// <summary>
/// 弹幕发射器：data spec → pattern 数学 → 对象池取弹。
/// 所有弹幕都从这里出（禁止各处自行 new 子弹），保证走对象池、走统一 pattern 数学。
/// </summary>
public partial class BulletEmitter : Node
{
    // BUG-003：预分配保守初始值 64（不是 512）——Main 开场玩家+敌人两发射器约 128 个 Area2D，
    // 峰值不足时 BulletPool.Spawn 会惰性扩容（见 BulletPool），避免多敌人/多波次拖慢启动与内存。
    public BulletPool Pool { get; } = new BulletPool(64);

    /// <summary>
    /// P0-1（敌弹世界坐标）：战场弹幕层（Main 装配的 "EnemyBullets" 节点，挂战场原点）。
    /// 设置后，Emit 出的子弹会 reparent 到此层——脱离发射器所在子树（如 Enemy 子节点），
    /// 敌人移动/死亡不再带动弹幕；层在原点时 LocalPosition ≈ 世界坐标。
    /// 未设置时回退旧行为（子弹挂发射器子树下），保证探针/独立场景安全。
    /// </summary>
    public Node2D? WorldLayer { get; set; }

    public override void _Ready()
    {
        AddChild(Pool);
    }

    public void Emit(PatternSpec spec)
    {
        Vector2[] velocities = spec.Kind switch
        {
            BulletPatternKind.Aimed => new[] { BulletPatterns.Aimed(spec.From, spec.Target, spec.Speed) },
            BulletPatternKind.Spread => BulletPatterns.Spread(spec.From, spec.BaseAngleDeg, spec.Count, spec.GapDeg, spec.Speed),
            BulletPatternKind.Ring => BulletPatterns.Ring(spec.From, spec.Count, spec.Speed, spec.SpinDeg, spec.RingIndex),
            BulletPatternKind.Spiral => BulletPatterns.Spiral(spec.From, spec.Count, spec.Speed, spec.SpinDeg),
            _ => System.Array.Empty<Vector2>(),
        };

        foreach (var v in velocities)
        {
            var b = Pool.Spawn();
            // P0-1：若配置了世界层，子弹脱离发射器子树。
            // 注意 Godot 的 AddChild 不自动 reparent——先摘除旧父节点，再挂世界层。
            if (WorldLayer != null && b.GetParent() != WorldLayer)
            {
                b.GetParent()?.RemoveChild(b);
                WorldLayer.AddChild(b);
            }
            b.Position = spec.From; // 世界层挂战场原点 → LocalPosition ≈ 世界坐标
            b.Configure(v, spec.Damage, spec.IsPlayerBullet);
            b.Recycle = Pool.Release; // 命中/出屏后回收到本发射器的池，保住对象池复用
        }
    }
}