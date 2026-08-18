using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Player;
using MagicThunder.UI;

namespace MagicThunder.Scenes;

/// <summary>
/// 主启动场景：地基自检 + 最小可视化占位（一个玩家矩形 + 一条扇面弹）。
/// 正式玩法从「弹幕沙盒」开始，本场景后续会被真正的战斗场景替换。
/// </summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        GD.Print("== Witch Protocol · 魔女协议 — 地基自检 ==");
        var gm = GameManager.I;
        var bus = EventBus.I;
        var save = SaveSystem.I;
        var dev = DevTestHub.I;
        GD.Print($"[✓] GameManager={gm != null}, Playfield={gm?.PlayfieldSize}");
        GD.Print($"[✓] EventBus={bus != null}");
        GD.Print($"[✓] SaveSystem={save != null}, version={SaveSystem.SaveVersion}");
        GD.Print($"[✓] DevTestHub={dev != null}, probes={dev?.ProbeIds.Count}");

        var center = gm!.PlayfieldSize / 2f;

        // 玩家占位（矩形，WASD/方向移动，Shift 低速）
        var player = new PlayerController();
        AddChild(player);
        player.Position = center + new Vector2(0, 90);

        // 演示弹幕发射器（一条环绕玩家的扇面弹，便于肉眼确认对象池链路）
        var emitter = new BulletEmitter { Name = "DemoEmitter" };
        AddChild(emitter);
        emitter.Emit(new PatternSpec
        {
            Kind = BulletPatternKind.Spread,
            From = center,
            Count = 24,
            Speed = 140f,
        });

        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Say("Witch Protocol · 魔女协议（地基）");
    }
}