using System.Collections.Generic;
using Godot;
using MagicThunder.Autoload;
using MagicThunder.Data;

namespace MagicThunder.Wave;

/// <summary>
/// 波次管理器（MVP 垂直切片）：按配置逐波生成小怪，清空后推进；全部波次清空派发 all_waves_cleared（由 Main 生成 Boss）。
/// 敌人数值是内容数据（每波数量/类型组合），敌人生成后数值走 enemy_*.tres（数据驱动）。
/// 事件：wave_cleared（当前波清空）；all_waves_cleared（所有波清空，Boss 出场）。
/// </summary>
public partial class WaveManager : Node
{
    /// 每波配置（内容数据；后续下沉为 .tres 关卡表）：
    /// count = 该波敌人数；kinds = 该波类型池（生成时等权随机取，重复项 = 权重，便于教学强调）。
    /// 主题（对齐 MVP 计划 §7）：W1 教学 → W2 走位 → W3 弹幕 → W4 混合压力 → W5 爆发。
    public static readonly (int count, EnemyKind[] kinds)[] Waves = new (int, EnemyKind[])[]
    {
        // W1 教学：纯基础飞行怪（熟悉操作）
        (3,  new[] { EnemyKind.Chaser }),
        // W2 走位：引入自爆怪（2:1 基础怪，逼走位）
        (4,  new[] { EnemyKind.Chaser, EnemyKind.Chaser, EnemyKind.Bomber }),
        // W3 弹幕：狙击 + 弹幕怪（第一次真正弹幕压力）
        (5,  new[] { EnemyKind.Chaser, EnemyKind.Sniper, EnemyKind.Sprayer }),
        // W4 混合：数量增加，走位 + 弹幕一起上
        (6,  new[] { EnemyKind.Chaser, EnemyKind.Chaser, EnemyKind.Bomber, EnemyKind.Sprayer }),
        // W5 爆发：全类型大型敌群
        (8, new[] { EnemyKind.Chaser, EnemyKind.Chaser, EnemyKind.Bomber, EnemyKind.Sniper, EnemyKind.Sprayer }),
    };

    private const float SpawnInterval = 0.6f;
    private const float TopMargin = 60f;

    private readonly List<Enemy.Enemy> _alive = new();
    private Node2D? _target;
    private int _waveIndex = -1;
    private int _spawned;
    private float _spawnTimer;
    private Vector2 _size;

    public int CurrentWave => _waveIndex + 1;
    public int TotalWaves => Waves.Length;
    public bool AllWavesCleared { get; private set; }

    /// 当前存活敌人（供 Main 自写距离碰撞检测遍历；返回内部列表只读视图，勿在遍历中修改）。
    public IReadOnlyList<Enemy.Enemy> Alive => _alive;

    public override void _Ready()
    {
        _size = GameManager.I!.PlayfieldSize;
    }

    public void SetTarget(Node2D? target) => _target = target;

    /// 从第 1 波开始（重开一关时由 Main 调用）。
    public void Begin()
    {
        _waveIndex = -1;
        _spawned = 0;
        AllWavesCleared = false;
        Advance();
    }

    private void Advance()
    {
        _waveIndex++;
        if (_waveIndex >= Waves.Length)
        {
            AllWavesCleared = true;
            EventBus.I.Dispatch("all_waves_cleared");
            return;
        }
        _spawned = 0;
        _spawnTimer = 0f;
        GD.Print($"[WaveManager] 波次 {CurrentWave}/{TotalWaves} 开始");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (AllWavesCleared) return;
        float dt = (float)delta;

        // 清理已阵亡的敌人（QueueFree 后下一帧不 in tree）
        _alive.RemoveAll(e => !IsInstanceValid(e) || !e.IsInsideTree());

        // 生成本波敌人
        if (_spawned < Waves[_waveIndex].count)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = SpawnInterval;
                SpawnEnemy();
                _spawned++;
            }
        }
        else if (_alive.Count == 0)
        {
            EventBus.I.Dispatch("wave_cleared");
            Advance();
        }
    }

    private void SpawnEnemy()
    {
        // 顶部随机横向入场；类型从本波类型池等权随机（重复项 = 权重，W1 纯 Chaser）
        float x = _size.X * 0.2f + GD.Randf() * _size.X * 0.6f;
        var kinds = Waves[_waveIndex].kinds;
        var enemy = new Enemy.Enemy { Name = "Enemy", Kind = kinds[GD.Randi() % (uint)kinds.Length] };
        AddChild(enemy);
        enemy.Position = new Vector2(x, -TopMargin);
        enemy.SetTarget(_target);
        _alive.Add(enemy);
    }
}
