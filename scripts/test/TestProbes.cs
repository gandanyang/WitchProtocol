using System.Linq;
using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Player;
using MagicThunder.UI;

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
        // M2 割草爽感（2026-08-19 转玩法）：拾取物 / 经验升级 / 新敌行为 / 5 波 / 升级池
        hub.RegisterProbe("m2_pickup", ProbeM2Pickup);
        hub.RegisterProbe("m2_exp", ProbeM2Exp);
        hub.RegisterProbe("m2_upgrade", ProbeM2Upgrade);
        hub.RegisterProbe("m2_enemy_bomber", ProbeM2EnemyBomber);
        hub.RegisterProbe("m2_enemy_sniper", ProbeM2EnemySniper);
        hub.RegisterProbe("m2_enemy_sprayer", ProbeM2EnemySprayer);
        hub.RegisterProbe("m2_waves", ProbeM2Waves);
        hub.RegisterProbe("m2_loop", ProbeM2Loop);
        // M2 道具（2026-08-19 WorkBuddy）：护盾/生命/火力——吸收派发 + 即时增益落地
        hub.RegisterProbe("m2_powerup", ProbeM2Powerup);
        // M2 移动端适配（D-010 触屏基调）：虚拟摇杆方向 → 移动动作映射
        hub.RegisterProbe("m2_touch", ProbeM2Touch);
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
            pool.Free(); // BUG-002：未入树 Node 直接 Free，避免 64 个 Node 退出泄漏
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
        // 探针隔离（2026-08-19）：必须立即 Free，不能 QueueFree——整批探针在同一帧跑完，
        // QueueFree 要等帧末才生效，残留的全局名 "EnemyBullets" 层会被后续探针的 Enemy._Ready
        // 误发现（Sniper/Sprayer 子弹 reparent 进去导致计数为 0），破坏探针独立性。
        holder.Free();
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

    /// m1_enemy_contact：敌人接触伤害（BUG-012 新契约，帧率优化专项）——去物理体后，
    /// 接触由 Enemy.ContactPlayer() 驱动：派发 player_hit 事件（Main 收到后扣血/判死）并自身消失。
    private static bool ProbeM1EnemyContact()
    {
        var holder = new Node { Name = "ProbeEnemyContactHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy();
        holder.AddChild(enemy);

        bool dispatched = false;
        void Handler(string name, GodotObject? payload)
        {
            if (name == "player_hit") dispatched = true;
        }
        EventBus.I.World += Handler;
        enemy.ContactPlayer();
        EventBus.I.World -= Handler;

        bool ok = Enemy.Enemy.Radius > 0f && dispatched; // 判定半径存在 + 接触能派发受击事件
        holder.QueueFree();
        return ok;
    }

    // ============ M2 割草爽感探针（2026-08-19 转玩法 WorkBuddy）============

    /// 统计子树里可见的敌弹（对象池预分配弹 Visible=false，天然被排除；只数发射后真正在飞的）。
    private static int CountVisibleEnemyBullets(Node root)
    {
        int n = 0;
        foreach (Node c in root.GetChildren())
        {
            if (c is Bullet.Bullet b && b.Visible && !b.IsPlayerBullet) n++;
            n += CountVisibleEnemyBullets(c);
        }
        return n;
    }

    /// 统计整棵场景树里可见的敌弹。用「发射前后差值」做断言：
    /// 子弹不管挂到敌机子树还是 Main 的 EnemyBullets 世界层（前序探针 QueueFree 挂起中残留）都能数到；
    /// 基线差也免疫同批次其它探针留下的孤儿弹。
    private static int CountSceneEnemyBullets()
        => CountVisibleEnemyBullets(DevTestHub.I.GetTree().Root);

    /// m2_pickup：星之残片——进入吸收半径派发 pickup_absorbed（Main 收到后加经验）；默认经验=1、磁吸半径=基础值。
    private static bool ProbeM2Pickup()
    {
        var holder = new Node { Name = "ProbeM2PickupHolder" };
        DevTestHub.I.AddChild(holder);
        var target = new Node2D { Name = "M2Target", Position = new Vector2(100, 100) };
        holder.AddChild(target);

        // 距 target 10px < AbsorbRadius(18)，首帧即吸收
        var p = new Pickup.Pickup { Name = "M2Pickup", Position = new Vector2(100, 110) };
        holder.AddChild(p);
        p.SetTarget(target);

        bool dispatched = false;
        void Handler(string name, GodotObject? payload)
        {
            if (name == "pickup_absorbed" && payload is Pickup.Pickup) dispatched = true;
        }
        EventBus.I.World += Handler;
        p._PhysicsProcess(0.1); // 进入吸收半径 → 派发 + QueueFree
        EventBus.I.World -= Handler;

        bool ok = dispatched && p.ExpValue == 1 && p.MagnetRadius == Pickup.Pickup.PickupMagnetBase;
        holder.QueueFree();
        return ok;
    }

    /// m2_exp：经验曲线纯函数 + AddExp 推进——Lv1 需 5 经验，之后每级 +4；未满不升、补满即升、可连升。
    private static bool ProbeM2Exp()
    {
        if (Player.PlayerController.ExpToNextFor(1) != 5) return false;
        if (Player.PlayerController.ExpToNextFor(2) != 9) return false;
        if (Player.PlayerController.ExpToNextFor(3) != 13) return false;

        var pc = new Player.PlayerController(); // 初始态（不进树即可测纯逻辑）
        try
        {
            if (pc.Level != 1 || pc.Exp != 0 || pc.ExpToNext != 5) return false;

            if (pc.AddExp(4) != 0 || pc.Level != 1 || pc.Exp != 4) return false; // 未满阈值不升级
            if (pc.AddExp(1) != 1 || pc.Level != 2 || pc.Exp != 0 || pc.ExpToNext != 9) return false;
            if (pc.AddExp(9) != 1 || pc.Level != 3 || pc.Exp != 0) return false; // 一次性补满 → 升 1 级
            return true;
        }
        finally { pc.Free(); } // 未入树实例必须 Free，避免退出 RID 泄漏（对齐 ProbePool 纪律）
    }

    /// m2_upgrade：局内升级落地——穿透/引力/武器封顶/生命上限，均正确改变玩家属性（割草爽感来源）。
    private static bool ProbeM2Upgrade()
    {
        var pc = new Player.PlayerController();
        try
        {
            // 初始态
            if (pc.Pierce || pc.MagnetRadius != Pickup.Pickup.PickupMagnetBase || pc.WeaponLevel != 1 || pc.MaxHp != 3) return false;

            pc.ApplyUpgrade(UpgradeType.Pierce);
            if (!pc.Pierce) return false;

            pc.ApplyUpgrade(UpgradeType.Magnet);
            float magnetAfter = Pickup.Pickup.PickupMagnetBase + Player.PlayerController.MagnetUpgradeBonus;
            if (Mathf.Abs(pc.MagnetRadius - magnetAfter) > 0.01f) return false;

            for (int i = 0; i < 4; i++) pc.ApplyUpgrade(UpgradeType.Weapon); // 连点应封顶
            if (pc.WeaponLevel != Player.PlayerController.MaxWeaponLevel) return false;

            pc.ApplyUpgrade(UpgradeType.MaxHp);
            return pc.MaxHp == 4 && pc.Hp == 4; // 生命上限 +1 且回 1 血
        }
        finally { pc.Free(); } // 未入树实例必须 Free，避免退出 RID 泄漏（对齐 ProbePool 纪律）
    }

    /// m2_enemy_bomber：自爆怪——Bomber 配置加载（MoveSpeed=220 哨兵）+ 冲脸不射击 + 接触按爆炸半径触发受击。
    private static bool ProbeM2EnemyBomber()
    {
        var holder = new Node { Name = "ProbeM2BomberHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy { Kind = Data.EnemyKind.Bomber };
        holder.AddChild(enemy); // _Ready → 读 enemy_bomber.tres
        var target = new Node2D { Name = "M2Target", Position = new Vector2(0, 300) };
        holder.AddChild(target);
        enemy.SetTarget(target);

        // 哨兵（默认 MoveSpeed=0 → 未读配置会失败）：自爆怪高速 + 判定半径 = 爆炸半径
        if (enemy.MoveSpeed != 220f) { holder.QueueFree(); return false; }
        if (enemy.Kind != Data.EnemyKind.Bomber) { holder.QueueFree(); return false; }
        if (enemy.ExplosionRadius <= 0f || enemy.ContactRadius != enemy.ExplosionRadius) { holder.QueueFree(); return false; }

        // 自爆怪不射击：跑 2 秒不应有子弹（ShootInterval=0，UpdateBomber 无发射逻辑）
        enemy._PhysicsProcess(2.0);
        if (CountVisibleEnemyBullets(enemy) != 0) { holder.QueueFree(); return false; }

        // 接触 → 派发 player_hit（自爆范围判定，Main 扣血）
        bool hit = false;
        void Handler(string name, GodotObject? payload) { if (name == "player_hit") hit = true; }
        EventBus.I.World += Handler;
        enemy.ContactPlayer();
        EventBus.I.World -= Handler;

        bool ok = hit;
        holder.QueueFree();
        return ok;
    }

    /// m2_enemy_sniper：狙击怪——Sniper 配置加载（LockTime=0.7 哨兵）+ 停靠→锁定→发射一枚高速瞄准弹。
    private static bool ProbeM2EnemySniper()
    {
        var holder = new Node { Name = "ProbeM2SniperHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy { Kind = Data.EnemyKind.Sniper };
        holder.AddChild(enemy); // _Ready → 读 enemy_sniper.tres
        var target = new Node2D { Name = "M2Target", Position = new Vector2(0, 600) };
        holder.AddChild(target);
        enemy.SetTarget(target);

        // 哨兵（默认 LockTime=0.6 / BulletSpeed=180 → 未读配置会失败）
        if (enemy.LockTime != 0.7f || enemy.BulletSpeed != 320f) { holder.QueueFree(); return false; }

        // 已低于停靠位（approachY = PlayfieldSize.Y*0.42；按实际战场高度计算，
        // 兼容 headless 下 PlayfieldSize.Y≠720 的情况——敌机放在停靠位下方 → 首帧即转锁定态）
        float approachY = (Autoload.GameManager.I?.PlayfieldSize.Y ?? 720f) * 0.42f;
        enemy.Position = new Vector2(0, approachY + 50f);
        enemy._PhysicsProcess(0.1);                       // Approach 帧（已在停靠位下方 → 转 Lock）
        enemy._PhysicsProcess(enemy.LockTime + 0.1f);     // 锁定结束 → 发射
        bool ok = CountVisibleEnemyBullets(enemy) == 1;  // 只发一发瞄准弹
        holder.QueueFree();
        return ok;
    }

    /// m2_enemy_sprayer：弹幕怪——Sprayer 配置加载（PatternCount=12 哨兵）+ 周期性环形弹幕 12 发。
    private static bool ProbeM2EnemySprayer()
    {
        var holder = new Node { Name = "ProbeM2SprayerHolder" };
        DevTestHub.I.AddChild(holder);
        var enemy = new Enemy.Enemy { Kind = Data.EnemyKind.Sprayer };
        holder.AddChild(enemy); // _Ready → 读 enemy_sprayer.tres
        var target = new Node2D { Name = "M2Target", Position = new Vector2(0, 600) };
        holder.AddChild(target);
        enemy.SetTarget(target);

        // 哨兵（默认 PatternCount=10 / MoveSpeed=0 = 不追人 → 未读配置会失败）
        if (enemy.PatternCount != 12 || enemy.MoveSpeed != 0f) { holder.QueueFree(); return false; }

        enemy._PhysicsProcess(enemy.ShootInterval + 0.2f); // 1.6s > ShootInterval(1.4) → 环形弹幕
        bool ok = CountVisibleEnemyBullets(enemy) == 12;
        holder.QueueFree();
        return ok;
    }

    /// m2_waves：波次配置——5 波主题化：W1 纯 Chaser（教学），W5 全类型（爆发）；每波人数/类型池有效。
    private static bool ProbeM2Waves()
    {
        var waves = Wave.WaveManager.Waves;
        if (waves.Length != 5) return false;
        foreach (var (count, kinds) in waves)
        {
            if (count <= 0 || kinds.Length == 0) return false;
            foreach (var k in kinds)
                if ((int)k < 0 || (int)k > 3) return false; // 类型必须合法
        }
        if (waves[0].kinds.Any(k => k != Data.EnemyKind.Chaser)) return false; // W1 教学：纯基础怪

        bool hasChaser = false, hasBomber = false, hasSniper = false, hasSprayer = false;
        foreach (var k in waves[4].kinds) // W5 爆发：全类型齐上
        {
            hasChaser |= k == Data.EnemyKind.Chaser;
            hasBomber |= k == Data.EnemyKind.Bomber;
            hasSniper |= k == Data.EnemyKind.Sniper;
            hasSprayer |= k == Data.EnemyKind.Sprayer;
        }
        return hasChaser && hasBomber && hasSniper && hasSprayer;
    }

    /// m2_loop：M2 正循环纯逻辑闭环——EnemyKilled → 掉星 → Pickup 吸收 → XP → 升级 → 强化 → 武器态变化。
    /// 击杀走真实 TakeDamage（派发 enemy_killed + DropExp>0 掉星条件）；掉落/吸收按 Main.SpawnFragment /
    /// pickup_absorbed 同款接线模拟；吸收满 Lv1→Lv2 经验后 AddExp 返回升级数 → ApplyUpgrade(Weapon) →
    /// ShotSpecs 弹道数增大（"弹幕可见变强"的纯逻辑证据）。
    private static bool ProbeM2Loop()
    {
        var holder = new Node { Name = "ProbeM2LoopHolder" };
        DevTestHub.I.AddChild(holder);

        // 1) 击杀小怪（基础 Chaser）：能掉星（DropExp>0）+ enemy_killed 事件派发
        var enemy = new Enemy.Enemy();
        holder.AddChild(enemy);
        if (enemy.DropExp <= 0) { holder.QueueFree(); return false; }
        bool killed = false;
        void KillHandler(string name, GodotObject? payload)
        {
            if (name == "enemy_killed" && ReferenceEquals(payload, enemy)) killed = true;
        }
        EventBus.I.World += KillHandler;
        enemy.TakeDamage(999); // 击杀 → enemy_killed（Main 据此掉星）
        EventBus.I.World -= KillHandler;
        if (!killed) { holder.QueueFree(); return false; }

        // 2) 玩家开局态（Lv1 / Exp0 / 武器1）
        var pc = new Player.PlayerController();
        holder.AddChild(pc);
        pc.Position = new Vector2(0, 0);
        int weaponBefore = pc.WeaponLevel;
        if (weaponBefore != 1 || pc.Level != 1 || pc.Exp != 0) { holder.QueueFree(); return false; }

        // 3) 按 Main 同款接线：生成足量星之残片（ExpValue=1）吸附吸收 → pickup_absorbed → AddExp
        int ups = 0;
        void AbsorbHandler(string name, GodotObject? payload)
        {
            if (name == "pickup_absorbed" && payload is Pickup.Pickup p)
                ups += pc.AddExp(p.ExpValue); // Main.OnWorldEvent 同款
        }
        EventBus.I.World += AbsorbHandler;
        int needToLevel = pc.ExpToNext; // Lv1 需 5 片
        for (int i = 0; i < needToLevel; i++)
        {
            var p = new Pickup.Pickup { ExpValue = 1, Position = new Vector2(0, 5) };
            holder.AddChild(p);
            p.SetTarget(pc);
            p._PhysicsProcess(0.1); // 距 pc 5px < AbsorbRadius(18) → 吸收派发
        }
        EventBus.I.World -= AbsorbHandler;
        if (ups <= 0 || pc.Level != 2) { holder.QueueFree(); return false; } // 吸满 5 片 → Lv2

        // 4) 升级 → 强化（Main.AutoUpgrade 的落地：自动加火力——应用武器强化）
        pc.ApplyUpgrade(UpgradeType.Weapon);

        // 5) 武器态变化：弹道数 1→2（"弹幕变了"的纯逻辑证据）
        int shotsBefore = Player.PlayerController.ShotSpecs(Vector2.Zero, weaponBefore, 100f).Length;
        int shotsAfter = Player.PlayerController.ShotSpecs(Vector2.Zero, pc.WeaponLevel, 100f).Length;
        bool ok = pc.WeaponLevel == weaponBefore + 1 && shotsAfter == shotsBefore + 1;
        holder.QueueFree();
        return ok;
    }

    /// m2_powerup：道具系统——三种道具（护盾/生命/火力）进入吸收半径都派发 powerup_taken
    /// （payload=自身，Main 收到后应用）；PlayerController.ApplyPowerup 即时增益落地：
    /// 盾 +1 / 满血回血不超上限 / 火力武器 +1（非满级）。
    private static bool ProbeM2Powerup()
    {
        var holder = new Node { Name = "ProbeM2PowerupHolder" };
        DevTestHub.I.AddChild(holder);
        var target = new Node2D { Name = "M2Target", Position = new Vector2(100, 100) };
        holder.AddChild(target);

        // 1) 三种道具距 target 10px < AbsorbRadius(18)，首帧吸收派发 powerup_taken
        int taken = 0;
        bool kindOk = true;
        void Handler(string name, GodotObject? payload)
        {
            if (name == "powerup_taken" && payload is Pickup.Powerup pw)
            {
                taken++;
                kindOk &= pw.Kind == Pickup.PowerupKind.Shield
                          || pw.Kind == Pickup.PowerupKind.Life
                          || pw.Kind == Pickup.PowerupKind.Fire;
            }
        }
        EventBus.I.World += Handler;
        foreach (var k in new[] { Pickup.PowerupKind.Shield, Pickup.PowerupKind.Life, Pickup.PowerupKind.Fire })
        {
            var p = new Pickup.Powerup { Kind = k, Position = new Vector2(100, 110) };
            holder.AddChild(p);
            p.SetTarget(target);
            p._PhysicsProcess(0.1); // 吸收 → 派发 + QueueFree
        }
        EventBus.I.World -= Handler;
        if (taken != 3 || !kindOk) { holder.QueueFree(); return false; }

        // 2) ApplyPowerup 落地（未入树实例，默认 Hp=MaxHp=3 / 武器1 / 盾0）
        var pc = new Player.PlayerController();
        try
        {
            pc.ApplyPowerup(Pickup.PowerupKind.Shield);
            if (pc.Shield != 1) return false;                 // 盾 +1
            pc.ApplyPowerup(Pickup.PowerupKind.Life);
            if (pc.Hp != pc.MaxHp) return false;              // 满血回血 = 不超上限
            int wl = pc.WeaponLevel;
            pc.ApplyPowerup(Pickup.PowerupKind.Fire);
            return pc.WeaponLevel == wl + 1;                  // 火力：武器等级 +1
        }
        finally { pc.Free(); holder.QueueFree(); }
    }

    /// m2_touch：虚拟摇杆方向 → 移动动作映射（D-010 移动端基调）。
    /// 验证 TouchControls.DirToActions 纯函数：零方向全松 / 正交单键 / 对角双键 / deadzone 内全松。
    private static bool ProbeM2Touch()
    {
        var (l, r, u, d) = TouchControls.DirToActions(Vector2.Zero);
        if (l || r || u || d) return false; // 零方向 → 全松

        (l, r, u, d) = TouchControls.DirToActions(new Vector2(1f, 0f));
        if (!r || l || u || d) return false; // 右推 → 仅 move_right

        (l, r, u, d) = TouchControls.DirToActions(new Vector2(0f, -1f));
        if (!u || l || r || d) return false; // 上推 → 仅 move_up

        (l, r, u, d) = TouchControls.DirToActions(new Vector2(-0.5f, 0.5f));
        if (!l || !d || r || u) return false; // 左下对角 → left + down

        (l, r, u, d) = TouchControls.DirToActions(new Vector2(0.1f, 0f));
        if (l || r || u || d) return false; // deadzone 内（0.2 阈值）→ 全松

        (l, r, u, d) = TouchControls.DirToActions(new Vector2(0.5f, -0.5f).Normalized());
        return r && u && !l && !d; // 右上对角（屏幕 Y 向下，-Y 为上；归一化后）→ right + up
    }
}
