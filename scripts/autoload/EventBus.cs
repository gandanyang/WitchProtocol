using System.Collections.Generic;
using Godot;

namespace MagicThunder.Autoload;

/// <summary>
/// 轻量事件总线：解耦系统间通信。
/// 纪律（对齐 ReturnToStars EventManager）：
///  - 低频/跨系统事件用 <see cref="Dispatch"/>；
///  - 存档相关「只触发一次」用 <see cref="TriggerOnce"/>（读档后不重复）；
///  - 弹幕 hotpath（每帧几千次命中）不要走总线，用对象池内直连。
/// </summary>
public partial class EventBus : Node
{
    public static EventBus I { get; private set; } = null!;

    /// 全局订阅（各系统在 _Ready 里挂，_ExitTree 里卸）。
    public event System.Action<string, GodotObject?>? World;

    private readonly HashSet<string> _once = new();
    private readonly List<string> _log = new();

    public override void _EnterTree() { I = this; }
    public override void _ExitTree() { if (I == this) I = null!; }

    /// 广播一个事件（只记录日志 + 通知订阅者，不持久化）。
    public void Dispatch(string name, GodotObject? payload = null)
    {
        _log.Add(name);
        World?.Invoke(name, payload);
    }

    /// 只触发一次。返回 true 表示本次是首次触发（调用方负责执行其副作用）。
    /// 一次性事件的持久化标记由 SaveSystem 负责，这里是内存态去重。
    public bool TriggerOnce(string id)
    {
        if (_once.Contains(id)) return false;
        _once.Add(id);
        return true;
    }

    public bool HasFired(string id) => _once.Contains(id);

    /// 已触发的一次性事件快照（供 SaveSystem 入档，实现"读档不重复"）。
    public IReadOnlyList<string> FiredIds() => new List<string>(_once);

    public IReadOnlyList<string> Log => _log;
}