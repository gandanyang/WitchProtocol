using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;

namespace MagicThunder.Enemy;

/// <summary>
/// Boss（MVP 垂直切片）：高 HP、左右漂移、多阶段弹幕（HP>50% 圆环；HP≤50% 螺旋 + 瞄准 burst），头顶血条。
/// 数值来自 data/BossConfig.tres（数据驱动；加载失败回退默认值，不阻断运行）。
/// 击杀派发 boss_killed → 胜利结算（见 Main）。
/// </summary>
public partial class Boss : Area2D
{
    private const string DefaultConfigPath = "res://data/BossConfig.tres";
    private const float RadiusPx = 30f;
    private const float HpBarWidth = 70f;
    private const float HpBarHeight = 7f;
    private const float HitFlashDuration = 0.08f;

    // 代码默认值 = 配置加载失败时的安全回退；须与 data/BossConfig.tres 的值不同，
    // 保证 boss_config 探针能区分「配置读取成功 / 失败」（见 TestProbes.ProbeBossConfig）。
    public int MaxHp { get; private set; } = 50;
    public float MoveSpeed { get; private set; } = 30f;
    public float BulletSpeed { get; private set; } = 200f;
    public int RingCount { get; private set; } = 12;
    public float RingInterval { get; private set; } = 2.0f;
    public int SpiralCount { get; private set; } = 6;
    public float SpiralInterval { get; private set; } = 0.7f;
    public float AimedInterval { get; private set; } = 3.0f;

    private int _hp;
    private Node2D? _target;
    private readonly BulletEmitter _emitter = new();
    private float _ringTimer;
    private float _spiralTimer;
    private float _aimedTimer;
    private float _spinDeg;
    private int _ringIndex;
    private float _flashTimer;
    private float _driftT;
    private float _baseX;

    public override void _Ready()
    {
        // 数据驱动：优先加载 BossConfig.tres；失败回退默认值（不阻断运行）
        var res = ResourceLoader.Load(DefaultConfigPath, "", ResourceLoader.CacheMode.Ignore);
        if (res is Data.BossConfig cfg)
        {
            MaxHp = cfg.MaxHp;
            MoveSpeed = cfg.MoveSpeed;
            BulletSpeed = cfg.BulletSpeed;
            RingCount = cfg.RingCount;
            RingInterval = cfg.RingInterval;
            SpiralCount = cfg.SpiralCount;
            SpiralInterval = cfg.SpiralInterval;
            AimedInterval = cfg.AimedInterval;
        }

        CollisionLayer = CollisionLayers.Enemy; // 供玩家弹 area_entered 检测
        CollisionMask = 0;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = RadiusPx } });
        AddChild(_emitter);

        // P0-1（敌弹世界坐标）：与 Enemy 一致，注入战场弹幕层 "EnemyBullets"（挂战场原点），
        // Boss 漂移/死亡不带动弹幕；找不到则回退（弹幕挂本节点下）。
        if (GetTree().Root.FindChild("EnemyBullets", true, false) is Node2D worldLayer)
            _emitter.WorldLayer = worldLayer;

        _hp = MaxHp;
        _baseX = Position.X;
        _ringTimer = 1.0f; // 出场后稍等再开火
        QueueRedraw();
    }

    public override void _Draw()
    {
        Color body = _flashTimer > 0f ? Colors.White : new Color(0.5f, 0.2f, 0.7f);
        DrawCircle(Vector2.Zero, RadiusPx, body);
        DrawArc(Vector2.Zero, RadiusPx + 4f, 0f, Mathf.Tau, 48, Colors.White, 2f);
        // 头顶血条
        float ratio = Mathf.Clamp((float)_hp / Mathf.Max(MaxHp, 1), 0f, 1f);
        var barPos = new Vector2(-HpBarWidth / 2f, -RadiusPx - 22f);
        DrawRect(new Rect2(barPos, new Vector2(HpBarWidth, HpBarHeight)), new Color(0, 0, 0, 0.7f));
        DrawRect(new Rect2(barPos, new Vector2(HpBarWidth * ratio, HpBarHeight)), Colors.Red);
    }

    public void SetTarget(Node2D? target) => _target = target;

    /// 被玩家弹命中（由玩家弹脚本调用）。归零则胜利结算。
    public void TakeDamage(int damage)
    {
        _hp -= damage;
        _flashTimer = HitFlashDuration;
        QueueRedraw();
        if (_hp <= 0)
        {
            EventBus.I.Dispatch("boss_killed", this);
            QueueFree();
        }
    }

    private bool HalfPhase => _hp <= MaxHp / 2;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            QueueRedraw();
        }

        // 缓慢左右漂移（MoveSpeed 越小越稳，当前仅作出场节奏占位）
        _driftT += dt;
        Position = new Vector2(_baseX + Mathf.Sin(_driftT * 0.6f) * 120f, Position.Y);

        if (_target == null || !IsInsideTree()) return;

        // 阶段一：圆环弹幕
        _ringTimer -= dt;
        if (_ringTimer <= 0f)
        {
            _ringTimer = RingInterval;
            _spinDeg = (_spinDeg + 12f) % 360f;
            _ringIndex++;
            _emitter.Emit(new PatternSpec
            {
                Kind = BulletPatternKind.Ring,
                From = Position,
                Count = RingCount,
                Speed = BulletSpeed,
                SpinDeg = _spinDeg,
                RingIndex = _ringIndex,
            });
        }

        // 阶段二（半血后）：螺旋 + 瞄准 burst
        if (HalfPhase)
        {
            _spiralTimer -= dt;
            if (_spiralTimer <= 0f)
            {
                _spiralTimer = SpiralInterval;
                _emitter.Emit(new PatternSpec
                {
                    Kind = BulletPatternKind.Spiral,
                    From = Position,
                    Count = SpiralCount,
                    Speed = BulletSpeed * 1.1f,
                    SpinDeg = 22f,
                });
            }

            _aimedTimer -= dt;
            if (_aimedTimer <= 0f)
            {
                _aimedTimer = AimedInterval;
                _emitter.Emit(new PatternSpec
                {
                    Kind = BulletPatternKind.Aimed,
                    From = Position,
                    Target = _target.Position,
                    Speed = BulletSpeed * 1.3f,
                });
            }
        }
    }
}
