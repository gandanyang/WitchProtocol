using Godot;
using MagicThunder.Autoload;

namespace MagicThunder.Effects;

/// <summary>
/// 全局战斗反馈系统（静态工具类）：屏幕震动 / HitStop 顿帧 / 镜头演出（缩放）。
///  - 宿主：懒加载挂到 GameManager（autoload）下——GameManager 下挂一个 Camera2D（战场中心）
///    成为当前相机，Main 无需自行装配相机；与并行 Agent 的装配改动隔离（AI_GUARDRAIL）。
///  - 震屏：Camera2D.Offset 随机衰减抖动，不影响战场逻辑坐标。
///  - HitStop：Engine.TimeScale 短时间降低（打击感），由内部 Driver 计时恢复。
///  - ZoomTo：供战败演出（镜头拉远）等使用，按时长线性缩放。
/// </summary>
public static partial class FeedbackSystem
{
    private static Camera2D? _camera;
    private static Driver? _driver;
    private static bool _initialized;

    private static float _shakeT, _shakeDur, _shakePower;
    private static float _stopT;
    private static float _zoomT, _zoomDur, _zoomFrom, _zoomTo;

    /// 确保相机 + 驱动节点挂在 GameManager 下（战场中心，Zoom=1）。
    public static bool Ensure()
    {
        if (_initialized) return _camera != null;
        _initialized = true;
        if (GameManager.I == null) return false;

        var size = GameManager.I.PlayfieldSize;
        _camera = new Camera2D
        {
            Name = "BattleCamera",
            Position = size / 2f,
            Zoom = Vector2.One,
            Enabled = true,
        };
        GameManager.I.AddChild(_camera);

        _driver = new Driver();
        GameManager.I.AddChild(_driver);
        return true;
    }

    /// 屏幕震动：duration 秒内 Camera2D.Offset 随机衰减抖动。
    public static void ShakeScreen(float duration, float power)
    {
        if (!Ensure()) return;
        _shakeT = _shakeDur = Mathf.Max(0.01f, duration);
        _shakePower = power;
    }

    /// HitStop 顿帧：TimeScale 短时间降低，恢复 1.0。弹幕打击感关键。
    public static void HitStop(float duration, float scale = 0.05f)
    {
        if (!Ensure()) return;
        Engine.TimeScale = scale;
        _stopT = Mathf.Max(0.01f, duration);
    }

    /// 镜头线性缩放到 zoom（供演出用，如战败镜头拉远）。
    public static void ZoomTo(float zoom, float duration)
    {
        if (!Ensure() || _camera == null) return;
        _zoomFrom = _camera.Zoom.X;
        _zoomTo = Mathf.Max(0.5f, zoom);
        _zoomDur = Mathf.Max(0.01f, duration);
        _zoomT = _zoomDur;
    }

    public static Camera2D? Camera => _camera;

    /// 每帧驱动：震屏衰减 / HitStop 恢复 / Zoom 动画。挂 GameManager 下。
    private partial class Driver : Node
    {
        public override void _Process(double delta)
        {
            float dt = (float)delta;

            if (_shakeT > 0f)
            {
                _shakeT -= dt;
                if (_camera != null)
                {
                    float k = Mathf.Max(0f, _shakeT / _shakeDur);
                    _camera.Offset = new Vector2(
                        (GD.Randf() * 2f - 1f) * _shakePower * k,
                        (GD.Randf() * 2f - 1f) * _shakePower * k);
                }
                if (_shakeT <= 0f && _camera != null) _camera.Offset = Vector2.Zero;
            }

            if (_stopT > 0f)
            {
                _stopT -= dt;
                if (_stopT <= 0f) Engine.TimeScale = 1f;
            }

            if (_zoomT > 0f)
            {
                _zoomT -= dt;
                float k = 1f - Mathf.Max(0f, _zoomT / _zoomDur);
                if (_camera != null)
                    _camera.Zoom = Vector2.One * Mathf.Lerp(_zoomFrom, _zoomTo, k);
            }
        }
    }
}
