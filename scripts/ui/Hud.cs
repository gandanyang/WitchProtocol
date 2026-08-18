using Godot;

namespace MagicThunder.UI;

/// <summary>
/// 地基版 HUD：左上主信息（血/盾/武器/波次/分数）+ 右上 FPS 帧数监测（开发期看掉帧）。
/// 复杂 UI（血条/炸弹/对话）后续在此上扩展。
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label? _label;
    private Label? _fpsLabel;
    private float _fpsTimer;

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(16, 12) };
        _label.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_label);

        // 右上角 FPS 帧数监测（每 0.5s 刷新，见 _Process）
        _fpsLabel = new Label { Text = "FPS --" };
        _fpsLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _fpsLabel.OffsetLeft = -170;
        _fpsLabel.OffsetTop = 12;
        _fpsLabel.OffsetRight = -16;
        _fpsLabel.OffsetBottom = 44;
        _fpsLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _fpsLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_fpsLabel);
    }

    public void Say(string text)
    {
        if (_label != null) _label.Text = text;
    }

    public override void _Process(double delta)
    {
        // FPS 每 0.5s 刷新一次，避免每帧改文本
        _fpsTimer += (float)delta;
        if (_fpsTimer >= 0.5f)
        {
            _fpsTimer = 0f;
            if (_fpsLabel != null) _fpsLabel.Text = $"FPS {Engine.GetFramesPerSecond():F0}";
        }
    }
}