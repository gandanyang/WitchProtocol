using Godot;
using MagicThunder.Autoload;
using MagicThunder.Bullet;
using MagicThunder.Player;
using MagicThunder.UI;

namespace MagicThunder.Scenes;

/// <summary>
/// 开发 / 自检场景（Dev / Diagnostic Scene）：地基自检 + 最小可视化占位。
/// 不是正式游戏入口（正式主场景是 scenes/Main.tscn，见 docs/开发约束与架构入口.md）。
/// 用途：基础设施诊断、孤立系统测试；禁止承载正式玩法逻辑。
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