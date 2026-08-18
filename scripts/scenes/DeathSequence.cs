using Godot;
using MagicThunder.Effects;
using MagicThunder.Player;
using MagicThunder.UI;

namespace MagicThunder.Scenes;

/// <summary>
/// 战败 CG 演出（MVP 的灵魂场景，用户红线"战败 CG 一定要有"）。
/// 流程：魔女失去控制坠落（1.8s）→ 镜头拉远 + 黑幕渐入（1.2s）→ 定格静默（0.5s）→ DEFEATED 结算。
///  - ProcessMode=Always：Main.EndRun 已 GetTree().Paused=true，本层仍推进演出；
///  - 玩家坠落由本层逐帧驱动（ApplyDeathFall），Boss 随暂停定格"漂浮在天空"；
///  - 黑幕（ColorRect 全屏）在演出结束时隐藏，露出 Settlement 结算层。
/// </summary>
public partial class DeathSequence : CanvasLayer
{
    private const float FallTime = 1.8f;
    private const float ZoomTime = 1.2f;
    private const float PauseTime = 0.5f;
    private const float FinalZoom = 1.45f;

    private PlayerController? _player;
    private Settlement? _settlement;
    private int _score;
    private int _kills;
    private float _timeSec;
    private System.Action? _onRetry;
    private ColorRect? _curtain;
    private float _t;
    private int _phase;

    /// 由 Main.EndRun(false) 创建并 AddChild。
    public void Setup(PlayerController player, Settlement settlement, int score, int kills, float timeSec, System.Action onRetry)
    {
        _player = player;
        _settlement = settlement;
        _score = score;
        _kills = kills;
        _timeSec = timeSec;
        _onRetry = onRetry;
    }

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;

        _curtain = new ColorRect { Color = new Color(0f, 0f, 0f, 0f) };
        _curtain.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_curtain);

        // 玩家进入坠落状态（停止输入/射击），Boss 随暂停定格漂浮
        _player?.EnterDeathFall();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;

        switch (_phase)
        {
            case 0: // 坠落 + 黑幕渐入
                _player?.ApplyDeathFall(dt);
                float k0 = Mathf.Clamp(_t / FallTime, 0f, 1f);
                SetCurtain(0.35f * k0);
                if (_t >= FallTime)
                {
                    _phase = 1;
                    _t = 0f;
                    FeedbackSystem.ZoomTo(FinalZoom, ZoomTime); // 镜头慢慢拉远
                }
                break;

            case 1: // 镜头拉远 + 黑幕加深
                float k1 = Mathf.Clamp(_t / ZoomTime, 0f, 1f);
                SetCurtain(Mathf.Lerp(0.35f, 0.95f, k1));
                if (_t >= ZoomTime)
                {
                    _phase = 2;
                    _t = 0f;
                }
                break;

            case 2: // 定格静默（天空裂缝扩大的心理留白）
                if (_t >= PauseTime) Finish();
                break;
        }
    }

    private void SetCurtain(float alpha)
    {
        if (_curtain != null)
            _curtain.Color = new Color(0f, 0f, 0f, Mathf.Clamp(alpha, 0f, 1f));
    }

    private void Finish()
    {
        if (_curtain != null) _curtain.Visible = false; // 黑幕撤下，露出结算层
        _settlement?.ShowDefeat(_score, _kills, _timeSec, _player?.WeaponLevel ?? 1, _onRetry!);
        QueueFree();
    }
}
