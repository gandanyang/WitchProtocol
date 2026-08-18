using Godot;

namespace MagicThunder.Effects;

/// <summary>
/// 一次性演出特效：放大 + 淡出后自毁。
/// 挂 Effects 容器（世界坐标）；用于受击闪白 / 击杀爆炸等最小视觉反馈。
/// 用法：new SpawnEffect { Texture = tex } → AddChild → Position = 目标点。
/// </summary>
public partial class SpawnEffect : Sprite2D
{
    public float Lifetime = 0.35f;
    public float StartScale = 0.6f;
    public float EndScale = 1.4f;

    private float _age;

    public override void _Ready()
    {
        Scale = Vector2.One * StartScale;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        float t = Mathf.Clamp(_age / Lifetime, 0f, 1f);
        Scale = Vector2.One * Mathf.Lerp(StartScale, EndScale, t);
        Modulate = new Color(1f, 1f, 1f, 1f - t);
        if (t >= 1f) QueueFree();
    }
}
