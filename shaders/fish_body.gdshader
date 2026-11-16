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
// How much of the painted scale rims stays (the rest melts into the scales' colour, read from a coarser mip): drawn
// rims made a black net over the fish; the scales still show by the normal map's relief and the light.
uniform float scale_lines = 0.3;
// Fish in the lake: the back darkens where the body faces up (countershading, as one sees a fish from the bank: a dark
// back over the bottom, the silver flank only flashing as it turns).
uniform float back_shade = 0.0;
uniform float back_y0 = 0.0;      // from this height of the unit model up the back darkens...
uniform float back_y1 = 0.1;      // ...to its darkest here

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
	VERTEX = fish_bend(VERTEX, TIME);
}

void fragment() {
	vec3 sharp = texture(albedo_tex, UV).rgb;
	vec3 soft = texture(albedo_tex, UV, 2.6).rgb;
	vec3 col = mix(soft, sharp, scale_lines);
	if (back_shade > 0.0) {
		// Darker up the body from the lateral line to the dorsal ridge, and wherever the body faces the sky.
		float up = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).y;
		float back = max(smoothstep(back_y0, back_y1, model_y), smoothstep(0.1, 0.8, up) * 0.85) * back_shade;
		// The back's own colour: the skin's hue, dark and olive (a roach's or a pike's back seen from above).
		vec3 dark = mix(vec3(dot(col, vec3(0.3, 0.59, 0.11))), col, 0.5) * vec3(0.3, 0.36, 0.31);
		col = mix(col, dark, back);
	}
	ALBEDO = col;
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
