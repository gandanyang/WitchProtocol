using Godot;
using MagicThunder.Bullet;

namespace MagicThunder.Player;

/// <summary>
/// 玩家操控体（魔女「黎歌」）。MVP：移动 + Focus 低速 + Z 连发开火。
/// 键位见 project.godot [input]：WASD/方向移动，Shift 低速，Z 射击。
/// 玩家弹发射器由 Main 绑定（挂在战场原点，避免子弹随玩家移动）。
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    private const string DefaultConfigPath = "res://data/PlayerConfig.tres";

    public float MoveSpeed = 320f;
    public float FocusSpeedRatio = 0.5f;

    private BulletEmitter? _emitter;
    private float _fireTimer;

    private const float FireInterval = 0.15f;
    private const float BulletSpeed = 520f;
    private const float RadiusPx = 10f;

    public override void _Ready()
    {
        // 数据驱动：优先加载 .tres 配置；加载失败回退默认值（不阻断运行）
        var res = ResourceLoader.Load(DefaultConfigPath, "", ResourceLoader.CacheMode.Ignore);
        if (res is Data.PlayerConfig cfg)
        {
            MoveSpeed = cfg.MoveSpeed;
            FocusSpeedRatio = cfg.FocusSpeedRatio;
        }
        // 碰撞体：供敌弹 body_entered 命中检测（敌弹 mask=层1 → 命中本体会触发）
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = RadiusPx } });
        // 占位立绘：一个矩形 + 中心描点（正式美术接入后移除 _Draw）
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(-8, -12, 16, 24), Colors.CornflowerBlue);
        DrawCircle(Vector2.Zero, 2f, Colors.White);
    }

    /// 绑定战场里的玩家弹发射器（发射器挂在战场原点，避免子弹随玩家移动）。
    public void BindEmitter(BulletEmitter emitter) => _emitter = emitter;

    public override void _PhysicsProcess(double delta)
    {
        // 移动
        var input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        var ratio = Input.IsActionPressed("focus") ? FocusSpeedRatio : 1f;
        Velocity = input * MoveSpeed * ratio;
        MoveAndSlide();

        // 开火（Z 按住连发）
        _fireTimer -= (float)delta;
        if (Input.IsActionPressed("shoot") && _fireTimer <= 0f && _emitter != null)
        {
            _fireTimer = FireInterval;
            _emitter.Emit(new PatternSpec
            {
                Kind = BulletPatternKind.Aimed,
                From = Position,
                Target = Position + new Vector2(0, -100), // 朝上
                Speed = BulletSpeed,
                IsPlayerBullet = true,
            });
        }
    }
}
