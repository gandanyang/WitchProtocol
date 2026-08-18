using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Player;
using MagicThunder.Score;
using MagicThunder.UI;

namespace MagicThunder.Scenes;

/// <summary>
/// MVP 垂直切片主场景装配：玩家（黎歌）→ 3 波小怪 → Boss → 胜利/死亡结算（本关结束统一升级）。
/// 完整闭环：波次推进 / 命中加分 / Boss 战 / 升级三选一 / 重新挑战。业务逻辑在玩法层，这里只做装配与事件调度。
/// </summary>
public partial class Main : Node
{
    private PlayerController? _player;
    private Wave.WaveManager? _waves;
    private Enemy.Boss? _boss;
    private Hud? _hud;
    private Settlement? _settlement;
    private (UpgradeType type, string desc)[]? _pendingChoices;

    private int _score;
    private int _kills;
    private bool _over;
    private readonly Random _rng = new();

    public override void _Ready()
    {
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _settlement = new Settlement { Name = "Settlement" };
        AddChild(_settlement);

        SetupStage();
        EventBus.I.World += OnWorldEvent;
    }

    public override void _ExitTree()
    {
        EventBus.I.World -= OnWorldEvent;
    }

    /// 开新一关：重置波次与战绩、回满血，保留玩家升级（武器/护盾等）。
    private void SetupStage()
    {
        var center = GameManager.I!.PlayfieldSize / 2f;

        // P0-1（敌弹世界坐标）：战场弹幕层，挂战场原点。敌弹 reparent 到此层，
        // LocalPosition ≈ 世界坐标，敌人移动/死亡不带动弹幕。只建一次。
        if (GetNodeOrNull("EnemyBullets") == null)
        {
            var enemyBullets = new Node2D { Name = "EnemyBullets", Position = Vector2.Zero };
            AddChild(enemyBullets);
        }

        // 玩家与发射器只建一次，跨关保留升级
        if (_player == null)
        {
            var playerEmitter = new BulletEmitter { Name = "PlayerEmitter" };
            AddChild(playerEmitter);

            _player = new PlayerController { Name = "Player" };
            AddChild(_player);
            _player.Position = center + new Vector2(0, 120);
            _player.BindEmitter(playerEmitter);
        }
        _player.RestoreForStage();

        _score = 0;
        _kills = 0;
        _over = false;
        GetTree().Paused = false;
        _settlement!.Hide();

        if (_waves != null) { _waves.QueueFree(); _waves = null; }
        _waves = new Wave.WaveManager { Name = "Waves" };
        AddChild(_waves);
        _waves.SetTarget(_player);
        _waves.Begin();

        if (_boss != null) { _boss.QueueFree(); _boss = null; }
        RefreshHud();
    }

    private void OnWorldEvent(string name, GodotObject? payload)
    {
        if (_over) return;
        switch (name)
        {
            case "player_hit":
                if (_player!.OnHit())
                {
                    RefreshHud();
                    if (_player.IsDead) EndRun(false);
                }
                break;

            case "enemy_killed":
                if (payload is Enemy.Enemy e) _score += e.KillScore;
                _kills++;
                RefreshHud();
                break;

            case "wave_cleared":
                // 当前波刚清空（Advance 尚未推进），CurrentWave-1 即本波下标
                _score += ScoreCalc.WaveClearBonus(_waves!.CurrentWave - 1);
                RefreshHud();
                break;

            case "all_waves_cleared":
                SpawnBoss();
                break;

            case "boss_killed":
                _score += ScoreCalc.BossBonus;
                RefreshHud();
                EndRun(true);
                break;
        }
    }

    private void SpawnBoss()
    {
        var center = GameManager.I!.PlayfieldSize / 2f;
        _boss = new Enemy.Boss { Name = "Boss" };
        AddChild(_boss);
        _boss.Position = center + new Vector2(0, -140);
        _boss.SetTarget(_player);
        RefreshHud();
    }

    private void EndRun(bool victory)
    {
        _over = true;
        GetTree().Paused = true;
        if (victory)
        {
            _pendingChoices = BuildChoices();
            _settlement!.ShowVictory(_score, _kills, _pendingChoices, OnPickUpgrade);
        }
        else
        {
            _pendingChoices = null;
            _settlement!.ShowDefeat(_score, _kills, SetupStage);
        }
    }

    /// 从升级池随机取 3 个（武器满级时不再出现武器强化）。
    private (UpgradeType type, string desc)[] BuildChoices()
    {
        var pool = new List<(UpgradeType, string)>();
        if (_player!.WeaponLevel < PlayerController.MaxWeaponLevel)
            pool.Add((UpgradeType.Weapon, Describe(UpgradeType.Weapon)));
        pool.Add((UpgradeType.RapidFire, Describe(UpgradeType.RapidFire)));
        pool.Add((UpgradeType.BulletSpeed, Describe(UpgradeType.BulletSpeed)));
        pool.Add((UpgradeType.MaxHp, Describe(UpgradeType.MaxHp)));
        pool.Add((UpgradeType.Shield, Describe(UpgradeType.Shield)));

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(3).ToArray();
    }

    private string Describe(UpgradeType t) => t switch
    {
        UpgradeType.Weapon => $"武器强化（Lv{_player!.WeaponLevel} → {_player.WeaponLevel + 1}：双发/三向）",
        UpgradeType.RapidFire => "连射提升（射速 +33%）",
        UpgradeType.BulletSpeed => "弹速提升（子弹速度 +20%）",
        UpgradeType.MaxHp => "生命上限 +1（并回 1 血）",
        UpgradeType.Shield => "获得护盾 ×1（抵挡一次伤害）",
        _ => "",
    };

    private void OnPickUpgrade(int index)
    {
        if (_pendingChoices == null || index < 0 || index >= _pendingChoices.Length) return;
        _player!.ApplyUpgrade(_pendingChoices[index].type);
        _pendingChoices = null;
        SetupStage();
    }

    private void RefreshHud()
    {
        if (_hud == null || _player == null || _waves == null) return;
        string stage = _boss != null && IsInstanceValid(_boss) && _boss.IsInsideTree()
            ? "BOSS 战"
            : $"波次 {_waves.CurrentWave}/{_waves.TotalWaves}";
        _hud.Say($"生命 {_player.Hp}/{_player.MaxHp} 盾{_player.Shield} 武器Lv{_player.WeaponLevel} | {stage} | 分数 {_score}");
    }
}
