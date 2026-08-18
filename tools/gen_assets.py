# -*- coding: utf-8 -*-
"""Witch Protocol 美术批量生成 — 复用 anima_turboV10 工作流（ComfyUI 127.0.0.1:8188）
用法: python tools/gen_assets.py [--list] [--only job1,job2]
前置: 本机 ComfyUI 已运行；工作流模板复用 G:/BattleGame/workflow/anima_turboV10.json
"""
import json
import os
import sys
import time
import urllib.parse
import urllib.request

BASE = "http://127.0.0.1:8188"
CLIENT = "wp-assets"
WF_PATH = "G:/BattleGame/workflow/anima_turboV10.json"
ASSETS = "G:/magicThunder/assets"
SEED_BASE = 20260819

# 视觉基准（设计规范 v0.1 §18）：暗黑幻想 x 魔法少女；黑底素材供 remove_dark shader 去底
# 黎歌人设（待拍板）：银紫长发 + 星紫瞳 + 黑色哥特魔女装 + 银新月发饰；主色紫/银，辅助色青蓝星光
JOBS = [
    # ---- S 级：黎歌角色设计（立绘两候选，拍板定稿）----
    dict(name="rika_portrait_A", w=832, h=1216, dir="characters/rika/portrait",
         prompt=("a 17-year-old magical girl with long silver-purple hair and starry violet eyes, "
                 "wearing a black gothic witch dress with faint star patterns, a small silver crescent hairpin, "
                 "gentle hopeful expression, holding a softly glowing star orb, standing in a twilight flower field "
                 "with falling star fragments, dark fantasy, gothic, magical girl, anime cinematic, beautiful girl, "
                 "high quality illustration, game character design, full body portrait")),
    dict(name="rika_portrait_B", w=832, h=1216, dir="characters/rika/portrait",
         prompt=("a 17-year-old magical girl with long silver-purple hair and starry violet eyes, "
                 "wearing a black gothic witch dress with glowing star patterns, determined battle expression, "
                 "surrounded by a huge glowing star magic circle and floating star fragments, starry night sky "
                 "with a distant rift, dark fantasy, gothic, magical girl, anime cinematic, glowing magic circle, "
                 "beautiful girl, high quality illustration, game character design, full body portrait")),
    # ---- S 级：黎歌战斗 sprite（黑底去底用；修订 v3：改"wizard robe + floor-length"约束）----
    dict(name="rika_battlesprite", w=1024, h=1024, dir="characters/rika/battlesprite",
         prompt=("full body character sprite of a young magical girl with long silver-purple hair and starry "
                 "violet eyes, wearing a long black wizard robe with silver star patterns that reaches her ankles, "
                 "the hem touching the ground, her legs fully covered by fabric, three-quarter view, holding "
                 "a small glowing star wand at her side, plain solid black background, clean flat anime style, "
                 "anime magical girl, dark fantasy priestess, full body coverage from neck to ankles, no exposed legs, "
                 "no short skirt, no bare skin below the waist, no thigh-highs, no garter, no corset, no strap shoes, "
                 "long black robes, floor-length hem, game character sprite asset")),
    # ---- S 级：玩家子弹（弹幕可读性）----
    dict(name="bullet_star", w=1024, h=1024, dir="bullets",
         prompt=("game bullet texture, a single glowing star projectile with a bright white core and violet-cyan "
                 "glow rays, centered, plain solid black background, clean flat anime style, high visibility, "
                 "game bullet asset")),
    # ---- S 级：战斗背景（暗黑幻想；修订：去掉"燃烧/红"，改为星紫调，与黎歌主色一致）----
    dict(name="bg_sky_ruins", w=1216, h=832, dir="backgrounds",
         prompt=("dark fantasy game battle background, ruined gothic floating city fragments and broken towers "
                 "at night, a tall luminous violet light pillar rising from the city into a starry sky with a "
                 "faintly glowing violet rift, falling star fragments, distant giant violet magic circles, "
                 "cool tones (violet, silver, deep blue, no warm colors), gothic atmosphere, no fire, no lava, no red, "
                 "anime cinematic, no characters, game background")),
    # ---- S 级：Boss 立绘（初代魔女阿斯特拉）----
    dict(name="boss_astra_portrait", w=832, h=1216, dir="characters/boss_astra",
         prompt=("a majestic first witch queen in a black and gold gothic gown with a huge ornate crown, pale skin, "
                 "calm closed eyes, floating above a giant glowing star magic circle, surrounded by star shards and "
                 "a dark rift in the sky, dark fantasy, gothic, magical girl villain, anime cinematic, high quality "
                 "illustration, game boss character design, full body portrait")),
    # ---- S 级：战败 CG（全屏结算背景，用户红线"战败 CG 一定要有"）----
    dict(name="defeat_cg", w=1216, h=832, dir="backgrounds",
         prompt=("dark fantasy defeat scene, a young magical girl with long silver-purple hair falling helplessly "
                 "through a ruined sky at night, her black gothic dress tattered, a huge glowing rift splitting the "
                 "sky above, a distant majestic witch queen silhouette hovering in the rift light, floating star "
                 "fragments and magic circle remnants fading, cinematic wide shot, melancholic epic atmosphere, "
                 "muted violet and silver palette, anime cinematic, high quality illustration, game defeat screen "
                 "background art, no text, no watermark")),
    # ---- S 级下一批：受击/击杀反馈特效 + 升级 UI + 魔法阵（黑底便于去底）----
    dict(name="hit_flash", w=1024, h=1024, dir="effects",
         prompt=("game visual effect sprite, a soft white-violet radial flash burst, expanding star sparks and "
                 "lens-flare rays, centered on plain solid black background, clean flat anime style, high contrast, "
                 "vfx game asset")),
    dict(name="kill_burst", w=1024, h=1024, dir="effects",
         prompt=("game visual effect sprite, a small cyan-violet star explosion, radiating star fragments and "
                 "thin light rays outward, centered on plain solid black background, clean flat anime style, "
                 "vfx game asset, no smoke")),
    dict(name="ui_levelup_panel", w=1024, h=1024, dir="backgrounds",
         prompt=("game UI panel illustration, a level up selection interface background in gothic fantasy style, "
                 "three horizontal rectangular card slots arranged side by side left center right with empty interior "
                 "space for placing text later, each slot framed with ornate gothic silver borders and glowing "
                 "violet magical accents, dark stone and starry background, ornate gothic frame border around the "
                 "whole panel, anime cinematic, game UI art asset, no text, no letters, no writing, no symbols")),
    dict(name="magic_circle_star", w=1024, h=1024, dir="effects",
         prompt=("game visual effect, an intricate glowing star magic circle, hexagonal geometric patterns and "
                 "concentric rings with a central bright star core, violet and silver glow on plain solid black "
                 "background, top-down view, clean anime style, high detail, pure geometric design with no text, "
                 "no letters, no writing, no symbols resembling alphabet characters, vfx game asset")),
]


def api(path):
    with urllib.request.urlopen(f"{BASE}{path}", timeout=15) as r:
        return r.read()


def submit(wf, job, seed):
    wf = json.loads(json.dumps(wf))  # deep copy
    wf["20"]["inputs"]["value"] = job["prompt"]
    wf["5"]["inputs"]["value"] = job["w"]
    wf["6"]["inputs"]["value"] = job["h"]
    wf["16"]["inputs"]["seed"] = seed
    wf["22"]["inputs"]["filename_prefix"] = "wp_" + job["name"]
    body = json.dumps({"prompt": wf, "client_id": CLIENT}).encode("utf-8")
    req = urllib.request.Request(f"{BASE}/prompt", data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=15) as r:
        resp = json.loads(r.read())
    pid = resp.get("prompt_id")
    if not pid:
        raise RuntimeError(f"submit failed: {resp}")
    return pid


def wait_image(pid, timeout=240):
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(2)
        try:
            h = json.loads(api(f"/history/{pid}"))
        except Exception:
            continue
        if pid in h:
            for out in h[pid].get("outputs", {}).values():
                for img in out.get("images", []):
                    if img.get("filename"):
                        return img
    raise TimeoutError(f"timeout waiting {pid}")


def download(img, dest):
    q = urllib.parse.urlencode({k: img[k] for k in ("filename", "subfolder", "type")})
    data = api(f"/view?{q}")
    # PNG 异步落盘重试
    for _ in range(5):
        if data and len(data) > 1000:
            break
        time.sleep(1)
        data = api(f"/view?{q}")
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "wb") as f:
        f.write(data)


def main():
    only = None
    if "--only" in sys.argv:
        only = set(sys.argv[sys.argv.index("--only") + 1].split(","))
    wf = json.load(open(WF_PATH, encoding="utf-8"))
    seed = SEED_BASE
    ok, fail = 0, 0
    for job in JOBS:
        if only and job["name"] not in only:
            continue
        seed += 12345
        print(f"== generating {job['name']} (seed={seed}) ...", flush=True)
        try:
            pid = submit(wf, job, seed)
            img = wait_image(pid)
            dest = os.path.join(ASSETS, job["dir"], job["name"] + ".png")
            download(img, dest)
            size = os.path.getsize(dest)
            print(f"   OK -> {dest} ({size} bytes)", flush=True)
            ok += 1
        except Exception as e:
            print(f"   FAIL: {e}", flush=True)
            fail += 1
    print(f"done. ok={ok} fail={fail}")


if __name__ == "__main__":
    main()
