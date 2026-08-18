using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Effects;

namespace MagicThunder.Enemy;

/// <summary>
/// 敌人（M2 割草爽感扩展）：数据驱动 + 类型化行为。
/// 数值来自 data/enemy_*.tres（按 <see cref="Kind"/> 选配置；加载失败回退默认值，不阻断运行）。
///
/// 类型（见 <see cref="Data.EnemyKind"/>）：
///  Chaser  基础飞行怪：朝玩家缓移 + 周期瞄准吐弹（教学）
///  Bomber  自爆怪：高速冲脸，接触玩家按 ExplosionRadius 判定爆炸（逼移动）
///  Sniper  狙击怪：进场停靠 → 红色锁定线预警 → 高速瞄准弹（观察能力）
///  Sprayer 弹幕怪：不追人，周期性环形弹幕（场面压力）
///
/// 帧率优化专项（2026-08-19）：去物理体——Node2D 不是 Area2D，无 CollisionShape2D/碰撞层，
/// 视觉 = 共享程序化圆盘贴图（Modulate 染色，可合批，替代 _Draw DrawCircle 自绘）。
/// 命中/接触判定改由 Main 里的自写距离碰撞检测驱动（见 Main.CheckCollisions）。
/// </summary>
public partial class Enemy : Node2D
{
    private const string DefaultConfigPath = "res://data/EnemyConfig.tres";
    private const string BomberConfigPath = "res://data/enemy_bomber.tres";
    private const string SniperConfigPath = "res://data/enemy_sniper.tres";
    private const string SprayerConfigPath = "res://data/enemy_sprayer.tres";

    /// 接触/命中判定半径（px），供 Main 距离碰撞检测使用。
    public const float Radius = 16f;
    private const float HitFlashDuration = 0.08f;
    /// 32px 程序化圆盘 → 直径 = Radius*2 的视觉尺寸（视觉半径 == 判定半径，弹幕本体命中）。
    private const float SpriteScale = Radius * 2f / 32f;
    /// 狙击手停靠位 = 战场高度比例（进场上半区，方便玩家观察锁定线）。
    private const float SniperApproachYRatio = 0.42f;

    /// 敌人行为类型。WaveManager 生成时预置；_Ready 读配置覆盖（配置是权威值）。
    public Data.EnemyKind Kind { get; set; } = Data.EnemyKind.Chaser;

    public int MaxHp { get; private set; } = 3;
    public float MoveSpeed { get; private set; } = 0f;
    public float ShootInterval { get; private set; } = 1.2f;
    public float BulletSpeed { get; private set; } = 180f;
    public float ContactDamage { get; private set; } = 1f;
    public int KillScore { get; private set; } = 100;
    // M2：类型特有参数 + 掉落经验
    public float ExplosionRadius { get; private set; } = 60f;
    public float LockTime { get; private set; } = 0.6f;
    public int PatternCount { get; private set; } = 10;
    public int DropExp { get; private set; } = 1;

    /// 接触判定半径：自爆怪用爆炸半径（范围判定），其余用本体半径。
    public float ContactRadius => Kind == Data.EnemyKind.Bomber ? ExplosionRadius : Radius;

    private int _hp;
    private Node2D? _target;
    private readonly BulletEmitter _emitter = new();
    private Sprite2D? _body;
    private Color _baseColor = Colors.Firebrick; // 类型基础色（闪白后恢复用）
    private float _shootTimer;
    private float _flashTimer;

    // ---- 狙击怪状态机（进场停靠 → 锁定预警 → 发射 → 冷却）----
    private enum SniperState { Approach, Lock, Wait }
    private SniperState _sniper = SniperState.Approach;
    private float _lockTimer;
    private Vector2 _lockDir;
    private float _approachY;
    private Line2D? _lockLine;

    public override void _Ready()
    {
        // 数据驱动：按 Kind 选配置加载；失败回退默认值（不阻断运行）
        // M1-PERF：CacheMode.Reuse 复用缓存——配置是静态内容，避免重复读磁盘 .tres。
        var res = ResourceLoader.Load(ConfigPathFor(Kind), "", ResourceLoader.CacheMode.Reuse);
        if (res is Data.EnemyConfig cfg)
        {
            Kind = cfg.Kind;
            MaxHp = cfg.MaxHp;
            MoveSpeed = cfg.MoveSpeed;
            ShootInterval = cfg.ShootInterval;
            BulletSpeed = cfg.BulletSpeed;
            ContactDamage = cfg.ContactDamage;
            ExplosionRadius = cfg.ExplosionRadius;
            LockTime = cfg.LockTime;
            PatternCount = cfg.PatternCount;
            DropExp = cfg.DropExp;
        }

        _approachY = GameManager.I?.PlayfieldSize.Y * SniperApproachYRatio ?? 200f;

        // 视觉：共享程序化圆盘贴图 + 染色（可合批）——替代 _Draw DrawCircle 逐节点自绘
        _baseColor = Kind switch
        {
            Data.EnemyKind.Bomber => new Color(1f, 0.45f, 0.15f), // 橙红：危险冲脸
            Data.EnemyKind.Sniper => new Color(0.4f, 0.9f, 1f),   // 青蓝：锁定预警
            Data.EnemyKind.Sprayer => new Color(0.75f, 0.3f, 1f), // 紫：弹幕压制
            _ => Colors.Firebrick,
        };
        _body = new Sprite2D
        {
            Texture = ProceduralTextures.Dot(),
            Scale = Vector2.One * SpriteScale,
            Modulate = _baseColor,
        };
        AddChild(_body);
        AddChild(_emitter);

        // P0-1（敌弹世界坐标）：查找 Main 装配的战场弹幕层 "EnemyBullets"（挂战场原点），
        // 注入后敌弹 reparent 到世界层，不随 Enemy 移动/死亡。找不到则回退（弹幕挂本节点下）。
        if (GetTree().Root.FindChild("EnemyBullets", true, false) is Node2D worldLayer)
            _emitter.WorldLayer = worldLayer;

        _hp = MaxHp;
    }

    /// 按行为类型返回对应 .tres 路径（未命中回退基础配置）。
    private static string ConfigPathFor(Data.EnemyKind kind) => kind switch
    {
        Data.EnemyKind.Bomber => BomberConfigPath,
        Data.EnemyKind.Sniper => SniperConfigPath,
        Data.EnemyKind.Sprayer => SprayerConfigPath,
        _ => DefaultConfigPath,
    };

    public void SetTarget(Node2D? target) => _target = target;

    /// 接触伤害（由 Main 距离碰撞检测触发，防贴脸无反馈）：玩家受击 + 自身消失。
    /// 自爆怪接触 = 爆炸：范围判定（ExplosionRadius）+ 更强震屏。
    public void ContactPlayer()
    {
        if (_hp <= 0) return; // 已阵亡（QueueFree 挂起中）不再触发
        EventBus.I.Dispatch("player_hit");
        if (Kind == Data.EnemyKind.Bomber)
            FeedbackSystem.ShakeScreen(0.35f, 10f); // 自爆：更强的受击反馈
        QueueFree();
    }

    /// 被玩家弹命中（由 Main 距离碰撞检测调用）。归零则阵亡并通知结算（payload=自身，携带 KillScore）。
    public void TakeDamage(int damage)
    {
        if (_hp <= 0) return; // 已阵亡（QueueFree 挂起中）防同帧重复结算
        _hp -= damage;
        _flashTimer = HitFlashDuration;
        if (_body != null) _body.Modulate = Colors.White; // 受击闪白
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
            if (_flashTimer <= 0f && _body != null) _body.Modulate = _baseColor; // 闪白结束恢复类型色
        }

        switch (Kind)
        {
            case Data.EnemyKind.Bomber: UpdateBomber(dt); break;
            case Data.EnemyKind.Sniper: UpdateSniper(dt); break;
            case Data.EnemyKind.Sprayer: UpdateSprayer(dt); break;
            default: UpdateChaser(dt); break;
        }
    }

    /// 基础飞行怪：MoveSpeed 朝玩家缓移 + 周期瞄准吐弹（原有行为，数据驱动）。
    private void UpdateChaser(float dt)
    {
        if (_target == null) return;
        if (MoveSpeed > 0f)
            Position += (_target.Position - Position).Normalized() * MoveSpeed * dt;

        _shootTimer -= dt;
        if (_shootTimer <= 0f && IsInsideTree())
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

    /// 自爆怪：高速直线冲脸，不射击。接触由 Main 按 ExplosionRadius 触发 ContactPlayer。
    private void UpdateBomber(float dt)
    {
        if (_target == null || MoveSpeed <= 0f) return;
        Position += (_target.Position - Position).Normalized() * MoveSpeed * dt;
    }

    /// 狙击怪：进场停靠 → 红色锁定线跟踪玩家 LockTime → 高速瞄准弹 → 冷却 ShootInterval。
    private void UpdateSniper(float dt)
    {
        if (_target == null) return;
        switch (_sniper)
        {
            case SniperState.Approach:
                // 缓慢下探到停靠位（慢于追击，便于玩家看清锁定机制）
                var to = (_target.Position - Position).Normalized();
                Position += to * Mathf.Max(MoveSpeed * 0.35f, 15f) * dt;
                if (Position.Y >= _approachY)
                {
                    _sniper = SniperState.Lock;
                    _lockTimer = LockTime;
                }
                break;

            case SniperState.Lock:
                _lockTimer -= dt;
                ShowLockLine();
                if (_lockTimer <= 0f)
                {
                    _lockDir = (_target.Position - Position).Normalized();
                    HideLockLine();
                    FireAimed(_lockDir);
                    _sniper = SniperState.Wait;
                    _shootTimer = ShootInterval;
                }
                break;

            case SniperState.Wait:
                HideLockLine();
                _shootTimer -= dt;
                if (_shootTimer <= 0f)
                {
                    _sniper = SniperState.Lock;
                    _lockTimer = LockTime;
                }
                break;
        }
    }

    /// 弹幕怪：不追人，周期性环形弹幕（PatternCount 颗）。
    private void UpdateSprayer(float dt)
    {
        if (_target == null) return;
        _shootTimer -= dt;
        if (_shootTimer <= 0f && IsInsideTree())
        {
            _shootTimer = ShootInterval;
            _emitter.Emit(new PatternSpec
            {
                Kind = BulletPatternKind.Ring,
                From = Position,
                Count = PatternCount,
                Speed = BulletSpeed,
            });
        }
    }

    /// 发射高速瞄准弹（狙击怪专用，沿锁定方向）。
    private void FireAimed(Vector2 dir)
    {
        if (_emitter == null || !IsInsideTree()) return;
        _emitter.Emit(new PatternSpec
        {
            Kind = BulletPatternKind.Aimed,
            From = Position,
            Target = Position + dir * 100f,
            Speed = BulletSpeed,
        });
    }

    /// 锁定线（红色预警，从狙击怪指向玩家当前位置，每帧刷新）。
    private void ShowLockLine()
    {
        if (_target == null) return;
        if (_lockLine == null)
        {
            _lockLine = new Line2D
            {
                Width = 2f,
                DefaultColor = new Color(1f, 0.15f, 0.15f, 0.9f),
                ZIndex = 5,
            };
            AddChild(_lockLine);
        }
        _lockLine.ClearPoints();
        _lockLine.AddPoint(Position);
        _lockLine.AddPoint(_target.Position);
        _lockLine.Visible = true;
    }

    private void HideLockLine()
    {
        if (_lockLine != null) _lockLine.Visible = false;
    }
}
