using System;
using System.Collections.Generic;
using Godot;

namespace MagicThunder.Autoload;

/// <summary>
/// Dev 测试中枢（可执行测试"探针"与"种子"）。
/// 纪律（对齐 ReturnToStars DevTestHub / TestSystem）：
///  - 探针(Probe)：只读检查某状态是否正确，返回 bool；无副作用；
///  - 种子(Seed)：把游戏置于某状态（用现有存档/事件系统，不直接改内存指针）；
///  - 探针与种子分离"当前状态"与"跳转目标"，避免散落调试代码；
///  - 发布构建必须彻底隐藏（见 docs/AI_GUARDRAIL.md 测试后门纪律）。
/// 运行方式：见 docs/dev/TestSystem.md 与 tools/run_tests.ps1。
/// </summary>
public partial class DevTestHub : Node
{
    public static DevTestHub I { get; private set; } = null!;

    private readonly Dictionary<string, Func<bool>> _probes = new();
    private readonly Dictionary<string, Action> _seeds = new();

    public override void _EnterTree() { I = this; }

    public override void _Ready()
    {
        Test.TestProbes.RegisterAll(this);
        GD.Print($"[DevTestHub] 已注册 {_probes.Count} 个探针");
    }

    public override void _ExitTree() { if (I == this) I = null!; }

    public void RegisterProbe(string id, Func<bool> fn) => _probes[id] = fn;
    public void RegisterSeed(string id, Action fn) => _seeds[id] = fn;

    public bool RunProbe(string id)
    {
        if (!_probes.TryGetValue(id, out var fn)) return false;
        try { return fn(); }
        catch (Exception e) { GD.PushError($"[DevTestHub] probe {id} 异常: {e.Message}"); return false; }
    }

    /// 批量跑探针，返回 id → 结果。不存在的探针记 false 并告警。
    public Dictionary<string, bool> RunMany(IEnumerable<string> ids)
    {
        var result = new Dictionary<string, bool>();
        foreach (var id in ids)
        {
            if (_probes.ContainsKey(id))
                result[id] = RunProbe(id);
            else
            {
                result[id] = false;
                GD.PushWarning($"[DevTestHub] 探针不存在: {id}");
            }
        }
        return result;
    }

    public IReadOnlyCollection<string> ProbeIds => _probes.Keys;
}