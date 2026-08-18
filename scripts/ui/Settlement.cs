using System;
using Godot;
using MagicThunder.Player;

namespace MagicThunder.UI;

/// <summary>
/// 结算界面（MVP 垂直切片）：胜利 = 升级三选一；失败 = 重新挑战。
/// ProcessMode=Always：结算时 GetTree().Paused=true，本层仍可交互（按钮可点）。
/// 界面元素全部代码构建（与 Hud 同风格，不依赖美术资源）。
/// </summary>
public partial class Settlement : CanvasLayer
{
    private Control? _root;
    private VBoxContainer? _box;

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;

        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        _box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _box.AddThemeConstantOverride("separation", 14);
        _root.AddChild(_box);

        Hide();
    }

    public new void Show() => _root?.Show();
    public new void Hide() => _root?.Hide();

    /// 胜利结算：战绩 + 三选一升级按钮，点击回调 onPick(index)。
    public void ShowVictory(int score, int kills, (UpgradeType type, string desc)[] choices, Action<int> onPick)
    {
        _root!.Show();
        ClearBox();

        AddTitle("胜利！");
        AddStats($"分数 {score}  |  击杀 {kills}  |  选择一项强化");
        AddSpacer();

        for (int i = 0; i < choices.Length; i++)
        {
            int idx = i;
            AddButton(choices[i].desc, () => onPick(idx));
        }

        AddHint("点击强化，进入下一关（敌人更强，分数更高）");
    }

    /// 失败结算（战败 CG 的收尾）：DEFEATED + 战斗统计 + "她还活着。" + 重新挑战。
    /// timeSec 战斗时长（秒，格式化为 mm:ss）；weaponLevel 武器等级（当前成长表征）。
    public void ShowDefeat(int score, int kills, float timeSec, int weaponLevel, Action onRetry)
    {
        _root!.Show();
        ClearBox();

        AddTitle("DEFEATED");
        int min = (int)(timeSec / 60f);
        int sec = (int)(timeSec % 60f);
        AddStats($"战斗时间 {min:00}:{sec:00}  |  击败敌人 {kills}  |  武器等级 Lv{weaponLevel}");
        AddStats($"获得星之残片 {score}");
        AddSpacer();
        var epilogue = new Label { Text = "「她还活着。」" };
        epilogue.AddThemeFontSizeOverride("font_size", 18);
        epilogue.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        epilogue.Modulate = new Color(1f, 0.85f, 0.6f);
        _box!.AddChild(epilogue);
        AddSpacer();
        AddButton("再次挑战（保留已强化）", () => onRetry());
    }

    private void ClearBox()
    {
        foreach (Node c in _box!.GetChildren())
            c.QueueFree();
    }

    private void AddTitle(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 40);
        l.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _box!.AddChild(l);
    }

    private void AddStats(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 22);
        l.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _box!.AddChild(l);
    }

    private void AddButton(string text, Action onClick)
    {
        var b = new Button { Text = text };
        b.CustomMinimumSize = new Vector2(380, 44);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Pressed += () => onClick();
        _box!.AddChild(b);
    }

    private void AddHint(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 14);
        l.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _box!.AddChild(l);
    }

    private void AddSpacer()
    {
        _box!.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
    }
}
