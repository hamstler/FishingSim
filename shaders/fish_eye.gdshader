// Close-up fish eye (the iris dome of a hand-built model): it moves with the body's swimming wave, so the eye stays in
// its socket while the head sways (a still eye under a swaying head showed the socket's rim cutting into it).
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/fish_bend.gdshaderinc"

uniform sampler2D iris_tex : source_color, filter_linear_mipmap_anisotropic;
uniform vec3 eye_origin;            // where the eye sits in the fish's unit-length model space
uniform vec3 eye_scale = vec3(1.0); // the eye node's scale in that space

void vertex() {
	vec3 m = eye_origin + VERTEX * eye_scale;
	VERTEX = (fish_bend(m, TIME) - eye_origin) / eye_scale;
}

void fragment() {
	// The iris is satin; the gloss is the cornea's over it.
	ALBEDO = texture(iris_tex, UV).rgb;
	ROUGHNESS = 0.4;
	METALLIC = 0.25;
	SPECULAR = 0.4;
}
