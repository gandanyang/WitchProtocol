using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Player;
using MagicThunder.UI;

namespace MagicThunder.Scenes;

/// <summary>
/// M0-2 最小可运行场景装配：一个玩家 + 一个敌人，弹幕对射，命中与胜负结算闭环。
/// 验证「移动/开火 → 敌人吐弹 → 命中 → 结算」最小战斗循环。
/// 正式版扩展：多波敌群 / Boss / 分数 / 掉落（见 docs/开发约束与架构入口.md）。
/// </summary>
public partial class Main : Node
{
    private const string ThisScenePath = "res://scenes/Main.tscn";
    private const int StartLives = 3;

    private PlayerController? _player;
    private MagicThunder.Enemy.Enemy? _enemy;
    private Hud? _hud;
    private int _lives = StartLives;
    private bool _over;

    public override void _Ready()
    {
        GD.Print("== Main 场景装配：玩家 + 敌人 + 弹幕对射 ==");
        var center = GameManager.I!.PlayfieldSize / 2f;

        // 玩家弹发射器挂在战场原点（世界坐标），避免子弹随玩家移动
        var playerEmitter = new BulletEmitter { Name = "PlayerEmitter" };
        AddChild(playerEmitter);

        _player = new PlayerController { Name = "Player" };
        AddChild(_player);
        _player.Position = center + new Vector2(0, 120);
        _player.BindEmitter(playerEmitter);

        _enemy = new MagicThunder.Enemy.Enemy { Name = "Enemy" };
        AddChild(_enemy);
        _enemy.Position = center + new Vector2(0, -140);
        _enemy.SetTarget(_player);

        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        RefreshHud();

        EventBus.I.World += OnWorldEvent;
    }

    public override void _ExitTree()
    {
        EventBus.I.World -= OnWorldEvent;
    }

    private void OnWorldEvent(string name, GodotObject? _)
    {
        switch (name)
        {
            case "player_hit":
                if (_over) return;
                _lives--;
                if (_lives <= 0) EndGame("游戏结束");
                else RefreshHud();
                break;
            case "enemy_killed":
                if (_over) return;
                EndGame("敌人击破，胜利！");
                break;
        }
    }

    private void EndGame(string result)
    {
        _over = true;
        _hud?.Say($"{result}  |  R 重新开始");
    }

    private void RefreshHud()
    {
        _hud?.Say($"生命 {_lives}  |  Z 射击 · Shift 低速 · WASD 移动");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // 结算后按 R 重开本场景
        if (_over && e is InputEventKey { Pressed: true, Keycode: Key.R })
            GameManager.I!.ChangeScene(ThisScenePath);
    }
}
