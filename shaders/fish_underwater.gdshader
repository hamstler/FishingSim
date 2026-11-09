// A photo fish seen through the water surface: the same swimming wave, tinted by the water and faded with depth,
// drawn after the transparent water so the lake bed and the pier still hide it.
shader_type spatial;
render_mode cull_disabled, depth_draw_never, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/fish_bend.gdshaderinc"

uniform sampler2D tex : source_color, filter_linear_mipmap_anisotropic;
uniform vec4 tint : source_color = vec4(1.0);
uniform bool use_alpha = false;

void vertex() {
	VERTEX = fish_bend(VERTEX, TIME);
}

void fragment() {
	vec4 c = texture(tex, UV);
	ALBEDO = c.rgb * tint.rgb;
	ALPHA = tint.a * (use_alpha ? c.a : 1.0);
	ROUGHNESS = 0.4;
}
