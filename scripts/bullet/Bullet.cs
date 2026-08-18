using Godot;

namespace MagicThunder.Bullet;

/// <summary>
/// 子弹本体（帧率优化专项：去物理实体、只做贴图）。
/// 由 <see cref="BulletPool"/> 统一分配/回收（弹幕瓶颈在架构，禁止随处 new/QueueFree）。
/// 不是物理体（Node2D）——碰撞改为 Main 里的距离检测（自写碰撞），避免每发子弹一个 Area2D 物理体拖垮 PhysicsServer。
/// 视觉：玩家弹用星辉魔弹贴图（去黑 shader）；敌弹用共享程序化圆盘贴图（可合批，替代 DrawCircle 自绘）。
/// 命中后通过 <see cref="Recycle"/> 回收到所属对象池（保住池内复用，不破坏 _active 列表）。
/// </summary>
public partial class Bullet : Node2D
{
    /// 命中判定半径（px），供 Main 距离碰撞检测使用。
    public const float Radius = 6f;

    public Vector2 Velocity { get; set; }
    public int Damage { get; set; } = 1;

    /// 是否玩家弹（决定碰撞方向与视觉）。由发射器在 Spawn 后 Configure 设置。
    public bool IsPlayerBullet { get; set; }

    /// 命中后回收到所属对象池（由发射器挂钩）。
    public System.Action<Bullet>? Recycle;

    private const string BulletStarTexture = "res://assets/bullets/bullet_star.png";
    private const string RemoveDarkShader = "res://assets/shaders/remove_dark.gdshader";
    private const float BulletSpriteScale = 0.05f; // 1024px 星弹 → ~50px 视觉
    private const float EnemyDotScale = 0.42f; // 32px 圆盘 → ~13px 敌弹视觉

    private Sprite2D? _sprite;

    /// 发射时由 emitter 配置（对象池复用时一定会先调一次 Reset + Configure）。
    public void Configure(Vector2 velocity, int damage = 1, bool isPlayerBullet = false)
    {
        Velocity = velocity;
        Damage = damage;
        IsPlayerBullet = isPlayerBullet;
        ApplyVisual();
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
            if (_sprite != null)
            {
                _sprite.Visible = true;
                _sprite.Material = SharedStarMaterial(); // 切回星弹材质
                _sprite.Modulate = Colors.White;
            }
        }
        else
        {
            // 敌弹：共享程序化圆盘贴图（可合批），去黑 shader 对纯色贴图无效，去掉材质。
            // 注意：池里首次以敌弹身份出生的弹 _sprite 可能为 null，必须在这里建贴图，
            //       否则整颗弹无视觉（"看不见敌机子弹"回归的根因）。
            if (_sprite == null)
            {
                _sprite = new Sprite2D
                {
                    Texture = Effects.ProceduralTextures.Dot(),
                    Scale = Vector2.One * EnemyDotScale,
                };
                AddChild(_sprite);
            }
            else
            {
                _sprite.Texture = Effects.ProceduralTextures.Dot();
                _sprite.Scale = Vector2.One * EnemyDotScale;
            }
            _sprite.Material = null;
            _sprite.Modulate = Colors.White;
            _sprite.Visible = true;
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

    /// 回收自身（碰撞/出屏后由调用方触发）。无物理事件，命中由 Main 距离检测驱动。
    public void RecycleSelf() => Recycle?.Invoke(this);
}
