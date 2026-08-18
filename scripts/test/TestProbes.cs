using System.Linq;
using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;

namespace MagicThunder.Test;

/// <summary>
/// 地基探针集合：在 DevTestHub 启动时注册。
/// 每个探针都是「只读 + 无副作用」的状态检查，返回 true 表示地基运行正确。
/// 涉及画面渲染之外的纯逻辑，可 headless 运行（见 tools/run_tests.ps1）。
/// </summary>
public static class TestProbes
{
    public static void RegisterAll(DevTestHub hub)
    {
        hub.RegisterProbe("boot", ProbeBoot);
        hub.RegisterProbe("event", ProbeEvent);
        hub.RegisterProbe("save", ProbeSave);
        hub.RegisterProbe("pool", ProbePool);
        hub.RegisterProbe("pattern", ProbePattern);
        hub.RegisterProbe("enemy_config", ProbeEnemyConfig);
        hub.RegisterProbe("weapon", ProbeWeapon);
        hub.RegisterProbe("boss_config", ProbeBossConfig);
        hub.RegisterProbe("score", ProbeScore);
        // M1 手感（WorkBuddy 2026-08-19）：P0-2 判定 / 低速 / 无敌帧 / 自动射击 / 敌人移动 / P0-1 敌弹世界坐标
        hub.RegisterProbe("m1_hitbox", ProbeM1Hitbox);
        hub.RegisterProbe("m1_focus", ProbeM1Focus);
        hub.RegisterProbe("m1_iframes", ProbeM1Iframes);
        hub.RegisterProbe("m1_autofire", ProbeM1Autofire);
        hub.RegisterProbe("m1_enemy_move", ProbeM1EnemyMove);
        hub.RegisterProbe("m1_enemybullet_world", ProbeM1EnemyBulletWorld);
        // M1 反馈层（2026-08-19 二轮）：音效资产 / 全局反馈相机 / 战败坠落状态
        hub.RegisterProbe("sfx", ProbeSfx);
        hub.RegisterProbe("feedback", ProbeFeedback);
        hub.RegisterProbe("deathfall", ProbeDeathFall);
        // Bug 收口（2026-08-19 三轮）：孤儿弹池兜底 / 敌人接触伤害
        hub.RegisterProbe("m1_orphan", ProbeM1OrphanBullet);
        hub.RegisterProbe("m1_enemy_contact", ProbeM1EnemyContact);
    }

    /// 启动自检：四个 autoload 单例均已就位，且拿到有效视口。
    private static bool ProbeBoot()
    {
        return GameManager.I != null
               && GameManager.I.PlayfieldSize.X > 0 && GameManager.I.PlayfieldSize.Y > 0
               && EventBus.I != null
               && SaveSystem.I != null
               && DevTestHub.I != null;
    }

    /// 事件总线：Dispatch 记录日志；TriggerOnce 第二次应为 false（内存态去重）。
    private static bool ProbeEvent()
    {
        var bus = EventBus.I;
        bus.Dispatch("probe_event");
        if (!bus.TriggerOnce("probe_once")) return false; // 首次应为 true
        if (bus.TriggerOnce("probe_once")) return false;  // 第二次应为 false
        return bus.Log.Any(x => x == "probe_event") && bus.HasFired("probe_once");
    }

    /// 存档：写→存→读往返，版本一致，Reset 会生成备份且写回完整结构。
    private static bool ProbeSave()
    {
        var s = SaveSystem.I;
        s.Set("chapter", 7);
        s.Set("probe_marker", "alive");
        if (!s.Save()) return false;

        if (!s.Load()) return false;
        if (s.Get("chapter", -1).AsInt32() != 7) return false;
        if (s.Get("probe_marker", "").AsString() != "alive") return false;
        if (s.Get("save_version", -1).AsInt32() != SaveSystem.SaveVersion) return false;

        s.Reset(); // 生成 user://save_backup_*.json 并归位
        return s.HasSave() && s.Get("chapter", -1).AsInt32() == 0;
    }

    /// 子弹对象池：spawn/release 数量守恒，且 release 后的实例被复用（而非新建）。
    private static bool ProbePool()
    {
        var pool = new BulletPool(64);
        try
        {
            var b1 = pool.Spawn();
            var b2 = pool.Spawn();
            if (pool.ActiveCount != 2 || pool.PooledCount != 62) return false;

            pool.Release(b1);
            if (pool.PooledCount != 63) return false;

            var b3 = pool.Spawn();
            return ReferenceEquals(b1, b3); // 对象池应复用 b1 的实例
        }
        finally
        {
            pool.Free(); // BUG-002：未入树 Node 直接 Free，避免 64 个 Area2D RID 退出泄漏
        }
    }

    /// pattern 数学：aimed 长度=speed；spread 数量与间距正确；ring/spiral 数量与速率正确。
    private static bool ProbePattern()
    {
        var aimed = BulletPatterns.Aimed(Vector2.Zero, new Vector2(0, 100), 50);
        if (Mathf.Abs(aimed.Length() - 50) > 0.001f) return false;

        const float speed = 100f;
        var spread = BulletPatterns.Spread(Vector2.Zero, 0f, 5, 10f, speed);
        if (spread.Length != 5) return false;
        foreach (var v in spread) if (Mathf.Abs(v.Length() - speed) > 0.001f) return false;

        var ring = BulletPatterns.Ring(Vector2.Zero, 8, 60f, 5f);
        if (ring.Length != 8) return false;

        var spiral = BulletPatterns.Spiral(Vector2.Zero, 12, 80f, 15f);
        if (spiral.Length != 12) return false;
        foreach (var v in spiral) if (Mathf.Abs(v.Length() - 80f) > 0.001f) return false;

        return true;
    }

    /// enemy_config：Enemy 从 EnemyConfig.tres 正确读取 HP / 射击间隔 / 子弹速度。
    /// 让 Enemy 进树触发 _Ready；配置的 max_hp(5) ≠ 代码默认值(3)，具备区分度（未读配置会回退 3 而失败）。
    private static bool ProbeEnemyConfig()
    {
        var res = ResourceLoader.Load("res://data/EnemyConfig.tres", "", ResourceLoader.CacheMode.Ignore);
        if (res is not Data.EnemyConfig cfg) return false;
        // 哨兵区分度（BUG-005/011 教训）：配置文件值必须 ≠ 代码默认值，否则 .tres 未反序列化也会空转通过
        if (cfg.MaxHp <= 0 || cfg.ShootInterval <= 0f || cfg.BulletSpeed <= 0f) return false;
        if (cfg.MaxHp == 3) return false; // 默认 3 = 文件未生效

        var holder = new Node { Name = "ProbeEnemyHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy();
        holder.AddChild(enemy); // 触发 _Ready → 从配置读取

        bool ok = enemy.MaxHp == cfg.MaxHp
                  && enemy.ShootInterval == cfg.ShootInterval
                  && enemy.BulletSpeed == cfg.BulletSpeed;
        holder.QueueFree();
        return ok;
    }

    /// weapon：武器等级 → 射击弹数（1 单发 / 2 双发 / 3 三向）。纯函数可 headless。
    private static bool ProbeWeapon()
    {
        if (Player.PlayerController.ShotSpecs(Vector2.Zero, 1, 100f).Length != 1) return false;
        if (Player.PlayerController.ShotSpecs(Vector2.Zero, 2, 100f).Length != 2) return false;
        if (Player.PlayerController.ShotSpecs(Vector2.Zero, 3, 100f).Length != 3) return false;
        return true;
    }

    /// boss_config：Boss 从 BossConfig.tres 读取 HP / 弹速 / 圆环数（配置值 ≠ 代码默认，具备区分度）。
    private static bool ProbeBossConfig()
    {
        var res = ResourceLoader.Load("res://data/BossConfig.tres", "", ResourceLoader.CacheMode.Ignore);
        if (res is not Data.BossConfig cfg) return false;
        if (cfg.MaxHp <= 0 || cfg.BulletSpeed <= 0f || cfg.RingCount <= 0) return false;
        if (cfg.MaxHp == 50) return false; // 哨兵（BUG-011）：默认 50 = .tres 未反序列化（snake_case 失效）

        var holder = new Node { Name = "ProbeBossHolder" };
        DevTestHub.I.AddChild(holder);
        var boss = new Enemy.Boss();
        holder.AddChild(boss); // 触发 _Ready → 从配置读取

        bool ok = boss.MaxHp == cfg.MaxHp && boss.BulletSpeed == cfg.BulletSpeed;
        holder.QueueFree();
        return ok;
    }

    /// score：结算分数纯函数（击杀 ×100 + 波次奖励 + Boss 2000）。
    private static bool ProbeScore()
    {
        return Score.ScoreCalc.Compute(10, 1500, true)
               == 10 * Score.ScoreCalc.BasePerKill + 1500 + Score.ScoreCalc.BossBonus;
    }

    // ============ M1 手感探针（2026-08-19 WorkBuddy）============

    /// m1_hitbox：P0-2 回归——自机实际判定 3px（视觉尺寸 ≠ Hitbox，弹幕游戏铁律）。
    private static bool ProbeM1Hitbox()
    {
        return Player.PlayerController.HitboxRadius == 3f;
    }

    /// m1_focus：Shift 低速——FocusSpeedRatio 从 PlayerConfig.tres 正确读入，且必须是减速比（<1）。
    private static bool ProbeM1Focus()
    {
        var res = ResourceLoader.Load("res://data/PlayerConfig.tres", "", ResourceLoader.CacheMode.Ignore);
        if (res is not Data.PlayerConfig cfg) return false;
        if (cfg.FocusSpeedRatio >= 1f) return false; // 低速比必须 <1，否则"低速"无意义

        var holder = new Node { Name = "ProbeM1FocusHolder" };
        DevTestHub.I.AddChild(holder);
        var pc = new Player.PlayerController();
        holder.AddChild(pc); // 触发 _Ready → 从配置读取
        bool ok = pc.FocusSpeedRatio == cfg.FocusSpeedRatio;
        holder.QueueFree();
        return ok;
    }

    /// m1_iframes：受击 → 扣 1 血 + 进入无敌帧；无敌帧内重复受击被忽略；1.5s 后恢复可受击。
    private static bool ProbeM1Iframes()
    {
        var holder = new Node { Name = "ProbeM1IframesHolder" };
        DevTestHub.I.AddChild(holder);
        var pc = new Player.PlayerController();
        holder.AddChild(pc);

        int hp0 = pc.Hp;
        if (!pc.OnHit()) return false;        // 首次受击应消耗
        if (pc.Hp != hp0 - 1) return false;   // 扣 1 血
        if (pc.IsVulnerable()) return false;  // 受击后应进入无敌
        if (pc.OnHit()) return false;         // 无敌帧内重复受击应被忽略
        if (pc.Hp != hp0 - 1) return false;   // 血没有二次扣除

        pc._Process(1.6f);                     // 推进超过 1.5s 无敌时长
        bool ok = pc.IsVulnerable();           // 应恢复可受击
        holder.QueueFree();
        return ok;
    }

    /// m1_autofire：自动射击——TryFire 经发射器打出玩家弹（Lv1 单发，走对象池）。
    private static bool ProbeM1Autofire()
    {
        var holder = new Node { Name = "ProbeM1AutofireHolder" };
        DevTestHub.I.AddChild(holder);
        var pc = new Player.PlayerController();
        holder.AddChild(pc);
        var emitter = new BulletEmitter { Name = "M1Emitter" };
        holder.AddChild(emitter);
        pc.BindEmitter(emitter);

        pc.TryFire();
        bool ok = emitter.Pool.ActiveCount == 1; // Lv1 单发
        holder.QueueFree();
        return ok;
    }

    /// m1_enemy_move：敌人 MoveSpeed>0 时朝目标缓移（数据驱动 EnemyConfig.tres；1 秒位移 ≈ MoveSpeed）。
    private static bool ProbeM1EnemyMove()
    {
        var holder = new Node { Name = "ProbeM1EnemyMoveHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy();
        holder.AddChild(enemy); // _Ready → 读配置
        var target = new Node2D { Name = "M1Target", Position = new Vector2(0, 100) };
        holder.AddChild(target);
        enemy.SetTarget(target);
        enemy.Position = new Vector2(0, 0);
        if (enemy.MoveSpeed <= 0f) return false; // 配置必须开启移动

        enemy._PhysicsProcess(1.0);
        bool ok = Mathf.Abs(enemy.Position.Y - enemy.MoveSpeed) < 0.5f; // 1 秒朝 target 移 MoveSpeed px
        holder.QueueFree();
        return ok;
    }

    /// m1_enemybullet_world：P0-1 回归——敌弹生成后挂在 "EnemyBullets" 世界层，不在 Enemy 子树
    /// （敌人移动/死亡不带动弹幕；子弹进入世界坐标体系，为 Boss 弹幕打基础）。
    private static bool ProbeM1EnemyBulletWorld()
    {
        var holder = new Node { Name = "ProbeM1EnemyBulletWorldHolder" };
        DevTestHub.I.AddChild(holder);
        var worldLayer = new Node2D { Name = "EnemyBullets", Position = Vector2.Zero };
        holder.AddChild(worldLayer);

        var enemy = new Enemy.Enemy();
        holder.AddChild(enemy); // _Ready → 自动查找并注入 worldLayer
        enemy.SetTarget(worldLayer); // 发射条件：target 非 null
        enemy.Position = new Vector2(300, 200);

        // 推进超过 ShootInterval，触发一次发射
        enemy._PhysicsProcess(enemy.ShootInterval + 0.1);

        Bullet.Bullet? found = null;
        foreach (var child in worldLayer.GetChildren())
        {
            if (child is Bullet.Bullet b) { found = b; break; }
        }
        holder.QueueFree();
        return found != null; // 子弹在世界层 = P0-1 已修复
    }

    // ============ M1 反馈层探针（2026-08-19 二轮 WorkBuddy）============

    /// sfx：程序化音效资产完整性（tools/gen_sfx.py 产出的 5 个 wav 全部可加载）。
    private static bool ProbeSfx()
    {
        return Audio.SfxPlayer.ProbeAssets();
    }

    /// feedback：全局反馈系统——BattleCamera 挂到 GameManager 下并居中战场。
    private static bool ProbeFeedback()
    {
        if (GameManager.I == null) return false;
        if (!Effects.FeedbackSystem.Ensure()) return false;
        var cam = Effects.FeedbackSystem.Camera;
        return cam != null && cam.Position == GameManager.I.PlayfieldSize / 2f && cam.Zoom == Vector2.One;
    }

    /// deathfall：战败坠落——EnterDeathFall 进入坠落态，ApplyDeathFall 推进位置下降（演出可用）。
    private static bool ProbeDeathFall()
    {
        var holder = new Node { Name = "ProbeDeathFallHolder" };
        DevTestHub.I.AddChild(holder);
        var pc = new Player.PlayerController();
        holder.AddChild(pc);
        pc.Position = new Vector2(100, 100);

        pc.EnterDeathFall();
        if (!pc.IsDying) { holder.QueueFree(); return false; }

        float y0 = pc.Position.Y;
        for (int i = 0; i < 30; i++) pc.ApplyDeathFall(0.1f); // 模拟 3 秒坠落
        bool ok = pc.Position.Y > y0 + 20f; // 垂直下坠明显
        holder.QueueFree();
        return ok;
    }

    /// m1_orphan：孤儿弹兜底（BUG-008）——池释放后 Recycle 目标无效，RecycleSelf 走自毁而非抛异常。
    private static bool ProbeM1OrphanBullet()
    {
        var holder = new Node { Name = "ProbeOrphanHolder" };
        DevTestHub.I.AddChild(holder);

        var pool = new BulletPool(8);
        holder.AddChild(pool);
        var b = pool.Spawn();
        var world = new Node2D { Name = "OrphanWorld" };
        holder.AddChild(world);
        b.GetParent()?.RemoveChild(b); // 先摘除（Godot AddChild 不自动 reparent）
        world.AddChild(b); // 子弹 reparent 到世界层（脱离 pool 子树，模拟 P0-1 场景）
        b.Recycle = pool.Release;

        holder.RemoveChild(pool);
        pool.Free(); // 池释放；子弹仍在 world 下存活（孤儿）

        bool ok;
        try
        {
            b.RecycleSelf(); // 应走 QueueFree 兜底，不抛"访问已释放实例"
            ok = true;
        }
        catch
        {
            ok = false;
        }
        holder.QueueFree();
        return ok;
    }

    /// m1_enemy_contact：敌人接触伤害（BUG-012）——Enemy 的 CollisionMask 含玩家层（BodyEntered 可触发）。
    private static bool ProbeM1EnemyContact()
    {
        var holder = new Node { Name = "ProbeEnemyContactHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy();
        holder.AddChild(enemy); // _Ready → 设置 mask
        bool ok = (enemy.CollisionMask & Bullet.CollisionLayers.Player) != 0;
        holder.QueueFree();
        return ok;
    }
}
