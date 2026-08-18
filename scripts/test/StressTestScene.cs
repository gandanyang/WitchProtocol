using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MagicThunder.Test;

/// <summary>
/// M1-PERF：编辑器内交互式压力测试场（DEV 后门，仅显式打开本场景使用，不进正常流程）。
/// 目的：在真渲染器（GL Compatibility）下看 draw call / 物理体 / 节点数与 FPS 的关系——
/// headless 基准测不了渲染侧，GPU 是否吃满要靠这里确认。
/// 按键：
///   1..5  敌弹预算 100 / 500 / 1000 / 2000 / 5000
///   6..8  敌机预算 6 / 20 / 50
///   0     清空负载
///   Q     退出
/// 面板每 0.5s 刷新：FPS / p99 帧ms / CPU·Phy ms / Draw calls / Phys 物体 / Node 数 / 存活敌弹·敌机。
/// 命令行自动跑（供自动化 GPU 压测，AI/制作人免键盘）：
///   godot --path . res://tests/StressTest.tscn -- --bullets=2000 --seconds=10 --autoquit
///   跑满 --seconds 后把最终面板指标打印到 stdout 并退出（帧数据带 1s 预热）。
/// 隔离：独立场景 + 显式打开触发，不进正常流程（AI_GUARDRAIL 测试后门纪律）。
/// </summary>
public partial class StressTestScene : Node
{
    private StressLoad? _load;
    private Label? _panel;
    private Label? _hint;
    private float _timer;
    private readonly List<float> _recent = new();

    // 命令行自动跑参数
    private int _autoBullets;
    private int _autoEnemies;
    private float _autoSeconds;
    private bool _autoQuit;
    private float _autoElapsed;
    private bool _warmedUp;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _autoBullets = IntArg(args, "bullets", 0);
        _autoEnemies = IntArg(args, "enemies", 0);
        _autoSeconds = FloatArg(args, "seconds", 0f);
        _autoQuit = HasFlag(args, "autoquit");

        _load = new StressLoad { Name = "StressLoad" };
        AddChild(_load);
        if (_autoBullets > 0 || _autoEnemies > 0)
        {
            _load.TargetBullets = _autoBullets;
            _load.TargetEnemies = _autoEnemies;
        }

        var ui = new CanvasLayer { Name = "Ui" };
        AddChild(ui);

        var hintText = _autoQuit
            ? $"自动跑: 敌弹={_autoBullets} 敌机={_autoEnemies} 时长={_autoSeconds}s"
            : "敌弹: 1=100 2=500 3=1000 4=2000 5=5000 | 敌机: 6=6 7=20 8=50 | 0=清空 | Q=退出";
        _hint = new Label
        {
            Text = hintText,
            Position = new Vector2(16, 12),
        };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        ui.AddChild(_hint);

        _panel = new Label { Text = "FPS --" };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetLeft = -340;
        _panel.OffsetTop = 12;
        _panel.OffsetRight = -16;
        _panel.OffsetBottom = 150;
        _panel.HorizontalAlignment = HorizontalAlignment.Right;
        _panel.AddThemeFontSizeOverride("font_size", 14);
        ui.AddChild(_panel);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed) return;
        switch (k.PhysicalKeycode)
        {
            case Key.Key1: SetLoad(0, 100); break;
            case Key.Key2: SetLoad(0, 500); break;
            case Key.Key3: SetLoad(0, 1000); break;
            case Key.Key4: SetLoad(0, 2000); break;
            case Key.Key5: SetLoad(0, 5000); break;
            case Key.Key6: SetLoad(6, 0); break;
            case Key.Key7: SetLoad(20, 0); break;
            case Key.Key8: SetLoad(50, 0); break;
            case Key.Key0: SetLoad(0, 0); break;
            case Key.Q: GetTree().Quit(); break;
        }
    }

    private void SetLoad(int enemies, int bullets)
    {
        _load!.TargetEnemies = enemies;
        _load.TargetBullets = bullets;
        _recent.Clear();
    }

    public override void _Process(double delta)
    {
        _load!.Step();

        float ms = (float)delta * 1000f;
        _recent.Add(ms);
        if (_recent.Count > 300) _recent.RemoveAt(0);

        // 命令行自动跑：预热 1s 后开始计时，跑满时长打印最终面板指标并退出。
        if (_autoQuit)
        {
            _autoElapsed += (float)delta;
            if (_autoElapsed >= 1f && !_warmedUp) { _warmedUp = true; _autoElapsed = 0f; }
            if (_warmedUp && _autoElapsed >= _autoSeconds)
            {
                PrintFinalMetrics();
                GetTree().Quit(0);
                return;
            }
        }

        _timer += (float)delta;
        if (_timer < 0.5f) return;
        _timer = 0f;

        var sorted = _recent.OrderBy(x => x).ToList();
        float p99 = sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.99f)] : 0f;
        float fps = (float)Engine.GetFramesPerSecond();
        float cpuMs = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
        float phyMs = (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f;
        long nodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        long phys = (long)Performance.GetMonitor(Performance.Monitor.Physics2DActiveObjects);
        long draw = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);

        _panel!.Text =
            $"FPS {fps:F0}  p99 {p99:F1}ms\n" +
            $"CPU {cpuMs:F1}ms  Phy {phyMs:F1}ms\n" +
            $"Node {nodes}  Phys {phys}\n" +
            $"Draw {draw}\n" +
            $"敌弹(受控) {_load.ControlledBullets}  敌机 {_load.ActiveEnemies}";
    }

    /// 自动跑收尾：把最终指标按行打印到 stdout（bench 日志可捕获），格式与面板一致。
    private void PrintFinalMetrics()
    {
        var sorted = _recent.OrderBy(x => x).ToList();
        float p99 = sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.99f)] : 0f;
        float fps = (float)Engine.GetFramesPerSecond();
        float cpuMs = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
        float phyMs = (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f;
        long nodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        long phys = (long)Performance.GetMonitor(Performance.Monitor.Physics2DActiveObjects);
        long draw = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        GD.Print($"[StressTest] 敌弹={_load!.ControlledBullets} 敌机={_load.ActiveEnemies}  " +
            $"fps={fps:F0}  p99={p99:F1}ms  cpu={cpuMs:F1}ms  phy={phyMs:F1}ms  " +
            $"draw={draw}  nodes={nodes}  phys={phys}");
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
