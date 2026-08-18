using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace MagicThunder.Test;

/// <summary>
/// M1-PERF：headless 弹幕压力基准（CPU/逻辑侧）。
/// headless 不渲染 → 测不出 GPU/draw call；渲染侧请打开 tests/StressTest.tscn（编辑器交互压力场）。
/// 用法（见 tools/bench.ps1）：
///   godot --headless --path . res://tests/StressBench.tscn -- --mode=bullets --count=1000 --seconds=4
///     mode=bullets : 维持 N 颗存活敌弹（0 敌机）——测「子弹 Node 数量」CPU 成本
///     mode=enemies : 维持 N 只敌机（朝中心移动+吐弹）——测「敌机 Node 数量」成本
///     mode=full    : 按 --enemies / --bullets 同时叠加
///     --sweep      : 全预设矩阵跑一遍（含 5000 弹压力），输出汇总报告
/// 输出：user://test_reports/benchmark_latest.txt（沙箱下 .godot/userhome/...，不入库）。
/// 隔离：独立场景 + 显式命令触发，不进正常流程（AI_GUARDRAIL 测试后门纪律）。
/// </summary>
public partial class StressBench : Node
{
    /// 性能红线（制作人定稿 2026-08-19，目标机器）：
    ///   常规 / 20敌机 / 50敌机 / 1000敌弹 / 2000敌弹 ≥ 60 FPS；5000 敌弹压力 / Boss 大招 ≥ 45 FPS。
    private const string ReportPath = "user://test_reports/benchmark_latest.txt";

    /// 全预设矩阵（敌机, 敌弹）。顺序：先纯弹量爬坡 → 再纯敌机 → 最后混合压力。
    private static readonly (int enemies, int bullets)[] Sweep =
    {
        (0, 100), (0, 500), (0, 1000), (0, 2000), (0, 5000),
        (6, 0), (20, 0), (50, 0),
        (20, 1000),
    };

    private StressLoad? _load;
    private readonly List<float> _frameMs = new();
    private long _bulletAcc;
    private long _enemyAcc;
    private float _cpuMsAcc;
    private float _phyMsAcc;
    private float _phaseTime;
    private bool _measuring;
    private bool _sweep;
    private int _sweepIndex = -1;
    private float _seconds = 4f;
    private float _warmup = 1f;
    private StringBuilder? _out;

    public override void _Ready()
    {
        // 锁 60 帧目标：CPU 快时帧时间≈16.6ms 封顶，CPU 慢时帧时间真实暴露 → 帧时间统计即性能悬崖。
        Engine.MaxFps = 60;

        var args = OS.GetCmdlineUserArgs();
        var mode = Arg(args, "mode") ?? "bullets";
        _sweep = HasFlag(args, "sweep");
        _seconds = FloatArg(args, "seconds", 4f);
        _warmup = FloatArg(args, "warmup", 1f);
        int enemies = IntArg(args, "enemies", 0);
        int bullets = IntArg(args, "bullets", 0);
        int count = IntArg(args, "count", -1);
        if (count >= 0)
        {
            if (mode == "enemies") enemies = count;
            else bullets = count;
        }

        _load = new StressLoad { Name = "StressLoad" };
        AddChild(_load);
        _load.TargetEnemies = enemies;
        _load.TargetBullets = bullets;

        _out = new StringBuilder();
        _out.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] M1-PERF StressBench（headless CPU/逻辑侧；渲染侧见 tests/StressTest.tscn）");
        _out.AppendLine($"mode={mode} enemies={enemies} bullets={bullets} seconds={_seconds}s warmup={_warmup}s sweep={_sweep}");

        if (_sweep) BeginNextPreset();
        else BeginPreset(enemies, bullets);
    }

    private void BeginNextPreset()
    {
        _sweepIndex++;
        if (_sweepIndex >= Sweep.Length) { FinishAndQuit(); return; }
        var (e, b) = Sweep[_sweepIndex];
        BeginPreset(e, b);
    }

    private void BeginPreset(int enemies, int bullets)
    {
        _load!.Clear();
        _load.TargetEnemies = enemies;
        _load.TargetBullets = bullets;

        _frameMs.Clear();
        _bulletAcc = _enemyAcc = 0;
        _cpuMsAcc = _phyMsAcc = 0f;
        _phaseTime = 0f;
        _measuring = false;
        _out!.AppendLine($"== preset enemies={enemies} bullets={bullets} ==");
    }

    public override void _Process(double delta)
    {
        if (_out == null || _load == null) return;
        _load.Step(); // 每帧补齐到目标数

        _phaseTime += (float)delta;
        if (!_measuring)
        {
            if (_phaseTime >= _warmup) { _measuring = true; _phaseTime = 0f; }
            return;
        }

        // 采样帧：帧时间 + CPU/物理耗时 + 活跃对象数
        _frameMs.Add((float)delta * 1000f);
        _cpuMsAcc += (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
        _phyMsAcc += (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f;
        _bulletAcc += _load.ControlledBullets;
        _enemyAcc += _load.ActiveEnemies;

        if (_phaseTime >= _seconds)
        {
            FinalizePreset();
            if (_sweep) BeginNextPreset();
            else FinishAndQuit();
        }
    }

    private void FinalizePreset()
    {
        int n = _frameMs.Count;
        if (n <= 0) { _out!.AppendLine("  (无采样)"); return; }

        var sorted = _frameMs.OrderBy(x => x).ToList();
        float avg = sorted.Average();
        float p50 = sorted[n / 2];
        float p95 = sorted[(int)(n * 0.95f)];
        float p99 = sorted[(int)(n * 0.99f)];
        float fps = Mathf.Min(60f, 1000f / avg);
        float fpsP1 = Mathf.Min(60f, 1000f / Mathf.Max(p99, 0.01f));
        float cpu = _cpuMsAcc / n;
        float phy = _phyMsAcc / n;
        int redline = _load!.TargetBullets >= 5000 ? 45 : 60;
        // 帧时间恰好卡满 60fps 上限时 1000/avg≈59.99，允许 0.05 浮点容差，避免满帧误判 FAIL。
        bool pass = fps + 0.05f >= redline;

        _out!.AppendLine(
            $"  fps={fps:F1}  p1low={fpsP1:F1}  frame[p50={p50:F1} p95={p95:F1} p99={p99:F1}]ms  " +
            $"cpu={cpu:F2}ms  phys={phy:F2}ms  bullets≈{_bulletAcc / n}  enemies≈{_enemyAcc / n}  " +
            $"total_on_screen={_load.TotalVisibleBullets}  " +
            $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}  " +
            $"红线{redline} → {(pass ? "PASS" : "FAIL")}");
    }

    private void FinishAndQuit()
    {
        var path = ProjectSettings.GlobalizePath(ReportPath);
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file != null) file.StoreString(_out!.ToString());
        GD.Print("\n" + _out);
        GD.Print($"[StressBench] 报告已写: {path}");
        GetTree().Quit(0);
    }

    private static string? Arg(string[] args, string name)
    {
        var p = "--" + name + "=";
        foreach (var a in args)
            if (a.StartsWith(p, StringComparison.Ordinal)) return a[p.Length..];
        return null;
    }

    private static bool HasFlag(string[] args, string name) => args.Any(a => a == "--" + name);
    private static int IntArg(string[] args, string name, int def) => int.TryParse(Arg(args, name), out var v) ? v : def;
    private static float FloatArg(string[] args, string name, float def) => float.TryParse(Arg(args, name), out var v) ? v : def;
}
