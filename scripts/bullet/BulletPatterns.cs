using Godot;

namespace MagicThunder.Bullet;

/// <summary>
/// Pattern 数学：纯函数，返回初始速度向量数组。不持有状态、不依赖场景，可直接单测。
/// 新增弹型（折线/抛物线/追踪等）加在这里，是弹幕手感的核心算法层。
/// </summary>
public static class BulletPatterns
{
    /// 瞄准弹：朝目标直线。
    public static Vector2 Aimed(Vector2 from, Vector2 target, float speed)
        => (from.DistanceTo(target) > 0.0001f ? (target - from).Normalized() * speed : Vector2.Zero);

    /// 扇面弹：以 baseAngleDeg 为中心、向两侧展开 count 发、间距 gapDeg。
    public static Vector2[] Spread(Vector2 center, float baseAngleDeg, int count, float gapDeg, float speed)
    {
        var list = new Vector2[count];
        float total = (count - 1) * gapDeg;
        float start = baseAngleDeg - total / 2f;
        for (int i = 0; i < count; i++)
            list[i] = Vector2.FromAngle(Mathf.DegToRad(start + i * gapDeg)) * speed;
        return list;
    }

    /// 圆环弹：等分圆；ringIndex 配合 spinDeg 让多层圆环错位旋转。
    public static Vector2[] Ring(Vector2 center, int count, float speed, float spinDeg, int ringIndex = 0)
    {
        var list = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            float deg = (360f / count) * i + ringIndex * spinDeg;
            list[i] = Vector2.FromAngle(Mathf.DegToRad(deg)) * speed;
        }
        return list;
    }

    /// 螺旋弹：每发相对上一发多转 turnDeg 的发射角。
    public static Vector2[] Spiral(Vector2 center, int bullets, float speed, float turnDeg, float baseAngleDeg = 0f)
    {
        var list = new Vector2[bullets];
        for (int i = 0; i < bullets; i++)
        {
            float deg = baseAngleDeg + i * turnDeg;
            list[i] = Vector2.FromAngle(Mathf.DegToRad(deg)) * speed;
        }
        return list;
    }
}