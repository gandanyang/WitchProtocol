"""程序化生成 MVP 音效（wav，16-bit mono 22050Hz）。

不依赖外部音频资产——用波形合成（正弦/方波/噪声 + 包络）生成可用的游戏音效：
  shot        射击（短促高频脉冲，音量低，避免 0.15s 射速下吵）
  player_hit  受击（低音下滑 + 噪声冲击）
  kill        击杀爆裂（噪声 + 低频衰减）
  levelup     升级（上行琶音）
  boss_intro  Boss 出场（低频轰鸣）

用法：python gen_sfx.py [--out DIR]   （默认 assets/audio/）
"""
import math
import os
import random
import struct
import sys
import wave

SR = 22050  # 采样率（游戏音效足够，文件小）
random.seed(42)


def write_wav(path, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        frames = b"".join(
            struct.pack("<h", max(-32767, min(32767, int(s * 32767)))) for s in samples
        )
        w.writeframes(frames)
    print(f"  {os.path.basename(path)}  {len(samples)/SR:.2f}s  {os.path.getsize(path)}B")


def env(t, dur, attack=0.005, release_ratio=0.6):
    """简单 AD 包络：短起音 + 尾部衰减（release 相对时长）。"""
    if t < attack:
        return t / attack if attack > 0 else 1.0
    release = dur * release_ratio
    if t > dur - release:
        return max(0.0, (dur - t) / release)
    return 1.0


def gen_shot():
    """射击：900→1200Hz 快速下滑双正弦，0.06s，-6dB。"""
    dur, out = 0.06, []
    for i in range(int(SR * dur)):
        t = i / SR
        f = 900 + (t / dur) * 300
        v = math.sin(2 * math.pi * f * t) * 0.5 + math.sin(2 * math.pi * f * 2 * t) * 0.2
        out.append(v * env(t, dur, 0.002, 0.5) * 0.5)
    return out


def gen_player_hit():
    """受击：200→70Hz 低音下滑 + 噪声冲击，0.28s。"""
    dur, out = 0.28, []
    for i in range(int(SR * dur)):
        t = i / SR
        f = 200 - (t / dur) * 130
        v = math.sin(2 * math.pi * f * t) * 0.6
        v += (random.random() * 2 - 1) * 0.5 * max(0.0, 1 - t / 0.05)  # 起音噪声
        out.append(v * env(t, dur, 0.003, 0.7) * 0.8)
    return out


def gen_kill():
    """击杀：白噪声爆裂 + 150→60Hz 低频，0.3s。"""
    dur, out = 0.3, []
    for i in range(int(SR * dur)):
        t = i / SR
        noise = (random.random() * 2 - 1) * 0.7 * max(0.0, 1 - t / 0.06)
        f = 150 - (t / dur) * 90
        low = math.sin(2 * math.pi * f * t) * 0.5
        out.append((noise + low) * env(t, dur, 0.002, 0.75) * 0.85)
    return out


def gen_levelup():
    """升级：523/659/784/1046Hz 上行琶音，各 0.07s。"""
    notes = [523.25, 659.25, 783.99, 1046.5]
    seg = 0.07
    out = []
    for idx, f in enumerate(notes):
        for i in range(int(SR * seg)):
            t = i / SR
            v = math.sin(2 * math.pi * f * t) * 0.5 + math.sin(2 * math.pi * f * 2 * t) * 0.15
            out.append(v * env(t, seg, 0.003, 0.6) * 0.7)
    return out


def gen_boss_intro():
    """Boss 出场：55Hz 低频轰鸣 + 缓慢噪声扫频，0.9s。"""
    dur, out = 0.9, []
    for i in range(int(SR * dur)):
        t = i / SR
        f = 55 + (t / dur) * 20
        v = math.sin(2 * math.pi * f * t) * 0.7
        v += math.sin(2 * math.pi * f * 0.5 * t) * 0.3
        v += (random.random() * 2 - 1) * 0.15 * max(0.0, t / 0.1)
        out.append(v * env(t, dur, 0.05, 0.5) * 0.9)
    return out


def main():
    out_dir = "assets/audio"
    if "--out" in sys.argv:
        out_dir = sys.argv[sys.argv.index("--out") + 1]

    print(f"生成音效 → {out_dir}")
    write_wav(os.path.join(out_dir, "shot.wav"), gen_shot())
    write_wav(os.path.join(out_dir, "player_hit.wav"), gen_player_hit())
    write_wav(os.path.join(out_dir, "kill.wav"), gen_kill())
    write_wav(os.path.join(out_dir, "levelup.wav"), gen_levelup())
    write_wav(os.path.join(out_dir, "boss_intro.wav"), gen_boss_intro())
    print("完成")


if __name__ == "__main__":
    main()
