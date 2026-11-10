// The clear cornea over a close-up fish's eye: only its gloss shows; it sways with the head like the eye under it.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, shadows_disabled;
#include "res://shaders/fish_bend.gdshaderinc"

uniform vec3 eye_origin;
uniform vec3 eye_scale = vec3(1.0);

void vertex() {
	vec3 m = eye_origin + VERTEX * eye_scale;
	VERTEX = (fish_bend(m, TIME) - eye_origin) / eye_scale;
}

void fragment() {
	ALBEDO = vec3(0.9, 0.95, 1.0);
	ALPHA = 0.03;
	ROUGHNESS = 0.02;
	SPECULAR = 1.0;
	CLEARCOAT = 1.0;
	CLEARCOAT_ROUGHNESS = 0.02;
}
