using Godot;

namespace MagicThunder.UI;

/// <summary>
/// 地基版 HUD：左上主信息（等级 + 经验条 + 血/盾/武器/波次/分数）+ 右上性能监测（开发期看掉帧）。
/// 性能面板（帧率优化专项行动 阶段0）：FPS + 脚本/物理耗时 + 节点/物理体/draw call 数，
/// 用于把「弹幕多时掉帧」定位到 CPU / 物理 / 绘制哪一层。0.5s 刷新，避免每帧改文本。
/// 移动端（OS.HasFeature("mobile")）隐藏性能面板，只留玩家 HUD。
/// M2 局内成长：经验条（UpdateExp）+ 等级标签，让「吃星之残片 → 升级」的正循环看得见。
/// 复杂 UI（血条/炸弹/对话）后续在此上扩展。
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label? _label;
    private Label? _levelLabel;
    private ProgressBar? _expBar;
    private Label? _fpsLabel;
    private float _fpsTimer;

    public override void _Ready()
    {
        // 等级标签 + 经验条（M2 局内成长反馈）
        _levelLabel = new Label { Position = new Vector2(16, 10), Text = "Lv.1" };
        _levelLabel.AddThemeFontSizeOverride("font_size", 18);
        _levelLabel.Modulate = new Color(0.9f, 0.8f, 1.0f); // 淡紫，呼应星之残片主题
        AddChild(_levelLabel);

        _expBar = new ProgressBar { Position = new Vector2(16, 38), Size = new Vector2(180, 8) };
        _expBar.MinValue = 0;
        _expBar.MaxValue = 100;
        _expBar.Value = 0;
        _expBar.ShowPercentage = false;
        var expBg = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.5f) };
        var expFill = new StyleBoxFlat { BgColor = new Color(0.72f, 0.55f, 0.95f) };
        _expBar.AddThemeStyleboxOverride("background", expBg);
        _expBar.AddThemeStyleboxOverride("fill", expFill);
        AddChild(_expBar);

        _label = new Label { Position = new Vector2(16, 52) };
        _label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_label);

        // 右上角性能监测（每 0.5s 刷新，见 _Process）；仅桌面/开发期显示，移动端发布隐藏
        if (!OS.HasFeature("mobile"))
        {
            _fpsLabel = new Label { Text = "FPS --" };
            _fpsLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            _fpsLabel.OffsetLeft = -300;
            _fpsLabel.OffsetTop = 12;
            _fpsLabel.OffsetRight = -16;
            _fpsLabel.OffsetBottom = 120;
            _fpsLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _fpsLabel.AddThemeFontSizeOverride("font_size", 14);
            AddChild(_fpsLabel);
        }
    }

    public void Say(string text)
    {
        if (_label != null) _label.Text = text;
    }

    /// <summary>M2 局内成长：刷新等级标签 + 经验条进度（吃星之残片后调用）。</summary>
    public void UpdateExp(int level, int currentExp, int expToNext)
    {
        if (_levelLabel != null) _levelLabel.Text = $"Lv.{level}";
        if (_expBar != null)
        {
            _expBar.MaxValue = Mathf.Max(expToNext, 1);
            _expBar.Value = Mathf.Clamp(currentExp, 0, _expBar.MaxValue);
        }
    }

    public override void _Process(double delta)
    {
        // 性能面板每 0.5s 刷新一次，避免每帧改文本
        _fpsTimer += (float)delta;
        if (_fpsTimer >= 0.5f)
        {
            _fpsTimer = 0f;
            if (_fpsLabel != null)
            {
                // 耗时单位：秒 → ms。draw call 在部分渲染器下不可用（返回 -1），照实显示。
                float cpuMs = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
                float phyMs = (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f;
                long nodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
                long phys = (long)Performance.GetMonitor(Performance.Monitor.Physics2DActiveObjects);
                long draw = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
                _fpsLabel.Text = $"FPS {Engine.GetFramesPerSecond():F0}\nCPU {cpuMs:F1}ms  Phy {phyMs:F1}ms\nNode {nodes}  Phys {phys}\nDraw {draw}";
            }
        }
    }
}