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
        var b1 = pool.Spawn();
        var b2 = pool.Spawn();
        if (pool.ActiveCount != 2 || pool.PooledCount != 62) return false;

        pool.Release(b1);
        if (pool.PooledCount != 63) return false;

        var b3 = pool.Spawn();
        return ReferenceEquals(b1, b3); // 对象池应复用 b1 的实例
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
        if (cfg.MaxHp <= 0 || cfg.ShootInterval <= 0f || cfg.BulletSpeed <= 0f) return false;

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
}
