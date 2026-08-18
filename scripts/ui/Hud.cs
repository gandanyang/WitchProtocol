using Godot;

namespace MagicThunder.UI;

/// <summary>地基版 HUD：一个可追加文本的 Label。复杂 UI（血条/炸弹/对话）后续在此上扩展。</summary>
public partial class Hud : CanvasLayer
{
    private Label? _label;

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(16, 12) };
        _label.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_label);
    }

    public void Say(string text)
    {
        if (_label != null) _label.Text = text;
    }
}