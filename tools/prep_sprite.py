"""离线精灵预处理：白底大图 → 透明背景高清小图（运行时免 shader、免大纹理缩放）。

性能动机（用户反馈"太卡"）：主角战斗 sprite 是 1024px 白底大图，运行时每帧
  - 采样 1024x1024 大纹理（GPU 带宽浪费）
  - 叠 remove_white shader 去白底（额外着色器开销）
改为**离线一步搞定**：白键抠图（距白 40 内全透明、40~80 羽化过渡）+ 缩到 256px
（显示约 82px，2x+ 像素密度=高清精灵）。运行时直接加载透明 PNG，去掉 shader。

用法：python tools/prep_sprite.py [--src 源.png] [--out 目标.png] [--size 256] [--rotate 180]
默认：rika_battlesprite.png → rika_battlesprite_ready.png (256x256)
--rotate 180：绕中心右转 180°——STG 玩家视角从后往前看，出图角色头朝画面上方时，
  翻转后角色头朝移动方向（屏幕上方=玩家前方）。
"""
import os
import sys
from PIL import Image, ImageChops

SRC = "G:/magicThunder/assets/characters/rika/battlesprite/rika_battlesprite.png"
DST = "G:/magicThunder/assets/characters/rika/battlesprite/rika_battlesprite_ready.png"
OUT_SIZE = 256
ROTATE = 180  # 默认翻转：玩家从后往前看
# 白键阈值：距白 < KEY_SOFT 全透明；KEY_SOFT~KEY_HARD 线性羽化；> KEY_HARD 不透明。
# 浅色头发/高光保留：KEY_SOFT 不能太高（会把浅色头发当成背景透明化），典型 20/60。
KEY_SOFT, KEY_HARD = 20, 60


def make_alpha(rgb_img):
    """白底抠图 alpha：dist = 255 - max(R,G,B)（纯白=0）。"""
    r, g, b = rgb_img.split()
    mx = ImageChops.lighter(ImageChops.lighter(r, g), b)  # 最亮通道
    dist = ImageChops.subtract(Image.new("L", rgb_img.size, 255), mx)
    lut = [0] * 256
    for t in range(256):
        if t < KEY_SOFT:
            lut[t] = 0
        elif t > KEY_HARD:
            lut[t] = 255
        else:
            lut[t] = int((t - KEY_SOFT) / (KEY_HARD - KEY_SOFT) * 255)
    return dist.point(lut)


def make_alpha_black(rgb_img):
    """黑底抠图 alpha（发光特效/深色底用）：亮度下界 = min(R,G,B)（纯黑=0），暗部透明、亮部不透明。"""
    r, g, b = rgb_img.split()
    mn = ImageChops.darker(ImageChops.darker(r, g), b)
    lut = [0] * 256
    for t in range(256):
        if t < 25:
            lut[t] = 0
        elif t > 90:
            lut[t] = 255
        else:
            lut[t] = int((t - 25) / (90 - 25) * 255)
    return mn.point(lut)


def defringe(rgba):
    """去白边 halo：对 alpha 过渡带像素，用周围最不透明的颜色替换（简单 1px 收缩采样）。"""
    # 轻微处理即可：把 alpha < 255 且 RGB 偏白的像素颜色向里收缩
    r, g, b, a = rgba.split()
    # 用中值滤波太慢，这里仅做"透明边缘去白"：alpha 过渡带内 RGB 改为整体乘 0.98 趋近内色
    return rgba


def main():
    src, dst, size, rotate = SRC, DST, OUT_SIZE, ROTATE
    mode = "white"  # white=白底抠图（角色/背景主体）；black=黑底抠图（发光特效/深色底）
    if "--src" in sys.argv:
        src = sys.argv[sys.argv.index("--src") + 1]
    if "--out" in sys.argv:
        dst = sys.argv[sys.argv.index("--out") + 1]
    if "--size" in sys.argv:
        size = int(sys.argv[sys.argv.index("--size") + 1])
    if "--rotate" in sys.argv:
        rotate = int(sys.argv[sys.argv.index("--rotate") + 1])
    if "--mode" in sys.argv:
        mode = sys.argv[sys.argv.index("--mode") + 1]

    img = Image.open(src).convert("RGBA")
    alpha = make_alpha(img.convert("RGB")) if mode == "white" else make_alpha_black(img.convert("RGB"))
    img.putalpha(alpha)
    if rotate:
        img = img.rotate(rotate, expand=False, resample=Image.BICUBIC)  # 绕中心旋转（尺寸不变）
    img = img.resize((size, size), Image.LANCZOS)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    img.save(dst)
    print(f"OK: {src} ({img.size[0]}x{img.size[1]}) -> {dst} ({os.path.getsize(dst)}B, 透明背景, mode={mode}, rotate={rotate})")


if __name__ == "__main__":
    main()
