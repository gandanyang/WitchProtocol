using Godot;

namespace MagicThunder.Bullet;

/// <summary>
/// 子弹本体。由 <see cref="BulletPool"/> 统一分配/回收（弹幕瓶颈在架构，禁止随处 new/QueueFree）。
/// 命中检测用事件信号（area_entered/body_entered），不是每帧查询；层/掩码由发射器 Configure 统一设置。
/// 命中后通过 <see cref="Recycle"/> 回收到所属对象池（保住池内复用，不破坏 _active 列表）。
/// </summary>
public partial class Bullet : Area2D
{
    public Vector2 Velocity { get; set; }
    public int Damage { get; set; } = 1;

    /// 是否玩家弹（决定碰撞层与命中目标）。由发射器在 Spawn 后 Configure 设置。
    public bool IsPlayerBullet { get; set; }

    /// 命中后回收到所属对象池（由发射器挂钩）。
    public System.Action<Bullet>? Recycle;

    private const float RadiusPx = 6f;
    private const uint LayerPlayer = 1;
    private const uint LayerEnemy = 2;
    private const uint LayerPlayerBullet = 3;
    private const uint LayerEnemyBullet = 4;

    public override void _Ready()
    {
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = RadiusPx } });
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, RadiusPx, Colors.White);
    }

    /// 发射时由 emitter 配置（对象池复用时一定会先调一次 Reset + Configure）。
    public void Configure(Vector2 velocity, int damage = 1, bool isPlayerBullet = false)
    {
        Velocity = velocity;
        Damage = damage;
        IsPlayerBullet = isPlayerBullet;
        // 碰撞层：玩家弹只打敌人(层2)；敌弹只打玩家 body(层1)。
        // 在 Configure 统一设置（_Ready 时 IsPlayerBullet 尚未就位）。
        CollisionLayer = isPlayerBullet ? LayerPlayerBullet : LayerEnemyBullet;
        CollisionMask = isPlayerBullet ? LayerEnemy : LayerPlayer;
    }

    /// 回收复用前复位到初始态（避免残留旧帧速度/伤害/归属）。
    public void Reset()
    {
        Velocity = Vector2.Zero;
        Damage = 1;
        IsPlayerBullet = false;
        Position = Vector2.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += Velocity * (float)delta;
    }

    private void OnAreaEntered(Area2D other)
    {
        if (!IsPlayerBullet) return; // 敌弹不打敌人
        if (other is Enemy.Enemy e)
        {
            e.TakeDamage(Damage);
            RecycleSelf();
        }
    }

    private void OnBodyEntered(Node2D body)
    {
        if (IsPlayerBullet) return; // 玩家弹不打玩家
        if (body is Player.PlayerController)
        {
            Autoload.EventBus.I.Dispatch("player_hit");
            RecycleSelf();
        }
    }

    private void RecycleSelf() => Recycle?.Invoke(this);
}
