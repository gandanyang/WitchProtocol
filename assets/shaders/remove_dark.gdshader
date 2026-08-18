shader_type canvas_item;
// 去黑底：亮度低于 cutoff 的像素变为透明。
// 用途：AI 黑底素材（角色 sprite / 子弹 / 特效）入游戏时去背景。
// 视觉统一规则：素材主色保持，只裁暗底，不做额外颜色偏移。

uniform float cutoff : hint_range(0.0, 1.0) = 0.08;

void fragment() {
    vec4 c = texture(TEXTURE, UV);
    float lum = max(max(c.r, c.g), c.b);
    float alpha = smoothstep(cutoff, cutoff + 0.18, lum) * c.a;
    COLOR = vec4(c.rgb, alpha);
}
