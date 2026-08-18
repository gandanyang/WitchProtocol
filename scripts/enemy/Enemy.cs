using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;

namespace MagicThunder.Enemy;

/// <summary>
/// 小怪（MVP 垂直切片）：数值来自 data/EnemyConfig.tres（数据驱动；加载失败回退默认值，不阻断运行）。
/// 行为：MoveSpeed 朝玩家缓移 + 周期性 Aimed 吐弹；被击闪白；击杀加分并派发 enemy_killed（payload=自身）。
/// 命中判定由玩家弹脚本驱动（见 Bullet.OnAreaEntered）。
/// </summary>
public partial class Enemy : Area2D
{
    private const string DefaultConfigPath = "res://data/EnemyConfig.tres";
    private const float RadiusPx = 16f;
    private const float HitFlashDuration = 0.08f;

    public int MaxHp { get; private set; } = 3;
    public float MoveSpeed { get; private set; } = 0f;
    public float ShootInterval { get; private set; } = 1.2f;
    public float BulletSpeed { get; private set; } = 180f;
    public float ContactDamage { get; private set; } = 1f;
    public int KillScore { get; private set; } = 100;

    private int _hp;
    private Node2D? _target;
    private readonly BulletEmitter _emitter = new();
    private float _shootTimer;
    private float _flashTimer;

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

        CollisionLayer = CollisionLayers.Enemy; // 供玩家弹 area_entered 检测
        CollisionMask = 0;  // 自身不主动检测（受击由玩家弹脚本处理）
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = RadiusPx } });
        AddChild(_emitter);

        // P0-1（敌弹世界坐标）：查找 Main 装配的战场弹幕层 "EnemyBullets"（挂战场原点），
        // 注入后敌弹 reparent 到世界层，不随 Enemy 移动/死亡。找不到则回退（弹幕挂本节点下）。
        if (GetTree().Root.FindChild("EnemyBullets", true, false) is Node2D worldLayer)
            _emitter.WorldLayer = worldLayer;

        _hp = MaxHp;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 受击闪白（HitFlash）给玩家命中反馈
        DrawCircle(Vector2.Zero, RadiusPx, _flashTimer > 0f ? Colors.White : Colors.Firebrick);
    }

    public void SetTarget(Node2D? target) => _target = target;

    /// 被玩家弹命中（由玩家弹脚本调用）。归零则阵亡并通知结算（payload=自身，携带 KillScore）。
    public void TakeDamage(int damage)
    {
        _hp -= damage;
        _flashTimer = HitFlashDuration;
        QueueRedraw();
        if (_hp <= 0)
        {
            EventBus.I.Dispatch("enemy_killed", this);
            QueueFree();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            QueueRedraw();
        }

        // 移动：MoveSpeed > 0 时朝玩家缓移（数据驱动，改 EnemyConfig.tres 生效）
        if (MoveSpeed > 0f && _target != null)
            Position += (_target.Position - Position).Normalized() * MoveSpeed * dt;

        _shootTimer -= dt;
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
