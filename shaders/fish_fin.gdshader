// Close-up fish fin: the painted fin (FinPainter) with translucent membrane and rays, lit from behind as well
// (sunlight shines through fins), bending with the body's swimming wave.
shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/fish_bend.gdshaderinc"

uniform sampler2D fin_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float flutter = 0.0;     // paired fins: a gentle beat of their own

void vertex() {
	VERTEX = fish_bend(VERTEX, TIME);
	VERTEX.y += flutter * UV.y * UV.y * 0.012 * sin(TIME * 6.0 + swim_phase * 1.3);
}

void fragment() {
	vec4 c = texture(fin_tex, UV);
	ALBEDO = c.rgb;
	ALPHA = c.a;
	ROUGHNESS = 0.4;
	SPECULAR = 0.45;
	BACKLIGHT = c.rgb * 0.55;
}
