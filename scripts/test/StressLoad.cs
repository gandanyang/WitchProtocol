using System;
using System.Collections.Generic;
using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Enemy;

namespace MagicThunder.Test;

/// <summary>
/// M1-PERF：压力负载生成器（共享给 headless StressBench 与编辑器 StressTest 场景）。
/// 用现有 Enemy / BulletEmitter + BulletPool 搭负载（不是新写一套独立模拟），
/// 每帧按目标数补齐存活敌弹/敌机，测「Node 数量 vs CPU」的成本曲线。
/// 纯测试资产，仅由显式场景/命令触发，不进正常流程（AI_GUARDRAIL 测试后门纪律）。
/// </summary>
public partial class StressLoad : Node
{
    private const string LayerName = "EnemyBullets";

    private BulletEmitter? _benchEmitter;
    private Node2D? _layer;
    private Node2D? _target;
    private readonly List<Enemy.Enemy> _enemies = new();
    private readonly Random _rng = new();

    /// 目标存活敌弹数（≤0 表示不补弹）。受控弹走 bench 发射器（与敌机自吐的弹分开）。
    public int TargetBullets;
    /// 目标存活敌机数（≤0 表示不补敌机）。
    public int TargetEnemies;

    public int ActiveEnemies => _enemies.Count;
    /// 受控敌弹数（bench 发射器池里的存活弹）。
    public int ControlledBullets => _benchEmitter?.Pool.ActiveCount ?? 0;

    public override void _Ready()
    {
        // 战场弹幕层挂原点（Enemy/Bullet 的 WorldLayer 约定，LocalPosition ≈ 世界坐标）
        _layer = new Node2D { Name = LayerName, Position = Vector2.Zero };
        AddChild(_layer);

        _benchEmitter = new BulletEmitter { Name = "BenchEmitter" };
        AddChild(_benchEmitter);
        _benchEmitter.WorldLayer = _layer;

        // 敌机目标 = 战场中心（真实移动 + Aimed 吐弹）
        _target = new Node2D { Name = "BenchTarget" };
        AddChild(_target);
        _target.Position = PlayfieldSize / 2f;
    }

    private Vector2 PlayfieldSize => GameManager.I?.PlayfieldSize ?? new Vector2(1280, 720);

    /// 每帧调用：按目标数补齐敌机/敌弹。
    public void Step()
    {
        // 1) 敌机补齐（顺带清理已阵亡/失效的）
        for (int i = _enemies.Count - 1; i >= 0; i--)
            if (!GodotObject.IsInstanceValid(_enemies[i]) || !_enemies[i].IsInsideTree())
                _enemies.RemoveAt(i);
        while (_enemies.Count < TargetEnemies) SpawnEnemy();

        // 2) 敌弹补齐：圆环从随机点发射，向四周散开（出屏由池自动回收）
        if (_benchEmitter != null && TargetBullets > 0)
        {
            int need = TargetBullets - _benchEmitter.Pool.ActiveCount;
            if (need > 0)
            {
                var size = PlayfieldSize;
                var from = new Vector2(
                    size.X * (0.15f + (float)_rng.NextDouble() * 0.7f),
                    size.Y * (0.15f + (float)_rng.NextDouble() * 0.7f));
                _benchEmitter.Emit(new PatternSpec
                {
                    Kind = BulletPatternKind.Ring,
                    From = from,
                    Count = need,
                    Speed = 180f,
                    SpinDeg = (float)_rng.NextDouble() * 360f,
                });
            }
        }
    }

    private void SpawnEnemy()
    {
        var size = PlayfieldSize;
        var enemy = new Enemy.Enemy { Name = "StressEnemy" };
        AddChild(enemy);
        enemy.Position = new Vector2(
            size.X * (0.1f + (float)_rng.NextDouble() * 0.8f),
            size.Y * (0.1f + (float)_rng.NextDouble() * 0.8f));
        enemy.SetTarget(_target);
        _enemies.Add(enemy);
    }

    /// 弹幕层里当前可见的子弹总数（含敌机自吐的弹；一次性扫描，供基准收尾统计）。
    public int TotalVisibleBullets
    {
        get
        {
            int c = 0;
            if (_layer == null) return c;
            foreach (Node n in _layer.GetChildren())
                if (n is Bullet.Bullet b && b.Visible) c++;
            return c;
        }
    }

    /// <summary>清空当前负载（敌机 + 弹幕层 + 受控池），下一预设/下一档压力复用。
    /// 关键纪律：受控池的子弹只能 <see cref="Bullet.Bullet.RecycleSelf"/> 回池，
    /// 绝不能直接 QueueFree——否则池里留下已释放对象，下次 Spawn 抛 ObjectDisposedException。</summary>
    public void Clear()
    {
        // 1) 敌机：整棵子树随 QueueFree 销毁（含其自带发射器与池）；它们 reparent 到弹幕层的弹会变孤儿，留到第 3 步清。
        foreach (var e in _enemies)
            if (GodotObject.IsInstanceValid(e)) e.QueueFree();
        _enemies.Clear();

        // 2) 受控池的弹：回池（Visible=false，仍在层内，等下一预设复用）。
        if (_benchEmitter != null)
        {
            var active = _benchEmitter.Pool.Active;
            for (int i = active.Count - 1; i >= 0; i--)
                active[i].RecycleSelf();
        }

        // 3) 弹幕层残余：只清非受控池的孤儿弹（敌机自吐后随敌机销毁残留）与非弹节点。
        //    受控池的弹已回池（Recycle 目标 == bench 池），必须跳过，不能再 QueueFree。
        if (_layer != null)
            foreach (Node c in _layer.GetChildren())
            {
                if (c is not Bullet.Bullet b) { c.QueueFree(); continue; }
                if (!ReferenceEquals(b.Recycle?.Target, _benchEmitter?.Pool)) c.QueueFree();
            }
    }
}
