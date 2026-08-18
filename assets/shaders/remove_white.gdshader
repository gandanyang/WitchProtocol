shader_type canvas_item;
// 去白底：三通道都接近白色（纯白背景/灰白底）的像素变为透明。
// 用途：白底素材（角色立绘 / 贴图）入游戏时去背景。
// 判定用 min(r,g,b)：只有"三通道都极高"的像素才删，含彩色分量的主体保留。
// 视觉统一规则：素材主色保持，只裁白底，不做额外颜色偏移。

uniform float cutoff : hint_range(0.0, 1.0) = 0.9;

void fragment() {
    vec4 c = texture(TEXTURE, UV);
    float mn = min(min(c.r, c.g), c.b);
    // mn >= cutoff 时 alpha→0（透明）；mn 越低越不透明。
    float alpha = (1.0 - smoothstep(cutoff, 1.0, mn)) * c.a;
    COLOR = vec4(c.rgb, alpha);
}
