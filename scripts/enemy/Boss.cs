using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Effects;

namespace MagicThunder.Enemy;

/// <summary>
/// Boss（MVP 垂直切片）：高 HP、左右漂移、多阶段弹幕（HP>50% 圆环；HP≤50% 螺旋 + 瞄准 burst），头顶血条。
/// 数值来自 data/BossConfig.tres（数据驱动；加载失败回退默认值，不阻断运行）。
///
/// 帧率优化专项（2026-08-19）：去物理体——Node2D 不是 Area2D，无 CollisionShape2D/碰撞层，
/// 视觉 = 共享程序化贴图（白色大圆盘打底 + 紫色小圆盘内体，纯贴图可合批，替代 _Draw DrawCircle 自绘）。
/// 头顶血条保留单节点 _Draw（全屏仅 1 个 Boss，draw call 可忽略）。
/// 命中判定改由 Main 里的自写距离碰撞检测驱动（见 Main.CheckCollisions）。
/// 击杀派发 boss_killed → 胜利结算（见 Main）。
/// </summary>
public partial class Boss : Node2D
{
    private const string DefaultConfigPath = "res://data/BossConfig.tres";
    /// 命中判定半径（px），供 Main 距离碰撞检测使用。
    public const float Radius = 30f;
    private const float HpBarWidth = 70f;
    private const float HpBarHeight = 7f;
    private const float HitFlashDuration = 0.08f;
    /// 32px 程序化圆盘 → 直径 = Radius*2 的视觉尺寸。
    private const float BodyScale = Radius * 2f / 32f;
    /// 外圈白环直径（比本体大一圈，叠出"白描边紫核"）。
    private const float RingScale = (Radius + 4f) * 2f / 32f;

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
    private Sprite2D? _body;
    private float _ringTimer;
    private float _spiralTimer;
    private float _aimedTimer;
    private float _spinDeg;
    private int _ringIndex;
    private float _flashTimer;
    private float _driftT;

    public override void _Ready()
    {
        // 数据驱动：优先加载 BossConfig.tres；失败回退默认值（不阻断运行）
        // M1-PERF：CacheMode.Reuse 复用缓存——配置是静态内容，避免重复读磁盘 .tres。
        var res = ResourceLoader.Load(DefaultConfigPath, "", ResourceLoader.CacheMode.Reuse);
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

        // 视觉：纯贴图叠层——白色大圆盘（外圈）+ 紫色小圆盘（内体），可合批；替代 _Draw DrawCircle
        var ring = new Sprite2D { Texture = ProceduralTextures.Dot(), Scale = Vector2.One * RingScale, Modulate = Colors.White };
        AddChild(ring);
        _body = new Sprite2D { Texture = ProceduralTextures.Dot(), Scale = Vector2.One * BodyScale, Modulate = new Color(0.5f, 0.2f, 0.7f) };
        AddChild(_body);
        AddChild(_emitter);

        // P0-1（敌弹世界坐标）：与 Enemy 一致，注入战场弹幕层 "EnemyBullets"（挂战场原点），
        // Boss 漂移/死亡不带动弹幕；找不到则回退（弹幕挂本节点下）。
        if (GetTree().Root.FindChild("EnemyBullets", true, false) is Node2D worldLayer)
            _emitter.WorldLayer = worldLayer;

        _hp = MaxHp;
        _ringTimer = 1.0f; // 出场后稍等再开火
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 头顶血条（仅 Boss 单实例，draw call 可忽略）
        float ratio = Mathf.Clamp((float)_hp / Mathf.Max(MaxHp, 1), 0f, 1f);
        var barPos = new Vector2(-HpBarWidth / 2f, -Radius - 22f);
        DrawRect(new Rect2(barPos, new Vector2(HpBarWidth, HpBarHeight)), new Color(0, 0, 0, 0.7f));
        DrawRect(new Rect2(barPos, new Vector2(HpBarWidth * ratio, HpBarHeight)), Colors.Red);
    }

    public void SetTarget(Node2D? target) => _target = target;

    /// 被玩家弹命中（由 Main 距离碰撞检测调用）。归零则胜利结算。
    public void TakeDamage(int damage)
    {
        if (_hp <= 0) return; // 已阵亡（QueueFree 挂起中）防同帧重复结算
        _hp -= damage;
        _flashTimer = HitFlashDuration;
        if (_body != null) _body.Modulate = Colors.White; // 受击闪白
        QueueRedraw(); // 血条变化
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
            if (_flashTimer <= 0f && _body != null) _body.Modulate = new Color(0.5f, 0.2f, 0.7f); // 闪白结束恢复
        }

        // Boss 移动：入场后向屏幕中心 x 收敛，再在中心附近缓慢左右漂移。
        // 曾因在 _Ready 捕获 _baseX=Position.X（AddChild 时 Main 尚未设置 Position → 0=屏幕最左）
        // 导致 Boss 在左边来回晃；现改为以战场中心为漂移基准，Lerp 平滑到位。
        float centerX = (Autoload.GameManager.I?.PlayfieldSize.X ?? 1280f) / 2f;
        _driftT += dt;
        float targetX = centerX + Mathf.Sin(_driftT * 0.6f) * 120f;
        Position = new Vector2(Mathf.Lerp(Position.X, targetX, 0.02f), Position.Y);

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
