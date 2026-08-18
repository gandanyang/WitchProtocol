using Godot;
using MagicThunder.Bullet;

namespace MagicThunder.Player;

/// <summary>
/// 玩家操控体（魔女「黎歌」）。M1 手感垂直切片：
///  8 方向移动（WASD/方向键）+ Shift 低速 + 自动射击（星辉魔弹）。
///
/// P0-2（自机判定）：实际 Hitbox = 3px（HitboxRadius），视觉尺寸 ≠ Hitbox——
/// 弹幕游戏铁律"子弹贴着裙角过去但活下来"。低速聚焦时判定点放大高亮显示。
/// 无敌帧：受击后 1.5s 无敌 + 闪烁（敌弹穿过，不重复扣血）。
/// 边界：限制在 PlayfieldSize 内（留边距）。
/// 视觉：优先 AI 黑底战斗 sprite + 去黑 shader；加载失败回退占位图。
///
/// MVP 垂直切片（本关结束统一结算升级）：
///  武器等级 WeaponLevel（1 单发 / 2 双发 / 3 三向），由 <see cref="ShotSpecs"/> 纯函数产出弹幕规格；
///  护盾 Shield 抵挡一次伤害；升级经 <see cref="ApplyUpgrade"/> 落地（跨关保留，见 Main）。
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    private const string DefaultConfigPath = "res://data/PlayerConfig.tres";
    private const string BattleSpritePath = "res://assets/characters/rika/battlesprite/rika_battlesprite.png";
    private const string RemoveDarkShader = "res://assets/shaders/remove_dark.gdshader";

    /// 武器等级上限（单发→双发→三向），满级后结算不再出现武器强化。
    public const int MaxWeaponLevel = 3;
    /// 三向扇面两翼偏角（度）。
    private const float TripleFanDeg = 12f;
    /// 双发/三向两发间偏角（度）。
    private const float DoubleFanDeg = 8f;
    /// Aimed 规格的目标距离（确定方向用，长度无关紧要）。
    private const float UpDistance = 100f;

    public float MoveSpeed = 320f;
    public float FocusSpeedRatio = 0.5f;
    /// 射击间隔（秒）。来自 PlayerConfig.tres；RapidFire 升级按比例缩短。
    public float FireInterval = 0.15f;
    /// 弹速（px/s）。来自 PlayerConfig.tres；BulletSpeed 升级按比例提升。
    public float BulletSpeed = 520f;

    /// P0-2：实际判定半径 3px（视觉尺寸 ≠ Hitbox）。
    public const float HitboxRadius = 3f;

    private const float InvincibleTime = 1.5f;
    private const float BoundaryMargin = 24f;
    private const float BattleSpriteScale = 0.08f; // 1024px 全身图 → ~82px 自机

    private BulletEmitter? _emitter;
    private float _fireTimer;
    private float _invincibleTimer;
    private bool _blinkOn;
    private bool _hasSprite;

    /// 武器等级：1 单发 / 2 双发 / 3 三向（本关结束结算升级，跨关保留）。
    public int WeaponLevel { get; private set; } = 1;
    /// 生命上限（来自 PlayerConfig.tres；MaxHp 升级 +1）。
    public int MaxHp { get; private set; } = 3;
    /// 当前生命（本关内减少；开新关 RestoreForStage 回满）。
    public int Hp { get; private set; } = 3;
    /// 护盾层数：抵挡一次伤害，不扣生命（结算升级获得，跨关保留）。
    public int Shield { get; private set; }
    public bool IsDead => Hp <= 0;

    public override void _Ready()
    {
        // 数据驱动：优先加载 PlayerConfig.tres；失败回退默认值（不阻断运行）
        var res = ResourceLoader.Load(DefaultConfigPath, "", ResourceLoader.CacheMode.Ignore);
        if (res is Data.PlayerConfig cfg)
        {
            MoveSpeed = cfg.MoveSpeed;
            FocusSpeedRatio = cfg.FocusSpeedRatio;
            MaxHp = cfg.MaxHp;
            FireInterval = cfg.FireInterval;
            BulletSpeed = cfg.BulletSpeed;
        }
        Hp = MaxHp;

        // 碰撞体：供敌弹 body_entered 命中检测（敌弹 mask=层1 → 命中本体会触发）
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = HitboxRadius } });

        TryAttachBattleSprite();
        QueueRedraw();
    }

    private void TryAttachBattleSprite()
    {
        var tex = ResourceLoader.Load<Texture2D>(BattleSpritePath, "", ResourceLoader.CacheMode.Ignore);
        var shader = ResourceLoader.Load<Shader>(RemoveDarkShader, "", ResourceLoader.CacheMode.Ignore);
        if (tex == null || shader == null) return;
        var sprite = new Sprite2D { Texture = tex, Scale = Vector2.One * BattleSpriteScale };
        sprite.Material = new ShaderMaterial { Shader = shader };
        AddChild(sprite);
        _hasSprite = true;
    }

    public override void _Draw()
    {
        // 占位外观（sprite 加载失败时）
        if (!_hasSprite) DrawRect(new Rect2(-8, -12, 16, 24), Colors.CornflowerBlue);
        DrawHitboxMarker();
    }

    /// 判定点标记：低速聚焦时放大高亮，平时淡色小点（P0-2 可读性）。
    private void DrawHitboxMarker()
    {
        bool focus = Input.IsActionPressed("focus");
        float r = focus ? 4f : 2.5f;
        Color c = focus ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.65f);
        DrawCircle(Vector2.Zero, r, c);
    }

    /// 绑定战场里的玩家弹发射器（发射器挂在世界层 PlayerBullets，子弹不随玩家移动）。
    public void BindEmitter(BulletEmitter emitter) => _emitter = emitter;

    /// 是否可被击中（无敌帧内 false，供敌弹命中判定使用）。
    public bool IsVulnerable() => _invincibleTimer <= 0f;

    /// 受击：无敌帧内忽略（防同帧多发子弹重复扣血）；有盾先扣盾，无盾扣生命。
    /// 返回是否真正消耗了一次受击（供 Main 决定是否刷新 HUD / 判定死亡）。
    public bool OnHit()
    {
        if (_invincibleTimer > 0f) return false;
        if (Shield > 0) Shield--;
        else Hp--;
        _invincibleTimer = InvincibleTime;
        return true;
    }

    /// 开新一关（胜利升级后 / 失败重开）：回满血、重置受击与射击状态。
    /// 保留跨关强化（武器等级 / 护盾 / 射速 / 弹速 / 生命上限）。
    public void RestoreForStage()
    {
        Hp = MaxHp;
        _invincibleTimer = 0f;
        _blinkOn = false;
        Modulate = Colors.White;
        _fireTimer = 0f;
    }

    /// 结算升级落地（跨关保留，本关内立即生效；由 Main.OnPickUpgrade 调用）。
    public void ApplyUpgrade(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.Weapon:
                WeaponLevel = Mathf.Min(WeaponLevel + 1, MaxWeaponLevel);
                break;
            case UpgradeType.RapidFire:
                FireInterval *= 0.75f;
                break;
            case UpgradeType.BulletSpeed:
                BulletSpeed *= 1.2f;
                break;
            case UpgradeType.MaxHp:
                MaxHp++;
                Hp++;
                break;
            case UpgradeType.Shield:
                Shield++;
                break;
        }
    }

    /// 武器形态纯函数：按等级返回 1/2/3 条发射规格（探针直接验证，不依赖场景）。
    /// Lv1 单发直上；Lv2 双发（±DoubleFanDeg）；Lv3 三向（±TripleFanDeg + 正中）。
    public static PatternSpec[] ShotSpecs(Vector2 from, int weaponLevel, float speed)
    {
        if (weaponLevel >= 3)
        {
            return new[]
            {
                new PatternSpec { Kind = BulletPatternKind.Aimed, From = from, Target = from + UpAt(-TripleFanDeg) * UpDistance, Speed = speed, IsPlayerBullet = true },
                new PatternSpec { Kind = BulletPatternKind.Aimed, From = from, Target = from + UpAt(0f) * UpDistance, Speed = speed, IsPlayerBullet = true },
                new PatternSpec { Kind = BulletPatternKind.Aimed, From = from, Target = from + UpAt(TripleFanDeg) * UpDistance, Speed = speed, IsPlayerBullet = true },
            };
        }

        if (weaponLevel == 2)
        {
            return new[]
            {
                new PatternSpec { Kind = BulletPatternKind.Aimed, From = from, Target = from + UpAt(-DoubleFanDeg) * UpDistance, Speed = speed, IsPlayerBullet = true },
                new PatternSpec { Kind = BulletPatternKind.Aimed, From = from, Target = from + UpAt(DoubleFanDeg) * UpDistance, Speed = speed, IsPlayerBullet = true },
            };
        }

        return new[]
        {
            new PatternSpec { Kind = BulletPatternKind.Aimed, From = from, Target = from + UpAt(0f) * UpDistance, Speed = speed, IsPlayerBullet = true },
        };
    }

    /// 朝上方向绕 Z 旋转偏角（度）的方向向量。
    private static Vector2 UpAt(float deg) => Vector2.Up.Rotated(Mathf.DegToRad(deg));

    /// 自动射击逻辑（独立成方法便于探针直接验证）：按当前武器等级打出全部弹道。
    public void TryFire()
    {
        if (_fireTimer <= 0f && _emitter != null)
        {
            _fireTimer = FireInterval;
            foreach (var spec in ShotSpecs(GlobalPosition, WeaponLevel, BulletSpeed))
                _emitter.Emit(spec);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // 移动（8 方向）
        var input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        var ratio = Input.IsActionPressed("focus") ? FocusSpeedRatio : 1f;
        Velocity = input * MoveSpeed * ratio;
        MoveAndSlide();

        // 边界限制（PlayfieldSize 内留边距）
        var size = Autoload.GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);
        Position = new Vector2(
            Mathf.Clamp(Position.X, BoundaryMargin, size.X - BoundaryMargin),
            Mathf.Clamp(Position.Y, BoundaryMargin, size.Y - BoundaryMargin));

        // 自动射击（星辉魔弹，无需按键）
        _fireTimer -= (float)delta;
        TryFire();
    }

    public override void _Process(double delta)
    {
        // 无敌帧：计时 + 闪烁
        if (_invincibleTimer > 0f)
        {
            _invincibleTimer -= (float)delta;
            _blinkOn = !_blinkOn;
            Modulate = _blinkOn ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 1f);
            if (_invincibleTimer <= 0f) Modulate = Colors.White;
        }
    }
}

/// <summary>
/// 结算升级选项（本关结束统一结算，见 Main）。置于命名空间顶层，
/// 让 Main / Settlement 通过 `using MagicThunder.Player;` 直接引用。
/// </summary>
public enum UpgradeType { Weapon, RapidFire, BulletSpeed, MaxHp, Shield }
