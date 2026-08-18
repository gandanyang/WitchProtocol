using System.Collections.Generic;
using Godot;
using MagicThunder.Autoload;

namespace MagicThunder.Wave;

/// <summary>
/// 波次管理器（MVP 垂直切片）：按配置逐波生成小怪，清空后推进；全部波次清空派发 all_waves_cleared（由 Main 生成 Boss）。
/// 敌人数值是内容数据（每波数量/生成间隔），敌人生成后数值走 EnemyConfig.tres（数据驱动）。
/// 事件：wave_cleared（当前波清空）；all_waves_cleared（所有波清空，Boss 出场）。
/// </summary>
public partial class WaveManager : Node
{
    /// 各波敌人数（内容数据；后续下沉为 .tres 关卡表）。
    public static readonly int[] WaveSizes = { 4, 6, 8 };

    private const float SpawnInterval = 0.6f;
    private const float TopMargin = 60f;

    private readonly List<Enemy.Enemy> _alive = new();
    private Node2D? _target;
    private int _waveIndex = -1;
    private int _spawned;
    private float _spawnTimer;
    private Vector2 _size;

    public int CurrentWave => _waveIndex + 1;
    public int TotalWaves => WaveSizes.Length;
    public bool AllWavesCleared { get; private set; }

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
        if (_waveIndex >= WaveSizes.Length)
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
        if (_spawned < WaveSizes[_waveIndex])
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
        // 顶部随机横向入场
        float x = _size.X * 0.2f + GD.Randf() * _size.X * 0.6f;
        var enemy = new Enemy.Enemy { Name = "Enemy" };
        AddChild(enemy);
        enemy.Position = new Vector2(x, -TopMargin);
        enemy.SetTarget(_target);
        _alive.Add(enemy);
    }
}
