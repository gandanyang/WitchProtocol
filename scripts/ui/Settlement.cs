using System;
using Godot;

namespace MagicThunder.UI;

/// <summary>
/// 结算界面（MVP 垂直切片）：胜利 = 成绩结算 + 再次挑战；失败 = 战败 CG + 重新挑战。
/// ProcessMode=Always：结算时 GetTree().Paused=true，本层仍可交互（按钮可点）。
/// 界面元素全部代码构建（与 Hud 同风格，不依赖美术资源）。
/// </summary>
public partial class Settlement : CanvasLayer
{
    private Control? _root;
    private VBoxContainer? _box;
    private TextureRect? _cg; // 全屏战败 CG（结算背景）

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

    /// 胜利结算：成绩 + 「再次挑战」（新一局从 Lv1 开始）。
    /// M2 语义调整：升级已改在局内即时发生，胜利不再给升级选择。
    public void ShowVictory(int score, int kills, int level, Action onRetry)
    {
        _root!.Show();
        if (_cg != null) _cg.Visible = false; // 胜利不显示战败 CG
        ClearBox();

        AddTitle("胜利！");
        AddStats($"分数 {score}  |  击杀 {kills}  |  达到等级 Lv{level}");
        AddHint("星之残片已收入星图");
        AddSpacer();
        AddButton("再次挑战", () => onRetry());
    }

    /// 失败结算（战败 CG，用户红线）：全屏战败 CG 图 + DEFEATED + 统计 + 「她还活着。」+ 菜单（继续/回到主菜单）。
    /// cg 战败图（ComfyUI 产出）；timeSec 战斗时长（mm:ss）；weaponLevel 武器等级。
    /// onRetry 继续（重开本关保留强化）；onMenu 回到主菜单。
    public void ShowDefeat(Texture2D cg, int score, int kills, float timeSec, int weaponLevel, Action onRetry, Action onMenu)
    {
        _root!.Show();

        // 全屏战败 CG（KeepAspectCovered，占满整个屏幕）
        if (_cg == null)
        {
            _cg = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            };
            _cg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(_cg);
            _cg.MoveToFront(); // CG 垫底在文字下、暗化之上
        }
        _cg.Texture = cg;
        _cg.Visible = true;

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
        AddButton("继续", () => onRetry());
        AddButton("回到主菜单", () => onMenu());
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
        // 触屏适配：按钮不超出屏宽 72%（小屏不溢出），高度加大便于手指点按
        float maxW = GetViewport().GetVisibleRect().Size.X * 0.72f;
        b.CustomMinimumSize = new Vector2(Mathf.Clamp(maxW, 220f, 380f), 56);
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        b.AddThemeFontSizeOverride("font_size", 22);
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
