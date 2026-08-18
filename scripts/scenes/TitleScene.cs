using Godot;
using MagicThunder.Autoload;

namespace MagicThunder.Scenes;

/// <summary>
/// 主菜单（MVP 最小版）：标题 + 开始游戏。
/// 战败 CG 菜单的「回到主菜单」入口（ChangeSceneToFile 切回本场景）。
/// 界面全部代码构建（与 Hud/Settlement 同风格）。背景复用深空星空素材。
/// </summary>
public partial class TitleScene : Control
{
    private const string BackgroundPath = "res://assets/backgrounds/bg_deep_space.jpg";

    public override void _Ready()
    {
        // 背景：深空星空全屏覆盖
        var bgTex = ResourceLoader.Load<Texture2D>(BackgroundPath);
        if (bgTex != null)
        {
            var bg = new TextureRect
            {
                Texture = bgTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            };
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(bg);
        }

        // 暗化（保证文字可读）
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.4f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        // 标题
        var title = new Label { Text = "魔女协议" };
        title.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        title.OffsetTop = 130;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 76);
        AddChild(title);

        var sub = new Label { Text = "WITCH PROTOCOL" };
        sub.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        sub.OffsetTop = 215;
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AddThemeFontSizeOverride("font_size", 20);
        sub.Modulate = new Color(0.75f, 0.8f, 1f);
        AddChild(sub);

        // 开始游戏
        var btn = new Button { Text = "开始游戏" };
        btn.SetAnchorsPreset(Control.LayoutPreset.Center);
        btn.OffsetLeft = -130;
        btn.OffsetTop = 70;
        btn.OffsetRight = 130;
        btn.OffsetBottom = 116;
        btn.AddThemeFontSizeOverride("font_size", 24);
        btn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        AddChild(btn);

        var hint = new Label { Text = "WASD / 方向键移动 · Shift 低速 · 自动射击" };
        hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        hint.OffsetBottom = -60;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        AddChild(hint);
    }
}
