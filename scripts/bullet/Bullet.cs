using Godot;

namespace MagicThunder.Bullet;

/// <summary>
/// 子弹本体。由 <see cref="BulletPool"/> 统一分配/回收（弹幕瓶颈在架构，禁止随处 new/QueueFree）。
/// 命中检测用事件信号（area_entered/body_entered），不是每帧查询；层/掩码由发射器 Configure 统一设置。
/// 命中后通过 <see cref="Recycle"/> 回收到所属对象池（保住池内复用，不破坏 _active 列表）。
/// 视觉：玩家弹用星辉魔弹贴图（去黑 shader），敌弹保持占位圆（M1 灰盒）。
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

    private const string BulletStarTexture = "res://assets/bullets/bullet_star.png";
    private const string RemoveDarkShader = "res://assets/shaders/remove_dark.gdshader";
    private const float BulletSpriteScale = 0.05f; // 1024px 星弹 → ~50px 视觉

    private Sprite2D? _sprite;

    public override void _Ready()
    {
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = RadiusPx } });
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
    }

    public override void _Draw()
    {
        if (IsPlayerBullet && _sprite != null) return; // 玩家弹有贴图，不画占位圆
        DrawCircle(Vector2.Zero, RadiusPx, Colors.White);
    }

    /// 发射时由 emitter 配置（对象池复用时一定会先调一次 Reset + Configure）。
    public void Configure(Vector2 velocity, int damage = 1, bool isPlayerBullet = false)
    {
        Velocity = velocity;
        Damage = damage;
        IsPlayerBullet = isPlayerBullet;
        // 碰撞层（协议见 CollisionLayers）：玩家弹只打敌人；敌弹只打玩家本体。
        CollisionLayer = isPlayerBullet ? CollisionLayers.PlayerBullet : CollisionLayers.EnemyBullet;
        CollisionMask = isPlayerBullet ? CollisionLayers.Enemy : CollisionLayers.Player;
        ApplyVisual();
        QueueRedraw();
    }

    private void ApplyVisual()
    {
        if (IsPlayerBullet)
        {
            if (_sprite == null)
            {
                // 默认资源缓存 + 共享材质：所有玩家弹共用一份纹理/材质（不再每发 new + CacheMode.Ignore 重复加载）。
                var tex = ResourceLoader.Load<Texture2D>(BulletStarTexture);
                if (tex != null)
                {
                    _sprite = new Sprite2D { Texture = tex, Scale = Vector2.One * BulletSpriteScale };
                    _sprite.Material = SharedStarMaterial();
                    AddChild(_sprite);
                }
            }
            if (_sprite != null) _sprite.Visible = true;
        }
        else if (_sprite != null)
        {
            _sprite.Visible = false; // 敌弹保持占位圆
        }
    }

    /// 星辉魔弹去黑底材质（静态共享）：全局一份，降低材质实例数量与 GPU 状态切换。
    private static ShaderMaterial? _starMat;
    private static ShaderMaterial? SharedStarMaterial()
    {
        if (_starMat == null)
        {
            var shader = ResourceLoader.Load<Shader>(RemoveDarkShader);
            if (shader != null) _starMat = new ShaderMaterial { Shader = shader };
        }
        return _starMat;
    }

    /// 回收复用前复位到初始态（避免残留旧帧速度/伤害/归属/贴图）。
    public void Reset()
    {
        Velocity = Vector2.Zero;
        Damage = 1;
        IsPlayerBullet = false;
        Position = Vector2.Zero;
        if (_sprite != null) _sprite.Visible = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += Velocity * (float)delta;

        // BUG-008 兜底：原池已释放（Recycle 目标无效）的孤儿弹 → 出屏自毁，
        // 防止幽灵弹无限飞行（池的 _Process 已随池销毁，无人回收它们）。
        if (Recycle != null && !GodotObject.IsInstanceValid(Recycle.Target as GodotObject))
        {
            var size = Autoload.GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);
            const float margin = 60f;
            var p = Position;
            if (p.X < -margin || p.X > size.X + margin || p.Y < -margin || p.Y > size.Y + margin)
                QueueFree();
        }
    }

    private void OnAreaEntered(Area2D other)
    {
        if (!IsPlayerBullet) return; // 敌弹不打敌人
        switch (other)
        {
            case Enemy.Enemy e:
                e.TakeDamage(Damage);
                RecycleSelf();
                break;
            case Enemy.Boss b:
                b.TakeDamage(Damage);
                RecycleSelf();
                break;
        }
    }

    private void OnBodyEntered(Node2D body)
    {
        if (IsPlayerBullet) return; // 玩家弹不打玩家
        if (body is Player.PlayerController pc)
        {
            if (!pc.IsVulnerable()) return; // 无敌帧内敌弹穿过
            Autoload.EventBus.I.Dispatch("player_hit");
            RecycleSelf();
        }
    }

    /// 命中回收（探针可调用）：回收前校验池仍有效（BUG-008——Enemy/Boss 阵亡后池已释放，
    /// 在飞孤儿弹的 Recycle 指向无效实例；无效则自毁，避免「访问已释放实例」异常）。
    public void RecycleSelf()
    {
        if (Recycle != null && GodotObject.IsInstanceValid(Recycle.Target as GodotObject))
            Recycle(this);
        else
            QueueFree();
    }
}
