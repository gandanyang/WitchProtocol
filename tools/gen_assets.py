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
    # ---- S 级：黎歌战斗 sprite v2（方向正确版：头朝上/裙子朝后飘/星翼；用户拍板方向）----
    # 俯视背部视角，角色头在画面上方（飞向屏幕上方）、裙摆向身后（画面下方）飘动，
    # 加一对与主色调（紫/银）搭配的半透明星紫水晶翅膀。
    dict(name="rika_battlesprite2", w=1024, h=1024, dir="characters/rika/battlesprite",
         prompt=("anime game sprite of a small magical girl viewed from behind and slightly above, flying upward, "
                 "her head at the top of the frame pointing up toward the sky, only the back of her head and "
                 "shoulders visible, no face, long silver-purple hair flowing downward behind her, "
                 "a pair of translucent violet crystal wings spread open on her back, "
                 "black gothic witch dress with faint silver star patterns, the skirt trailing behind and "
                 "flowing downward away from her, arms slightly spread, "
                 "a few tiny glowing star fragments drifting nearby, "
                 "the character centered in the frame occupying about half the image height, "
                 "plain solid white background, clean flat anime style, soft even lighting, "
                 "small STG bullet hell player ship character sprite, top-down view")),
    # ---- S 级：黎歌战斗 sprite（俯视背部视角——STG 自机标准；prompt 按 prompt-expander-photo 方法论优化：
    #     视觉层级=背姿→长发→裙摆→星辉；可执行性=具体空间事实；自然语言段落而非关键词沙拉）----
    dict(name="rika_battlesprite", w=1024, h=1024, dir="characters/rika/battlesprite",
         prompt=("anime game sprite of a small magical girl character viewed from directly behind and slightly above, "
                 "flying upward toward the top of the screen, her head pointing up, only the back of her head and "
                 "shoulders visible, no face, long silver-purple hair streaming and floating upward, "
                 "black gothic witch dress with faint silver star patterns, skirt and hem flaring around her, "
                 "arms slightly spread for balance, a few tiny glowing star fragments drifting nearby, "
                 "the character centered in the frame occupying about half the image height, "
                 "plain solid white background, clean flat anime style, soft even lighting, "
                 "small STG bullet hell player ship character sprite, top-down view")),
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
    # ---- S 级：战斗背景候选（用户要求出几张选；统一星紫调+暗色低对比，保证弹幕可读性）----
    # prompt-expander-photo 方法论：视觉层级=主景→次景→细节光点；自然语言段落；反套路不堆 cinematic
    dict(name="bg_cand_nebula", w=1216, h=832, dir="backgrounds",
         prompt=("a vast deep-space nebula seen from far away, drifting clouds of violet and deep purple gas "
                 "with scattered silver stars and a faint starry galaxy band across the upper sky, "
                 "the lower third of the image calm and dark with a few tiny star fragments drifting down, "
                 "first read is the glowing violet nebula core in the upper center, secondary read the galaxy band, "
                 "the overall scene is dark and subdued with low contrast so foreground elements stay readable, "
                 "game stage background art for a vertical bullet hell shooter, muted violet silver palette, "
                 "no text, no watermark")),
    dict(name="bg_cand_ruins", w=1216, h=832, dir="backgrounds",
         prompt=("ancient gothic stone ruins floating in a starry night sky, broken pillars and archways covered in "
                 "faint glowing violet runes, a wide dark sky filled with small silver stars behind them, "
                 "a few star shards floating between the ruins, the ruins occupy the lower half of the image and "
                 "stay dark and low contrast so the player ship remains readable, first read the silhouette of the "
                 "broken archway, second read the glowing runes, game stage background art for a vertical bullet "
                 "hell shooter, muted violet silver palette, no text, no watermark")),
    dict(name="bg_cand_magiccircle", w=1216, h=832, dir="backgrounds",
         prompt=("a huge faint glowing star magic circle drawn across a dark night sky, its violet lines slowly "
                 "fading near the edges, a few small floating star fragments and dust specks catching the light, "
                 "the center of the circle is dark and empty giving space for the player, first read the huge "
                 "elegant magic circle, second read the drifting star shards, overall dark and subdued low contrast, "
                 "game stage background art for a vertical bullet hell shooter, muted violet silver palette, "
                 "no text, no watermark")),
    dict(name="bg_cand_floatingisles", w=1216, h=832, dir="backgrounds",
         prompt=("a few small floating islands hovering in a deep violet starry sky, tiny waterfalls of silver "
                 "starlight falling from their edges, a crescent moon glowing faintly behind the largest island, "
                 "the islands stay dark silhouettes in the lower part of the image with low contrast, "
                 "first read the crescent moon and largest island, second read the waterfalls of starlight, "
                 "overall calm and dark for bullet readability, game stage background art for a vertical bullet "
                 "hell shooter, muted violet silver palette, no text, no watermark")),
    dict(name="bg_cand_towers", w=1216, h=832, dir="backgrounds",
         prompt=("a silhouette of tall gothic spires and a witch castle on a distant floating rock, seen against a "
                 "deep violet night sky full of small silver stars, a faint glowing aurora band above the towers, "
                 "the towers stay dark and unlit at the bottom of the image for contrast, first read the spire "
         "silhouettes against the starfield, second read the faint aurora, overall dark subdued low contrast, "
         "game stage background art for a vertical bullet hell shooter, muted violet silver palette, "
         "no text, no watermark")),
    # ---- S 级：背景候选二批（氛围简约版：更大面积暗色留白，只留少量剪影/微光，弹幕可读性优先）----
    dict(name="bg_cand2_gradient", w=1216, h=832, dir="backgrounds",
         prompt=("an extremely minimal dark violet gradient sky, very dark almost black at the bottom fading to a "
                 "deep violet glow at the top, a handful of tiny silver stars scattered sparsely, one very faint "
                 "glowing line of a distant star trail, huge empty dark space in the middle of the frame, "
                 "no buildings no ground no clouds, soft gradient shading, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, keep the center of the "
                 "screen dark and empty for bullets, no text, no watermark")),
    dict(name="bg_cand2_cloudsea", w=1216, h=832, dir="backgrounds",
         prompt=("a dark sea of clouds seen from high above at night, the cloud tops barely catching a faint violet "
                 "moonlight from a small crescent moon in the upper corner, most of the image is deep dark clouds "
                 "with soft gentle shapes, a few tiny star glints above, calm and quiet, the upper middle of the "
                 "frame left dark and empty, minimal details, soft gradient shading, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, no text, no watermark")),
    dict(name="bg_cand2_aurora", w=1216, h=832, dir="backgrounds",
         prompt=("a dark starry sky with a single wide faint aurora ribbon of violet and pale silver light curving "
                 "across the top, the aurora is soft and translucent, below it the sky is very dark and almost "
                 "empty with only a few small stars, a thin line of dark mountain silhouette at the very bottom, "
                 "huge dark negative space in the middle, minimal and quiet, soft gradients, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, no text, no watermark")),
    dict(name="bg_cand2_skydust", w=1216, h=832, dir="backgrounds",
         prompt=("a very dark violet night sky with a gentle rain of tiny glowing silver star dust falling straight "
                 "down, each spark small and dim, most of the sky is deep dark empty space, a soft faint glow at the "
                 "top where the dust seems to come from, minimal details, quiet melancholic mood, "
                 "soft gradient shading, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, no text, no watermark")),
    dict(name="bg_cand2_silhouette", w=1216, h=832, dir="backgrounds",
         prompt=("a wide dark silhouette of a distant witch castle and leafless twisted trees on a low hill at the "
                 "very bottom edge of the frame, above them a huge dark violet starry sky with a large faint glowing "
                 "violet moon high up, a few wisps of thin cloud, the middle of the sky stays dark and almost empty, "
                 "the castle silhouette is simple and dark, minimal details, soft gradient shading, clean flat "
                 "anime style, minimalist game stage background art for a vertical bullet hell shooter, "
                 "no text, no watermark")),
    # ---- S 级：背景候选三批（深邃太空版：用户认可 skydust/gradient 方向，加强"太空深邃感"——
    #     更深黑、远近视差星点、远处星云/银河带、紫色只作微弱光晕点缀）----
    dict(name="bg_cand3_milkyway", w=1216, h=832, dir="backgrounds",
         prompt=("a vast deep-space view, mostly very dark near-black with an extremely faint translucent band of the "
                 "milky way stretching diagonally across the frame, the band is soft pale violet and silver with "
                 "denser star clusters inside, scattered individual stars at very different distances giving a "
                 "strong sense of depth, the center of the frame stays dark and almost empty for the player, "
                 "soft gradient, clean flat anime style, minimalist game stage background art for a vertical "
                 "bullet hell shooter, no text, no watermark")),
    dict(name="bg_cand3_deepvoid", w=1216, h=832, dir="backgrounds",
         prompt=("an extremely dark deep space void, near pure black background with only a handful of small "
                 "scattered stars at varying sizes and brightness giving a powerful sense of depth, a single very "
                 "faint distant violet nebula glow on one edge of the frame, no buildings no ground no clouds, "
                 "the middle of the screen left dark and empty, soft gradient, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, no text, no watermark")),
    dict(name="bg_cand3_farnebula", w=1216, h=832, dir="backgrounds",
         prompt=("deep dark space with a single very distant glowing violet nebula in the upper background, soft and "
                 "translucent, surrounded by a quiet cluster of stars, the rest of the sky is mostly near-black "
                 "with a sparse dusting of tiny stars giving strong depth, a single faint shooting star trail across "
                 "the middle, the center of the frame stays dark for bullets, soft gradient, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, no text, no watermark")),
    dict(name="bg_cand3_galaxyband", w=1216, h=832, dir="backgrounds",
         prompt=("a wide dark deep space sky with a soft horizontal galaxy band across the middle, the band is very "
                 "faint pale violet and silver with a few brighter star clusters inside, the upper and lower parts of "
                 "the sky are deeper near-black, scattered individual stars at different distances, "
                 "strong sense of depth, the central area between the band and the lower edge left dark and empty, "
                 "soft gradient, clean flat anime style, minimalist game stage background art for a vertical bullet "
                 "hell shooter, no text, no watermark")),
    dict(name="bg_cand3_silentmoon", w=1216, h=832, dir="backgrounds",
         prompt=("a quiet deep dark space with a single large faint glowing violet moon high in the upper sky, very "
                 "soft and atmospheric, a sparse field of small stars at different sizes and brightnesses giving "
                 "depth, the lower part of the sky fades to near-black, a single very thin star trail far in the "
                 "background, huge empty dark space in the middle, soft gradient, clean flat anime style, "
                 "minimalist game stage background art for a vertical bullet hell shooter, no text, no watermark")),
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
    # 多候选：--variants N（配合 --only 单 job）→ 输出 name_v1..name_vN（不同 seed）
    variants = 1
    if "--variants" in sys.argv:
        variants = int(sys.argv[sys.argv.index("--variants") + 1])
    wf = json.load(open(WF_PATH, encoding="utf-8"))
    seed = SEED_BASE
    ok, fail = 0, 0
    for job in JOBS:
        if only and job["name"] not in only:
            continue
        for v in range(variants):
            seed += 12345
            suffix = f"_v{v + 1}" if variants > 1 else ""
            print(f"== generating {job['name']}{suffix} (seed={seed}) ...", flush=True)
            try:
                pid = submit(wf, job, seed)
                img = wait_image(pid)
                dest = os.path.join(ASSETS, job["dir"], job["name"] + suffix + ".png")
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
