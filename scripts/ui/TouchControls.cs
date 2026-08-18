using Godot;

namespace MagicThunder.UI;

/// <summary>
/// 移动端触控层（D-010 移动端基调：「触屏更智能、更容错」）。
/// 左手动态虚拟摇杆（移动）+ 右手右下聚焦按钮（低速/判定放大）。
///
/// 关键原则：**输入注入 Input Map 动作（Input.ActionPress/Release），不重写玩家输入逻辑**——
/// PlayerController 的 Input.GetVector / IsActionPressed("focus") 原样可用，键盘支持天然保留。
/// 自动射击已是默认行为，移动端友好天然具备。
///
/// 仅触屏可用时显示；桌面 / 开发机自动隐藏（不影响键鼠调试与探针）。
/// </summary>
public partial class TouchControls : CanvasLayer
{
    /// 摇杆方向判定阈值（与 project.godot 输入 deadzone 一致）。
    public const float Deadzone = 0.2f;
    /// 摇杆底盘半径（px）。
    public const float JoystickRadius = 70f;

    public override void _Ready()
    {
        // 非触屏（桌面 / headless 探针）不创建任何子控件，避免干扰键鼠与探针
        if (!DisplayServer.IsTouchscreenAvailable())
        {
            Visible = false;
            return;
        }

        var joystick = new JoystickPad();
        AddChild(joystick);

        var focus = new FocusButton();
        focus.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        focus.OffsetLeft = -170;
        focus.OffsetTop = -190;
        focus.OffsetRight = -80;
        focus.OffsetBottom = -100;
        AddChild(focus);
    }

    /// <summary>
    /// 方向向量 → 4 个移动动作的按下布尔（8 方向，分量超过阈值判定；纯函数，探针可验证）。
    /// dir 应为已归一化的方向向量（零向量 = 全松）。
    /// </summary>
    public static (bool left, bool right, bool up, bool down) DirToActions(Vector2 dir)
    {
        bool left = dir.X < -Deadzone;
        bool right = dir.X > Deadzone;
        bool up = dir.Y < -Deadzone;
        bool down = dir.Y > Deadzone;
        return (left, right, up, down);
    }
}

/// <summary>
/// 动态虚拟摇杆：手指按下任意位置出现摇杆底盘，拖动改变移动方向，松开复位。
/// 触摸点注入移动动作（move_left/right/up/down）到 Input Map，供 Input.GetVector 读取。
/// </summary>
public partial class JoystickPad : Control
{
    private int _touchIndex = -1; // 当前接管摇杆的触摸 index（-1 = 无）
    private Vector2 _base;        // 摇杆底盘中心（按下位置）
    private Vector2 _offset;      // 摇杆头相对底盘的位移（≤ 半径）

    public override void _Ready()
    {
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // 不拦截鼠标/其他 UI
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed && _touchIndex == -1)
            {
                _touchIndex = (int)touch.Index;
                _base = touch.Position;
                _offset = Vector2.Zero;
                QueueRedraw();
            }
            else if (!touch.Pressed && touch.Index == _touchIndex)
            {
                Release();
            }
        }
        else if (@event is InputEventScreenDrag drag)
        {
            if (drag.Index == _touchIndex)
            {
                _offset = (drag.Position - _base).LimitLength(TouchControls.JoystickRadius);
                ApplyActions();
                QueueRedraw();
            }
        }
    }

    /// 把摇杆位移转成移动动作：先按阈值判定「是否推杆」，再归一化方向（8 方向）。
    private void ApplyActions()
    {
        var effective = _offset.Length() < TouchControls.Deadzone * TouchControls.JoystickRadius
            ? Vector2.Zero
            : _offset.Normalized();
        var (l, r, u, d) = TouchControls.DirToActions(effective);
        SetAction("move_left", l);
        SetAction("move_right", r);
        SetAction("move_up", u);
        SetAction("move_down", d);
    }

    private void Release()
    {
        _touchIndex = -1;
        _offset = Vector2.Zero;
        SetAction("move_left", false);
        SetAction("move_right", false);
        SetAction("move_up", false);
        SetAction("move_down", false);
        QueueRedraw();
    }

    private static void SetAction(string name, bool pressed)
    {
        if (pressed) Input.ActionPress(name);
        else Input.ActionRelease(name);
    }

    public override void _Draw()
    {
        if (_touchIndex == -1) return;
        // 底盘（半透明白，不遮挡战场视觉）
        DrawCircle(_base, TouchControls.JoystickRadius, new Color(1f, 1f, 1f, 0.08f));
        DrawArc(_base, TouchControls.JoystickRadius, 0f, Mathf.Tau, 32, new Color(1f, 1f, 1f, 0.28f), 2f);
        // 摇杆头
        DrawCircle(_base + _offset, 22f, new Color(1f, 1f, 1f, 0.4f));
    }
}

/// <summary>
/// 右下角聚焦按钮：按住 = 低速 + 判定点放大（注入 Input "focus" 动作）。
/// 消费自身区域的触摸事件，避免误触动态摇杆。
/// </summary>
public partial class FocusButton : Control
{
    private int _touchIndex = -1;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(90, 90);
        MouseFilter = MouseFilterEnum.Stop; // 接收并消费触摸，不穿透到摇杆
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed && _touchIndex == -1)
            {
                _touchIndex = (int)touch.Index;
                Input.ActionPress("focus");
                AcceptEvent();
                QueueRedraw();
            }
            else if (!touch.Pressed && touch.Index == _touchIndex)
            {
                _touchIndex = -1;
                Input.ActionRelease("focus");
                AcceptEvent();
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        var c = new Vector2(Size.X / 2f, Size.Y / 2f);
        bool pressed = _touchIndex >= 0;
        DrawCircle(c, 46f, pressed ? new Color(1f, 1f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.16f));
        DrawArc(c, 46f, 0f, Mathf.Tau, 32, new Color(1f, 1f, 1f, 0.55f), 2f);
        var font = ThemeDB.FallbackFont;
        DrawString(font, c + new Vector2(-26f, -9f), "聚焦", HorizontalAlignment.Center, 52, 20, new Color(1f, 1f, 1f, 0.95f));
    }
}
