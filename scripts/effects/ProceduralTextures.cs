using Godot;

namespace MagicThunder.Effects;

/// <summary>
/// 程序化共享纹理（帧率优化专项）：敌弹/敌机用同一张程序生成贴图 + Modulate 染色，
/// 替代逐节点 _Draw 自绘 DrawCircle——自绘无法被 Godot 2D 合批，弹幕一多 draw call 爆炸。
/// 共享同纹理的 Sprite2D 可合批（modulate 走顶点色，不打断合批）。
/// 只生成一次，全局缓存；后续美术替换贴图时，把这里换成 .tres 资源即可。
/// </summary>
public static class ProceduralTextures
{
    private static Texture2D? _dot;

    /// 白色实心圆盘（敌弹/敌机共用，Modulate 染色）。32×32 居中圆，边缘 1px 抗锯齿留白。
    public static Texture2D Dot()
    {
        if (_dot != null) return _dot;
        const int size = 32;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f;
        float r = size / 2f - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                img.SetPixel(x, y, Mathf.Sqrt(dx * dx + dy * dy) <= r ? Colors.White : Colors.Transparent);
            }
        }
        _dot = ImageTexture.CreateFromImage(img);
        return _dot;
    }
}
