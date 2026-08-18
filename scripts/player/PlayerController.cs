using Godot;
using MagicThunder.Audio;
using MagicThunder.Bullet;
using MagicThunder.Effects;

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
/// 帧率优化专项（2026-08-19）：去物理体——Node2D 不是 CharacterBody2D，无 CollisionShape2D/碰撞层，
/// 移动改为自写速度积分（Position += velocity*dt），不再 MoveAndSlide（省下 PhysicsServer 每帧步进）。
/// 被敌弹命中由 Main 里的自写距离碰撞检测驱动（见 Main.CheckCollisions）。
///
/// MVP 垂直切片（本关结束统一结算升级）：
///  武器等级 WeaponLevel（1 单发 / 2 双发 / 3 三向），由 <see cref="ShotSpecs"/> 纯函数产出弹幕规格；
///  护盾 Shield 抵挡一次伤害；升级经 <see cref="ApplyUpgrade"/> 落地（跨关保留，见 Main）。
/// </summary>
public partial class PlayerController : Node2D
{
    private const string DefaultConfigPath = "res://data/PlayerConfig.tres";
    // 离线预处理精灵图（tools/prep_sprite.py 产出）：白底大图 → 256px 透明背景高清小图。
    // 运行时直接显示，免 remove_white shader、免 1024px 大纹理每帧采样（性能优化，用户反馈"太卡"）。
    private const string BattleSpritePath = "res://assets/characters/rika/battlesprite/rika_battlesprite_ready.png";

    /// 武器等级上限（单发→双发→三向），满级后结算不再出现武器强化。
    public const int MaxWeaponLevel = 3;
    /// 三向扇面两翼偏角（度）。
    private const float TripleFanDeg = 12f;
    /// 双发/三向两发间偏角（度）。
    private const float DoubleFanDeg = 8f;
    /// Aimed 规格的目标距离（确定方向用，长度无关紧要）。
    private const float UpDistance = 100f;
    // M2 局内成长：经验曲线。Lv→Lv+1 所需经验 = 基础 + 等级步进（数值膨胀小，弹幕形态才是爽点）。
    private const int ExpBase = 5;
    private const int ExpPerLevel = 4;
    /// 引力升级：每次扩大磁吸半径（px）。
    public const float MagnetUpgradeBonus = 50f;

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
    // 256px 预处理精灵 → 显示 ~82px 自机（2x+ 高清密度）
    private const float BattleSpriteScale = 0.32f;

    // ---- 手感（M1 调优）：加速/减速，快起快停，消除"瞬移"僵硬感 ----
    /// 移动加速度（px/s²）：输入方向时向目标速度靠拢的速率。
    private const float MoveAccel = 2600f;
    /// 移动减速度（px/s²）：松开输入时的停止速率（略大于加速，停止更干脆）。
    private const float MoveDecel = 3200f;
    /// 无敌帧闪烁周期（秒）：0.1s 一明一暗，比逐帧切换更清晰可读。
    private const float BlinkPeriod = 0.1f;
    /// 受击瞬间白闪时长（秒）：受击第一帧闪白，强化打击反馈。
    private const float HitFlashTime = 0.08f;
    /// 护盾环视觉半径（px）：Shield>0 时绘制。
    private const float ShieldRingRadius = 15f;

    private BulletEmitter? _emitter;
    private float _fireTimer;
    private float _invincibleTimer;
    private float _blinkTimer;
    private bool _blinkOn;
    private bool _hasSprite;
    private float _hitFlashTimer;
    // 帧率优化专项：自写速度（替代 CharacterBody2D.Velocity/MoveAndSlide）
    private Vector2 _velocity;

    // ---- 战败坠落状态（供 DeathSequence 驱动；死亡演出 = 灵魂，见 M4-1）----
    private Vector2 _fallVelocity;

    /// 武器等级：1 单发 / 2 双发 / 3 三向（本关结束结算升级，跨关保留）。
    public int WeaponLevel { get; private set; } = 1;
    /// 生命上限（来自 PlayerConfig.tres；MaxHp 升级 +1）。
    public int MaxHp { get; private set; } = 3;
    /// 当前生命（本关内减少；开新关 RestoreForStage 回满）。
    public int Hp { get; private set; } = 3;
    /// 护盾层数：抵挡一次伤害，不扣生命（结算升级获得，跨关保留）。
    public int Shield { get; private set; }
    public bool IsDead => Hp <= 0;
    /// 是否处于战败坠落状态（死亡演出中，不响应输入/射击）。
    public bool IsDying { get; private set; }

    // ---- M2 局内成长：经验 / 等级 / 穿透 / 引力 ----
    /// 当前等级（1 起）。局内吃星之残片升级。
    public int Level { get; private set; } = 1;
    /// 当前经验（满 ExpToNext 升级）。
    public int Exp { get; private set; }
    /// 升到下一级所需经验（随等级递增）。
    public int ExpToNext { get; private set; } = ExpBase;
    /// 穿透：玩家弹命中不消失（穿怪），割草爽感核心升级。
    public bool Pierce { get; private set; }
    /// 磁吸半径（px）：星之残片吸收范围，引力升级放大。
    public float MagnetRadius { get; private set; } = Pickup.Pickup.PickupMagnetBase;

    // 基础值快照（_Ready 读配置后记录，ResetProgression 恢复到初始态）
    private int _baseMaxHp;
    private float _baseFireInterval;
    private float _baseBulletSpeed;

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
        _baseMaxHp = MaxHp;      // 记录配置基础值，ResetProgression 恢复用
        _baseFireInterval = FireInterval;
        _baseBulletSpeed = BulletSpeed;

        // 帧率优化专项：去物理体——不再挂 CollisionShape2D/碰撞层，命中由 Main 距离检测驱动
        TryAttachBattleSprite();
        QueueRedraw();
    }

    private void TryAttachBattleSprite()
    {
        // 离线预处理精灵（透明 PNG）：直接加载显示，无 shader 开销（性能优化）。
        var tex = ResourceLoader.Load<Texture2D>(BattleSpritePath);
        if (tex == null) return;
        var sprite = new Sprite2D { Texture = tex, Scale = Vector2.One * BattleSpriteScale };
        AddChild(sprite);
        _hasSprite = true;
    }

    public override void _Draw()
    {
        // 占位外观（sprite 加载失败时）
        if (!_hasSprite) DrawRect(new Rect2(-8, -12, 16, 24), Colors.CornflowerBlue);
        DrawShieldRing();
        DrawHitboxMarker();
    }

    /// 护盾环：Shield>0 时画一圈蓝紫光环（视觉提示"还有一次抵挡"）。
    private void DrawShieldRing()
    {
        if (Shield <= 0) return;
        var c = new Color(0.45f, 0.6f, 1f, 0.55f);
        DrawArc(Vector2.Zero, ShieldRingRadius, 0f, Mathf.Tau, 32, c, 2f);
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
        _hitFlashTimer = HitFlashTime; // 受击瞬间白闪
        QueueRedraw(); // 护盾环可能变化

        // 受击反馈（M1 手感核心）：震屏 + HitStop 顿帧 + 音效
        FeedbackSystem.ShakeScreen(0.25f, 6f);
        FeedbackSystem.HitStop(0.06f);
        SfxPlayer.Play("player_hit");
        return true;
    }

    /// 开新一关（胜利升级后 / 失败重开）：回满血、重置受击与射击状态。
    /// 保留跨关强化（武器等级 / 护盾 / 射速 / 弹速 / 生命上限）。
    public void RestoreForStage()
    {
        Hp = MaxHp;
        _invincibleTimer = 0f;
        _blinkOn = false;
        _blinkTimer = 0f;
        _hitFlashTimer = 0f;
        IsDying = false;
        _fallVelocity = Vector2.Zero;
        _velocity = Vector2.Zero;
        Rotation = 0f;
        Modulate = Colors.White;
        _fireTimer = 0f;
    }

    /// 结算升级落地（局内即时升级/重开前调用，立即生效；由 Main 调用）。
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
            case UpgradeType.Pierce:
                Pierce = true;
                break;
            case UpgradeType.Magnet:
                MagnetRadius += MagnetUpgradeBonus;
                break;
        }
    }

    /// 局内道具吸收落地（Powerup 吸收 → 即时增益）。
    /// Shield 护盾 +1；Life 回 1 血（不超上限）；Fire 武器 +1（满级转射速 +15%）。
    public void ApplyPowerup(Pickup.PowerupKind kind)
    {
        switch (kind)
        {
            case Pickup.PowerupKind.Shield:
                Shield++;
                break;
            case Pickup.PowerupKind.Life:
                Hp = Mathf.Min(Hp + 1, MaxHp);
                break;
            case Pickup.PowerupKind.Fire:
                if (WeaponLevel < MaxWeaponLevel) WeaponLevel++;
                else FireInterval *= 0.85f;
                break;
        }
        QueueRedraw(); // 护盾环可能变化
    }

    /// 局内加经验（星之残片吸收）。返回本次升级次数（0 = 未升级）。
    /// 经验曲线：Lv→Lv+1 需要 ExpBase + (Lv-1)*ExpPerLevel（纯函数，探针可验证）。
    public static int ExpToNextFor(int level) => ExpBase + (level - 1) * ExpPerLevel;

    public int AddExp(int amount)
    {
        Exp += amount;
        int ups = 0;
        while (Exp >= ExpToNext)
        {
            Exp -= ExpToNext;
            Level++;
            ExpToNext = ExpToNextFor(Level);
            ups++;
        }
        return ups;
    }

    /// 新一局开局（从主菜单进入）：把局内成长全部复位到配置基础值（经验/等级/武器/盾/穿透/引力）。
    /// 区别于 RestoreForStage（同关重开保留成长，便于打 Boss 前累积）。
    public void ResetProgression()
    {
        Level = 1;
        Exp = 0;
        ExpToNext = ExpBase;
        WeaponLevel = 1;
        Shield = 0;
        Pierce = false;
        MagnetRadius = Pickup.Pickup.PickupMagnetBase;
        MaxHp = _baseMaxHp;
        FireInterval = _baseFireInterval;
        BulletSpeed = _baseBulletSpeed;
        RestoreForStage();
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
            SfxPlayer.Play("shot", -12f); // 射击音效：音量低（0.15s 射速下不吵）
        }
    }

    /// 进入战败坠落（死亡演出）：由 DeathSequence 驱动，停止输入/射击。
    /// 正常（未暂停）流程下 _PhysicsProcess 也会自行推进，保证两种路径都生效。
    public void EnterDeathFall()
    {
        IsDying = true;
        _fallVelocity = Vector2.Zero;
        _invincibleTimer = 0f;
    }

    /// 坠落推进（每秒调用一次，dt=秒）：速度衰减 + 缓降 + 自旋。供 DeathSequence 每帧调用。
    public void ApplyDeathFall(float dt)
    {
        if (!IsDying) IsDying = true;
        // 水平速度快速衰减（失去控制），垂直缓慢加速下坠（失重感）
        _fallVelocity = _fallVelocity * 0.92f + new Vector2(0f, 55f) * dt;
        Position += _fallVelocity * dt;
        Rotation += 0.9f * dt;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 战败坠落：不响应输入/射击，由 ApplyDeathFall 推进（DeathSequence 或本帧循环）
        if (IsDying)
        {
            ApplyDeathFall(dt);
            return;
        }

        // 移动（8 方向 + 加速/减速）：快起快停，消除速度突变僵硬感
        var input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        var ratio = Input.IsActionPressed("focus") ? FocusSpeedRatio : 1f;
        var targetVel = input * MoveSpeed * ratio;
        float accel = input.LengthSquared() > 0f ? MoveAccel : MoveDecel;
        _velocity = _velocity.MoveToward(targetVel, accel * dt);
        Position += _velocity * dt;

        // 边界限制（PlayfieldSize 内留边距）
        var size = Autoload.GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);
        Position = new Vector2(
            Mathf.Clamp(Position.X, BoundaryMargin, size.X - BoundaryMargin),
            Mathf.Clamp(Position.Y, BoundaryMargin, size.Y - BoundaryMargin));

        // 自动射击（星辉魔弹，无需按键）
        _fireTimer -= dt;
        TryFire();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 受击白闪（短暂，先于无敌闪烁显示）
        if (_hitFlashTimer > 0f)
        {
            _hitFlashTimer -= dt;
            if (_hitFlashTimer <= 0f) QueueRedraw();
        }

        // 无敌帧：计时 + 高频闪烁（0.1s 周期，alpha 0.25 更明显）
        if (_invincibleTimer > 0f)
        {
            _invincibleTimer -= dt;
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinkTimer = BlinkPeriod;
                _blinkOn = !_blinkOn;
            }
            if (_hitFlashTimer > 0f)
                Modulate = new Color(1f, 1f, 1f, 1f); // 白闪期不降透明
            else
                Modulate = _blinkOn ? new Color(1f, 1f, 1f, 0.25f) : new Color(1f, 1f, 1f, 1f);
            if (_invincibleTimer <= 0f) Modulate = Colors.White;
        }
    }
}

/// <summary>
/// 局内升级选项（三选一，见 Main）。置于命名空间顶层，
/// 让 Main / Settlement 通过 `using MagicThunder.Player;` 直接引用。
/// </summary>
public enum UpgradeType
{
    /// 武器等级 +1（单发→双发→三向）。
    Weapon,
    /// 连射提升（射速 -25%）。
    RapidFire,
    /// 弹速提升（+20%）。
    BulletSpeed,
    /// 生命上限 +1（并回 1 血）。
    MaxHp,
    /// 获得护盾 ×1（抵挡一次伤害）。
    Shield,
    /// 穿透：玩家弹命中不消失，穿怪。
    Pierce,
    /// 引力：星之残片磁吸范围扩大。
    Magnet,
}
