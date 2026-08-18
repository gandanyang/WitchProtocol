using System;
using System.Collections.Generic;
using Godot;
using MagicThunder.Audio;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Effects;
using MagicThunder.Player;
using MagicThunder.Pickup;
using MagicThunder.Score;
using MagicThunder.UI;

namespace MagicThunder.Scenes;

/// <summary>
/// MVP 垂直切片主场景装配：玩家（黎歌）→ 3 波小怪 → Boss → 胜利/死亡结算（本关结束统一升级）。
/// 完整闭环：波次推进 / 命中加分 / Boss 战 / 升级三选一 / 重新挑战。业务逻辑在玩法层，这里只做装配与事件调度。
/// </summary>
public partial class Main : Node
{
    /// 深空星空背景（全屏，见 SetupBackground）。
    /// 素材来源：制作人用 comfyui 出图后，导出为 png 放到本路径即可生效（Godot 自动 import）；
    /// 文件缺失时回退 project.godot 的 default_clear_color（深空色），不会白屏。
    private const string BackgroundTexturePath = "res://assets/backgrounds/bg_deep_space.png";

    private PlayerController? _player;
    private Wave.WaveManager? _waves;
    private Enemy.Boss? _boss;
    private Hud? _hud;
    private Settlement? _settlement;
    private Node2D? _pickups; // 星之残片/道具层（独立于波次/Boss 生命周期，挂战场原点）

    // M2 局内成长：定期掉落安全网——保证「杀不到怪」也有稳定的经验节奏
    private const float PeriodicDropInterval = 6f;
    private const int PeriodicDropCount = 2;
    private float _dropTimer;

    // M2 道具：击杀小概率掉护盾/生命/火力（测试期保底手段）
    private const float PowerupDropChance = 0.2f;
    private bool _autoToggle; // 武器满级后 连射/弹速 交替（自动升级）

    // 帧率优化专项：自写距离碰撞检测所需的引用（去物理体后由 Main 统一判定命中）
    private BulletEmitter? _playerEmitter; // 玩家弹发射器 → 遍历其 Pool.Active
    private Node2D? _enemyBullets;         // 敌弹世界层 → 遍历其子节点

    // M1-PERF：碰撞热路径复用缓冲——消灭每帧 new List / GetChildren() 数组分配。
    // 这是敌方（敌机/Boss/敌弹）处理路径上「偶尔卡一下」GC 尖峰的主要来源之一。
    private readonly List<Bullet.Bullet> _hitBullets = new();          // A 段：命中待回收的玩家弹
    private Godot.Collections.Array<Node> _enemyBulletChildren = new(); // B 段：敌弹世界层子节点快照
    private int _enemyBulletChildrenCount = -1;                         // 子节点数变化时重建快照

    private int _score;
    private int _kills;
    private bool _over;
    private float _runTime; // 本关战斗时长（秒），战败结算统计用
    private readonly Random _rng = new();

    public override void _Process(double delta)
    {
        if (_over) return;
        _runTime += (float)delta; // 战斗计时（暂停/结算后停止）

        // M2 定期掉落：每 PeriodicDropInterval 秒随机落 2 片星之残片（杀不到怪也有成长节奏）
        _dropTimer -= (float)delta;
        if (_dropTimer <= 0f && _player != null && _pickups != null)
        {
            _dropTimer = PeriodicDropInterval;
            var size = GameManager.I!.PlayfieldSize;
            for (int i = 0; i < PeriodicDropCount; i++)
            {
                float x = 60f + (float)_rng.NextDouble() * Mathf.Max(size.X - 120f, 10f);
                float y = 60f + (float)_rng.NextDouble() * Mathf.Max(size.Y - 140f, 10f);
                SpawnFragment(new Vector2(x, y));
            }
        }
    }

    /// 帧率优化专项（2026-08-19）：去物理体后的自写距离碰撞检测。
    /// 每物理帧跑一次：玩家弹→敌人/Boss、敌弹→玩家、敌人接触→玩家。
    /// 命中采用「遍历收集 → 统一应用」的方式，避免在遍历中修改对象池/存活列表。
    public override void _PhysicsProcess(double delta)
    {
        if (_over || _player == null) return;
        var player = _player;
        bool vulnerable = player.IsVulnerable();

        // A. 玩家弹 → 敌人 / Boss
        if (_playerEmitter != null)
        {
            // 复用缓冲：Pool.Active 遍历中不能回池（Release 会 Remove 破坏枚举器），先收集后统一回池。
            _hitBullets.Clear();
            foreach (var b in _playerEmitter.Pool.Active)
            {
                if (!b.IsPlayerBullet) continue;
                var pos = b.Position;
                bool used = false;

                // A1. 打小怪
                if (_waves != null)
                {
                    foreach (var e in _waves.Alive)
                    {
                        if (e == null || !IsInstanceValid(e) || !e.IsInsideTree()) continue;
                        if (pos.DistanceTo(e.Position) <= Bullet.Bullet.Radius + Enemy.Enemy.Radius)
                        {
                            e.TakeDamage(b.Damage);
                            used = true;
                            if (!player.Pierce) break; // 穿透弹：命中不回收，继续飞行穿怪
                        }
                    }
                }
                if (used && !player.Pierce) { _hitBullets.Add(b); continue; }

                // A2. 打 Boss
                if (_boss != null && IsInstanceValid(_boss) && _boss.IsInsideTree()
                    && pos.DistanceTo(_boss.Position) <= Bullet.Bullet.Radius + Enemy.Boss.Radius)
                {
                    _boss.TakeDamage(b.Damage);
                    // Boss 命中一律回收（穿透弹若滞留 Boss 判定圈，会每物理帧多段伤害）
                    _hitBullets.Add(b);
                }
            }
            foreach (var b in _hitBullets) b.RecycleSelf();
        }

        // B. 敌弹 → 玩家（世界层里的敌弹；命中判定含自机 Hitbox，P0-2 小判定）
        if (vulnerable && _enemyBullets != null)
        {
            // 子节点快照只在数量变化时重建（敌弹生成/清层），弹道稳定期复用同一份数组，
            // 消灭 GetChildren() 每物理帧的数组分配。回收只置 Visible=false 不增删子节点，
            // 快照仍持有已回收弹，靠 !Visible 跳过（与原逻辑一致）。
            int childCount = _enemyBullets.GetChildCount();
            if (childCount != _enemyBulletChildrenCount)
            {
                _enemyBulletChildren = _enemyBullets.GetChildren();
                _enemyBulletChildrenCount = childCount;
            }
            foreach (Node n in _enemyBulletChildren)
            {
                if (n is not Bullet.Bullet b) continue;
                if (!b.Visible) continue; // 跳过已回收的孤儿（池已随敌人销毁，节点残留在层里）
                if (b.Position.DistanceTo(player.Position) <= Bullet.Bullet.Radius + PlayerController.HitboxRadius)
                {
                    b.RecycleSelf(); // 立即回池安全：命中即 break，不再继续遍历快照
                    EventBus.I.Dispatch("player_hit");
                    break; // 一帧最多受击一次；无敌帧由 player_hit 兜底
                }
            }
        }

        // C. 敌人接触 → 玩家（防贴脸无反馈，对齐 BUG-012；自爆怪用爆炸半径范围判定）
        if (vulnerable && _waves != null)
        {
            foreach (var e in _waves.Alive)
            {
                if (e == null || !IsInstanceValid(e) || !e.IsInsideTree()) continue;
                if (e.Position.DistanceTo(player.Position) <= e.ContactRadius + PlayerController.HitboxRadius)
                {
                    e.ContactPlayer();
                    break;
                }
            }
        }
    }

    public override void _Ready()
    {
        SetupBackground(); // 星空背景最先装配，保证在所有战场元素下层（ZIndex 最低）
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        // 移动端触控层（D-010）：虚拟摇杆 + 聚焦按钮；桌面自动隐藏，不影响键鼠
        AddChild(new UI.TouchControls { Name = "TouchControls" });
        _settlement = new Settlement { Name = "Settlement" };
        AddChild(_settlement);

        SetupStage();
        EventBus.I.World += OnWorldEvent;
    }

    /// 深空星空背景：Sprite2D 全屏 Keep-Aspect-Covered 拉伸，ZIndex=-10 放最底。
    /// 加载失败时靠 project.godot 的 default_clear_color（深空色）兜底，不会白屏。
    private void SetupBackground()
    {
        var size = GameManager.I!.PlayfieldSize;
        var tex = ResourceLoader.Load<Texture2D>(BackgroundTexturePath);
        if (tex == null || size.X <= 0f || size.Y <= 0f) return;
        var bg = new Sprite2D { Texture = tex, Centered = false, ZIndex = -10 };
        bg.Scale = Vector2.One * Mathf.Max(size.X / tex.GetWidth(), size.Y / tex.GetHeight());
        AddChild(bg);
    }

    public override void _ExitTree()
    {
        EventBus.I.World -= OnWorldEvent;
    }

    /// 开新一关：重置波次与战绩、回满血，保留玩家升级（武器/护盾等）。
    private void SetupStage()
    {
        var center = GameManager.I!.PlayfieldSize / 2f;

        // P0-1（敌弹世界坐标）：战场弹幕层，挂战场原点。敌弹 reparent 到此层，
        // LocalPosition ≈ 世界坐标，敌人移动/死亡不带动弹幕。只建一次。
        if (GetNodeOrNull("EnemyBullets") == null)
        {
            var enemyBullets = new Node2D { Name = "EnemyBullets", Position = Vector2.Zero };
            AddChild(enemyBullets);
        }

        // M2 拾取物层（星之残片）：挂战场原点，独立于波次/Boss 生命周期。只建一次。
        if (GetNodeOrNull("Pickups") == null)
        {
            var pickups = new Node2D { Name = "Pickups", Position = Vector2.Zero };
            AddChild(pickups);
        }
        if (GetNodeOrNull("Pickups") is Node2D pickupsLayer)
        {
            _pickups = pickupsLayer;
            foreach (Node c in pickupsLayer.GetChildren()) c.QueueFree(); // 清空上一关遗留残片
        }

        // BUG-007：重开一关前清空空中遗留敌弹——上一关的发射器（Enemy/Boss 子节点）已随其
        // QueueFree 销毁，这些 reparent 到世界层的子弹是"孤儿"，直接销毁不回池（池已随发射器没了）。
        if (GetNodeOrNull("EnemyBullets") is Node2D bulletsLayer)
        {
            _enemyBullets = bulletsLayer;
            ClearEnemyBullets(bulletsLayer);
        }

        // 玩家与发射器只建一次，跨关保留升级
        if (_player == null)
        {
            _playerEmitter = new BulletEmitter { Name = "PlayerEmitter" };
            AddChild(_playerEmitter);

            _player = new PlayerController { Name = "Player" };
            AddChild(_player);
            _player.Position = center + new Vector2(0, 120);
            _player.BindEmitter(_playerEmitter);
        }
        _player.RestoreForStage();

        _score = 0;
        _kills = 0;
        _over = false;
        _runTime = 0f;
        GetTree().Paused = false;
        _settlement!.Hide();

        if (_waves != null) { _waves.QueueFree(); _waves = null; }
        _waves = new Wave.WaveManager { Name = "Waves" };
        AddChild(_waves);
        _waves.SetTarget(_player);
        _waves.Begin();

        if (_boss != null) { _boss.QueueFree(); _boss = null; }
        RefreshHud();
    }

    /// 清空敌弹世界层（孤儿弹清理）：本波/上一关的发射器（Enemy/Boss 子节点）已随其 QueueFree
    /// 销毁，reparent 到世界层的子弹是"孤儿"（Recycle 指向已释放池），直接销毁不回池。
    /// 调用时机：重开一关前 + 每波清空后（BUG-008 残留收尾）。
    /// 清层同时作废 B 段碰撞的缓存快照——下一物理帧按新子节点数重建，避免遍历到已释放节点。
    private void ClearEnemyBullets(Node2D bulletsLayer)
    {
        foreach (Node c in bulletsLayer.GetChildren())
            c.QueueFree();
        _enemyBulletChildrenCount = -1;
    }

    private void OnWorldEvent(string name, GodotObject? payload)
    {
        if (_over) return;
        switch (name)
        {
            case "player_hit":
                if (_player!.OnHit())
                {
                    RefreshHud();
                    if (_player.IsDead) EndRun(false);
                }
                break;

            case "enemy_killed":
                _kills++;
                RefreshHud();
                SfxPlayer.Play("kill"); // 击杀音效
                if (payload is Enemy.Enemy e)
                {
                    _score += e.KillScore;
                    SpawnKillBurst(e.GlobalPosition); // 击杀爆炸特效（kill_burst 资产）
                    // M2 割草爽感：按 DropExp 掉落星之残片（击杀 → 吸收 → 升级的正循环）
                    for (int i = 0; i < e.DropExp; i++)
                        SpawnFragment(e.GlobalPosition + new Vector2(_rng.Next(-16, 17), _rng.Next(-16, 17)));
                    // M2 道具：小概率掉护盾/生命/火力（测试期保底回血/护盾手段）
                    if (_rng.NextDouble() < PowerupDropChance)
                        SpawnPowerup(e.GlobalPosition + new Vector2(_rng.Next(-20, 21), _rng.Next(-20, 21)));
                }
                break;

            case "pickup_absorbed":
                // M2：吸收星之残片 → 加经验 → 升级自动加火力（不弹三选一）
                if (payload is Pickup.Pickup p)
                {
                    int ups = _player!.AddExp(p.ExpValue);
                    RefreshHud();
                    if (ups > 0) AutoUpgrade(ups);
                }
                break;

            case "powerup_taken":
                // M2 道具：护盾/生命/火力即时生效
                if (payload is Pickup.Powerup pw)
                {
                    _player!.ApplyPowerup(pw.Kind);
                    SfxPlayer.Play("levelup"); // 道具吸收音效（与升级同音，反馈统一）
                    RefreshHud();
                }
                break;

            case "wave_cleared":
                // 当前波刚清空（Advance 尚未推进），CurrentWave-1 即本波下标
                _score += ScoreCalc.WaveClearBonus(_waves!.CurrentWave - 1);
                // BUG-008 残留收尾：本波全部敌机已阵亡，剩在 EnemyBullets 的都是孤儿弹
                // （发射器/池已随敌机销毁），直接销毁不回池，避免波次内幽灵弹残留。
                if (_enemyBullets != null) ClearEnemyBullets(_enemyBullets);
                RefreshHud();
                break;

            case "all_waves_cleared":
                SpawnBoss();
                break;

            case "boss_killed":
                _score += ScoreCalc.BossBonus;
                RefreshHud();
                EndRun(true);
                break;
        }
    }

    private void SpawnBoss()
    {
        var center = GameManager.I!.PlayfieldSize / 2f;
        _boss = new Enemy.Boss { Name = "Boss" };
        AddChild(_boss);
        _boss.Position = center + new Vector2(0, -140);
        _boss.SetTarget(_player);
        SfxPlayer.Play("boss_intro"); // Boss 出场轰鸣
        RefreshHud();
    }

    /// 击杀爆炸：kill_burst 黑底素材 + 去黑 shader，在敌人位置放一次放大淡出特效。
    private void SpawnKillBurst(Vector2 at)
    {
        var tex = ResourceLoader.Load<Texture2D>("res://assets/effects/kill_burst.png");
        var shader = ResourceLoader.Load<Shader>("res://assets/shaders/remove_dark.gdshader");
        if (tex == null) return;
        var fx = new SpawnEffect { Texture = tex, Lifetime = 0.35f, StartScale = 0.5f, EndScale = 1.3f };
        if (shader != null) fx.Material = new ShaderMaterial { Shader = shader };
        AddChild(fx);
        fx.Position = at;
    }

    /// M2 星之残片：生成一片（经验=1），磁吸半径取玩家当前值（引力升级即时生效）。
    private void SpawnFragment(Vector2 at)
    {
        if (_pickups == null || _player == null) return;
        var p = new Pickup.Pickup
        {
            Name = "Pickup",
            ExpValue = 1,
            MagnetRadius = _player.MagnetRadius,
        };
        _pickups.AddChild(p);
        p.Position = at;
        p.SetTarget(_player);
    }

    /// M2 道具：生成一个（护盾/生命/火力等权随机），磁吸半径取玩家当前值。
    private void SpawnPowerup(Vector2 at)
    {
        if (_pickups == null || _player == null) return;
        var kind = _rng.Next(3) switch
        {
            0 => Pickup.PowerupKind.Shield,
            1 => Pickup.PowerupKind.Life,
            _ => Pickup.PowerupKind.Fire,
        };
        var p = new Pickup.Powerup
        {
            Name = "Powerup",
            Kind = kind,
            MagnetRadius = _player.MagnetRadius,
        };
        _pickups.AddChild(p);
        p.Position = at;
        p.SetTarget(_player);
    }

    private void EndRun(bool victory)
    {
        _over = true;
        GetTree().Paused = true;
        if (victory)
        {
            // M2 胜利结算语义调整：升级已改在局内即时发生，胜利只做成绩结算 + 再来一局（从 Lv1 开始）
            _settlement!.ShowVictory(_score, _kills, _player!.Level, OnPlayAgain);
        }
        else
        {
            // 战败 CG（灵魂）：死亡演出（坠落→拉远→黑幕）→ 全屏 CG + DEFEATED 菜单（继续/回主菜单）
            var death = new DeathSequence { Name = "DeathSequence" };
            death.Setup(_player!, _settlement!, _score, _kills, _runTime,
                OnPlayAgain, // 继续：新一局（局内成长每局重置）
                () => GetTree().ChangeSceneToFile("res://scenes/TitleScene.tscn")); // 回到主菜单
            AddChild(death);
        }
    }

    /// M2 再来一局（胜利结算 / 战败继续共用）：局内成长全部重置，开新一局。
    /// 局内（经验/等级/武器/穿透/引力）每局重置；局外养成（meta）阶段2再接入。
    private void OnPlayAgain()
    {
        _player!.ResetProgression();
        SetupStage(); // SetupStage 内部已 _settlement.Hide() + GetTree().Paused = false
    }

    /// M2 升级自动加火力：升级不弹三选一，直接按顺序强化（武器优先 → 满级后连射/弹速交替）。
    /// 保持战斗不中断，「吃星 → 升级 → 火力变强」的正循环零停顿（补割草爽感）。
    private void AutoUpgrade(int count)
    {
        for (int i = 0; i < count; i++)
            _player!.ApplyUpgrade(NextAutoUpgrade());
        SfxPlayer.Play("levelup"); // 升级音效
        FeedbackSystem.ShakeScreen(0.12f, 4f); // 升级轻微震屏反馈
        RefreshHud();
    }

    /// 自动升级顺序：武器等级没满优先加武器（单发→双发→三向），满级后连射/弹速交替。
    private UpgradeType NextAutoUpgrade()
    {
        if (_player!.WeaponLevel < PlayerController.MaxWeaponLevel) return UpgradeType.Weapon;
        _autoToggle = !_autoToggle;
        return _autoToggle ? UpgradeType.RapidFire : UpgradeType.BulletSpeed;
    }

    private void RefreshHud()
    {
        if (_hud == null || _player == null || _waves == null) return;
        string stage = _boss != null && IsInstanceValid(_boss) && _boss.IsInsideTree()
            ? "BOSS 战"
            : $"波次 {_waves.CurrentWave}/{_waves.TotalWaves}";
        _hud.UpdateExp(_player.Level, _player.Exp, _player.ExpToNext); // 等级标签 + 经验条
        _hud.Say($"生命 {_player.Hp}/{_player.MaxHp} 盾{_player.Shield} 武器Lv{_player.WeaponLevel} | {stage} | 分数 {_score}");
    }
}
