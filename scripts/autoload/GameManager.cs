using Godot;

namespace MagicThunder.Autoload;

/// <summary>
/// 全局运行态单例：游戏阶段、防御区域尺寸、场景路由。
/// 通过 <see cref="I"/> 访问（由 project.godot 的 autoload 保证唯一实例，挂在 /root/GameManager）。
/// </summary>
public partial class GameManager : Node
{
    public static GameManager I { get; private set; } = null!;

    /// 弹幕玩法视口（防御区域）尺寸；相机/出屏裁剪/对象池清理都以它为准。
    public Vector2 PlayfieldSize { get; private set; } = new(1280, 720);

    /// 当前章节（规划用占位；正式数值走存档，见 SaveSystem 红线）。
    public int Chapter { get; set; } = 0;

    public override void _EnterTree()
    {
        I = this;
    }

    public override void _Ready()
    {
        // 用离屏外 margin 兜底，避免首帧取到 0
        var r = GetViewport().GetVisibleRect();
        if (r.Size.X > 0f && r.Size.Y > 0f)
        {
            PlayfieldSize = r.Size;
        }
    }

    public override void _ExitTree()
    {
        if (I == this) I = null!;
    }

    /// 切场景（推荐用 ChangeSceneToFile + 场景路径，避免魔法字符串散落）。
    public void ChangeScene(string scenePath)
    {
        GetTree().ChangeSceneToFile(scenePath);
    }
}