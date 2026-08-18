using System.Collections.Generic;
using Godot;
using MagicThunder.Autoload;
using MagicThunder.Effects;

namespace MagicThunder.Pickup;

/// <summary>
/// 局内道具类型（Powerup）：敌人击杀小概率掉落，吸收获得即时增益。
/// 定位（测试期「补割草爽感」）：给玩家稳定的护盾 / 回血 / 火力手段，降低挫败。
/// </summary>
public enum PowerupKind
{
    /// 护盾 +1（抵挡一次伤害，视觉=玩家护盾环）。
    Shield,
    /// 回 1 血（不超过生命上限）。
    Life,
    /// 火力：武器等级 +1（满级后转射速提升）。
    Fire,
}

/// <summary>
/// 局内道具（Powerup）：与星之残片同行为（缓落 → 磁吸 → 进入判定半径派发 powerup_taken 并消失）。
/// 视觉：真实美术图标（assets/items/*.png，黑底 + 去黑 shader，与 kill_burst 同管线）；
/// 素材缺失时回退程序化圆盘 + 类型色（不白屏）。
/// </summary>
public partial class Powerup : Node2D
{
    /// 吸收判定半径（px）：与星之残片一致。
    public const float AbsorbRadius = Pickup.AbsorbRadius;
    private const float FallSpeed = 45f;       // 未磁吸时缓落速度（px/s）
    private const float MagnetAccel = 1400f;   // 磁吸加速度（px/s²）
    private const float MagnetMaxSpeed = 620f; // 磁吸最大速度（防瞬移）
    private const float RealSpriteScale = 0.05f; // 1024px 图标 → ~50px 视觉（比星之残片略大可读）
    private const float FallbackSpriteScale = 0.5f; // 32px 回退圆盘 → ~16px 视觉

    /// 道具类型（Spawn 时由 Main 预置）。
    public PowerupKind Kind { get; set; }
    /// 磁吸半径（px）：随玩家当前磁吸半径（Spawn 时赋值）。
    public float MagnetRadius { get; set; } = Pickup.PickupMagnetBase;

    private Node2D? _target;
    private Vector2 _velocity;
    private Sprite2D? _body;

    /// 类型 →（美术路径, 回退染色）。黑底图标 + 去黑 shader 显示。
    private static readonly Dictionary<PowerupKind, (string path, Color fallback)> Visuals = new()
    {
        [PowerupKind.Shield] = ("res://assets/items/powerup_shield.png", new Color(0.4f, 0.65f, 1f)),
        [PowerupKind.Life] = ("res://assets/items/powerup_heart.png", new Color(1f, 0.4f, 0.45f)),
        [PowerupKind.Fire] = ("res://assets/items/powerup_fire.png", new Color(1f, 0.55f, 0.2f)),
    };

    public override void _Ready()
    {
        _body = new Sprite2D { Scale = Vector2.One * FallbackSpriteScale };
        var tex = ResourceLoader.Load<Texture2D>(Visuals[Kind].path);
        if (tex != null)
        {
            // 真实美术图标：1024px 黑底 PNG → 缩小到 ~50px（对齐 Bullet 星弹的 0.05 缩放约定）
            _body.Scale = Vector2.One * RealSpriteScale;
            _body.Texture = tex;
            var shader = ResourceLoader.Load<Shader>("res://assets/shaders/remove_dark.gdshader");
            if (shader != null) _body.Material = new ShaderMaterial { Shader = shader };
        }
        else
        {
            // 回退：程序化圆盘 + 类型色（素材缺失不白屏）
            _body.Texture = ProceduralTextures.Dot();
            _body.Modulate = Visuals[Kind].fallback;
        }
        AddChild(_body);
    }

    public void SetTarget(Node2D? target) => _target = target;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 出屏自毁（同星之残片）
        var size = GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);
        if (Position.Y > size.Y + 40f) { QueueFree(); return; }

        if (_target == null)
        {
            Position += new Vector2(0, FallSpeed) * dt;
            return;
        }

        var toPlayer = _target.Position - Position;
        float dist = toPlayer.Length();

        // 吸收：进入判定半径 → 通知 Main 应用增益并消失
        if (dist <= AbsorbRadius)
        {
            EventBus.I.Dispatch("powerup_taken", this);
            QueueFree();
            return;
        }

        // 磁吸（同星之残片）
        if (dist <= MagnetRadius)
        {
            _velocity += toPlayer.Normalized() * MagnetAccel * dt;
            if (_velocity.Length() > MagnetMaxSpeed) _velocity = _velocity.Normalized() * MagnetMaxSpeed;
            Position += _velocity * dt;
        }
        else
        {
            _velocity = Vector2.Zero;
            Position += new Vector2(0, FallSpeed) * dt;
        }
    }
}
