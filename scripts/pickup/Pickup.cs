using Godot;
using MagicThunder.Autoload;
using MagicThunder.Effects;

namespace MagicThunder.Pickup;

/// <summary>
/// 星之残片拾取物（M2 割草爽感）：敌人死亡 / 定期掉落生成。
/// 行为：缓落 → 玩家进入 <see cref="MagnetRadius"/> 被磁吸 → 进入 <see cref="AbsorbRadius"/>
/// 派发 pickup_absorbed（payload=自身）并消失（Main 收到后加经验）。
/// 视觉：共享程序化圆盘贴图 + 金色染色（可合批）。
/// </summary>
public partial class Pickup : Node2D
{
    /// 吸收判定半径（px）：进入即被玩家吸收。
    public const float AbsorbRadius = 18f;
    /// 磁吸基础半径（px）。玩家开局自带此磁吸（D-008 移动端基调/测试期爽感），引力升级在此基础上放大。
    public const float PickupMagnetBase = 200f;
    private const float FallSpeed = 45f;       // 未磁吸时缓落速度（px/s）
    private const float MagnetAccel = 1400f;   // 磁吸加速度（px/s²）
    private const float MagnetMaxSpeed = 620f; // 磁吸最大速度（防瞬移）
    private const float SpriteScale = 0.4f;    // 32px 圆盘 → ~13px 视觉

    /// 本片提供经验（星之残片数值）。
    public int ExpValue { get; set; } = 1;
    /// 磁吸半径（px）；引力升级可放大。
    public float MagnetRadius { get; set; } = PickupMagnetBase;

    private Node2D? _target;
    private Vector2 _velocity;
    private Sprite2D? _body;

    public override void _Ready()
    {
        _body = new Sprite2D
        {
            Texture = ProceduralTextures.Dot(),
            Scale = Vector2.One * SpriteScale,
            Modulate = new Color(1f, 0.85f, 0.35f), // 星之残片：金色
        };
        AddChild(_body);
    }

    public void SetTarget(Node2D? target) => _target = target;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 出屏（掉出战场底部）自毁，避免无限下落堆积。
        var size = GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);
        if (Position.Y > size.Y + 40f) { QueueFree(); return; }

        if (_target == null)
        {
            Position += new Vector2(0, FallSpeed) * dt;
            return;
        }

        var toPlayer = _target.Position - Position;
        float dist = toPlayer.Length();

        // 吸收：进入判定半径 → 通知 Main 加经验并消失
        if (dist <= AbsorbRadius)
        {
            EventBus.I.Dispatch("pickup_absorbed", this);
            QueueFree();
            return;
        }

        // 磁吸：半径内加速朝玩家靠拢；半径外重置速度、继续缓落
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
