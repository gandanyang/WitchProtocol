using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;

namespace MagicThunder.Enemy;

/// <summary>
/// 最小敌人（MVP 占位）：周期性朝玩家方向吐弹，可被玩家弹击倒。
/// 数值来自 data/EnemyConfig.tres（数据驱动；配置加载失败回退默认值，不阻断运行）。
/// 命中判定由玩家弹脚本驱动（见 Bullet.OnAreaEntered）。
/// 移动 AI（MoveSpeed）在垂直切片阶段接入。
/// </summary>
public partial class Enemy : Area2D
{
    private const string DefaultConfigPath = "res://data/EnemyConfig.tres";
    private const float RadiusPx = 16f;

    public int MaxHp { get; private set; } = 3;
    public float MoveSpeed { get; private set; } = 0f;
    public float ShootInterval { get; private set; } = 1.2f;
    public float BulletSpeed { get; private set; } = 180f;
    public float ContactDamage { get; private set; } = 1f;

    private int _hp;
    private Node2D? _target;
    private readonly BulletEmitter _emitter = new();
    private float _shootTimer;

    public override void _Ready()
    {
        // 数据驱动：优先加载 EnemyConfig.tres；失败回退默认值（不阻断运行）
        var res = ResourceLoader.Load(DefaultConfigPath, "", ResourceLoader.CacheMode.Ignore);
        if (res is Data.EnemyConfig cfg)
        {
            MaxHp = cfg.MaxHp;
            MoveSpeed = cfg.MoveSpeed;
            ShootInterval = cfg.ShootInterval;
            BulletSpeed = cfg.BulletSpeed;
            ContactDamage = cfg.ContactDamage;
        }

        CollisionLayer = 2; // 供玩家弹 area_entered 检测
        CollisionMask = 0;  // 自身不主动检测（受击由玩家弹脚本处理）
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = RadiusPx } });
        AddChild(_emitter);
        _hp = MaxHp;
        QueueRedraw(); // 占位外观，正式美术接入后移除 _Draw
    }

    public override void _Draw() => DrawCircle(Vector2.Zero, RadiusPx, Colors.Firebrick);

    public void SetTarget(Node2D target) => _target = target;

    /// 被玩家弹命中（由玩家弹脚本调用）。归零则阵亡并通知 Main 结算。
    public void TakeDamage(int damage)
    {
        _hp -= damage;
        if (_hp <= 0)
        {
            EventBus.I.Dispatch("enemy_killed");
            QueueFree();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _shootTimer -= (float)delta;
        if (_shootTimer <= 0f && _target != null && IsInsideTree())
        {
            _shootTimer = ShootInterval;
            _emitter.Emit(new PatternSpec
            {
                Kind = BulletPatternKind.Aimed,
                From = Position,
                Target = _target.Position,
                Speed = BulletSpeed,
            });
        }
    }
}
