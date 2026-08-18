using System.Collections.Generic;
using Godot;
using MagicThunder.Autoload;

namespace MagicThunder.Bullet;

/// <summary>
/// 子弹对象池：弹幕手感/性能的基础设施。
///  - 预热分配 <see cref="PooledCount"/> 个实例，Spawn 从池取、Release 回池复用（绝不高频 new）；
///  - 出屏回收（_Process 里对出界子弹自动 Release）；
///  - 峰值不足时 Spawn 会临时扩容，避免弹幕变糊/崩溃。
/// 挂载点应放在战场原点（作为"弹幕层"根节点），保证子弹 LocalPosition ≈ 世界坐标。
/// </summary>
public partial class BulletPool : Node
{
    private readonly Stack<Bullet> _pool = new();
    private readonly List<Bullet> _active = new();

    public BulletPool(int capacity = 512)
    {
        for (int i = 0; i < capacity; i++)
            _pool.Push(Create());
    }

    private Bullet Create()
    {
        var b = new Bullet { Visible = false };
        b.SetPhysicsProcess(false);
        AddChild(b);
        return b;
    }

    public Bullet Spawn()
    {
        var b = _pool.Count > 0 ? _pool.Pop() : Create(); // 扩容路径
        b.Reset();
        b.Visible = true;
        b.SetPhysicsProcess(true);
        _active.Add(b);
        return b;
    }

    public void Release(Bullet b)
    {
        if (b == null || !_active.Remove(b)) return;
        b.Reset();
        b.Visible = false;
        b.SetPhysicsProcess(false);
        _pool.Push(b);
    }

    public override void _Process(double delta)
    {
        Vector2 size = GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);
        const float margin = 60f;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var b = _active[i];
            var p = b.Position;
            if (p.X < -margin || p.X > size.X + margin || p.Y < -margin || p.Y > size.Y + margin)
                Release(b);
        }
    }

    public int ActiveCount => _active.Count;
    public int PooledCount => _pool.Count;

    /// 当前存活子弹（供 Main 自写距离碰撞检测遍历；返回内部列表只读视图，勿在遍历中修改）。
    public IReadOnlyList<Bullet> Active => _active;
}