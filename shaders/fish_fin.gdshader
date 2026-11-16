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
	// The painted rays softened (a coarser mip under the sharp one): thin, faint lines in a translucent membrane,
	// not a comb of black stripes; the colour a little calmer.
	vec4 sharp = texture(fin_tex, UV);
	vec4 soft = texture(fin_tex, UV, 2.2);
	vec4 c = vec4(mix(soft.rgb, sharp.rgb, 0.35), mix(soft.a, sharp.a, 0.75));
	c.rgb = mix(vec3(dot(c.rgb, vec3(0.3, 0.59, 0.11))), c.rgb, 0.8);
	ALBEDO = c.rgb;
	ALPHA = c.a * 0.9;
	ROUGHNESS = 0.4;
	SPECULAR = 0.45;
	BACKLIGHT = c.rgb * 0.55;
}
