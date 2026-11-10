// Close-up fish body: the painted skin (FishSkin) with its scale normal map, a wet clear-coat, a silvery metallic
// sheen, and the swimming wave of fish_bend.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/fish_bend.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic;
uniform bool use_normal = false;
uniform float normal_strength = 0.9;
uniform float metallic = 0.2;
uniform float roughness = 0.32;
uniform float clearcoat = 0.6;
uniform float clearcoat_roughness = 0.16;

void vertex() {
	VERTEX = fish_bend(VERTEX, TIME);
}

void fragment() {
	ALBEDO = texture(albedo_tex, UV).rgb;
	METALLIC = metallic;
	SPECULAR = 0.6;
	ROUGHNESS = roughness;
	if (use_normal) {
		NORMAL_MAP = texture(normal_tex, UV).rgb;
		NORMAL_MAP_DEPTH = normal_strength;
	}
	CLEARCOAT = clearcoat;
	CLEARCOAT_ROUGHNESS = clearcoat_roughness;
	// Light passing through the thin belly and fin roots.
	BACKLIGHT = ALBEDO * 0.08;
}
