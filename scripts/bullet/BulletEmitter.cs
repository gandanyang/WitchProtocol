using Godot;

namespace MagicThunder.Bullet;

/// <summary>
/// 弹幕发射器：data spec → pattern 数学 → 对象池取弹。
/// 所有弹幕都从这里出（禁止各处自行 new 子弹），保证走对象池、走统一 pattern 数学。
/// </summary>
public partial class BulletEmitter : Node
{
    public BulletPool Pool { get; } = new BulletPool(512);

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
            b.Position = spec.From;
            b.Configure(v, spec.Damage, spec.IsPlayerBullet);
            b.Recycle = Pool.Release; // 命中/出屏后回收到本发射器的池，保住对象池复用
        }
    }
}