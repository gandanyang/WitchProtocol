using System.Collections.Generic;
using Godot;
using MagicThunder.Autoload;

namespace MagicThunder.Audio;

/// <summary>
/// 程序化音效播放器（静态工具类）。
///  - 音效资产：assets/audio/*.wav（由 tools/gen_sfx.py 程序生成，见该文件注释）。
///  - 宿主：懒加载挂到 GameManager（autoload）下，不依赖场景装配 / project.godot 改动——
///    避免与并行 Agent 的装配改动冲突（AI_GUARDRAIL 分文件责任）。
///  - 多通道：4 个 AudioStreamPlayer 轮换，防止同帧多音效互相截断。
///  - headless 安全：探针可直接调用 Play（不发声不崩）。
/// </summary>
public static class SfxPlayer
{
    private const string AudioDir = "res://assets/audio/";
    private const int Channels = 4;

    private static readonly List<AudioStreamPlayer> _players = new();
    private static readonly Dictionary<string, AudioStreamWav> _cache = new();
    private static int _cursor;

    /// 确保播放器池挂在 GameManager（autoload）下。失败（无 GameManager）则静默返回，不阻断游戏。
    private static bool EnsurePlayers()
    {
        if (_players.Count > 0) return true;
        if (GameManager.I == null) return false;

        for (int i = 0; i < Channels; i++)
        {
            var p = new AudioStreamPlayer { Name = $"Sfx{i}" };
            GameManager.I.AddChild(p);
            _players.Add(p);
        }
        return true;
    }

    /// 播放音效（按文件名，不带扩展名）。找不到资产时静默（不崩、不打日志刷屏）。
    public static void Play(string name, float volumeDb = 0f)
    {
        if (!EnsurePlayers()) return;

        if (!_cache.TryGetValue(name, out var stream))
        {
            stream = ResourceLoader.Load<AudioStreamWav>(AudioDir + name + ".wav");
            if (stream == null) return; // 资产缺失：静默跳过
            _cache[name] = stream;
        }

        var player = _players[_cursor];
        _cursor = (_cursor + 1) % Channels;
        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.Play();
    }

    /// 探针：音效资产完整性（5 个 wav 全部可加载）。headless 可跑。
    public static bool ProbeAssets()
    {
        foreach (var name in new[] { "shot", "player_hit", "kill", "levelup", "boss_intro" })
        {
            if (ResourceLoader.Load<AudioStreamWav>(AudioDir + name + ".wav") == null)
                return false;
        }
        return true;
    }
}
