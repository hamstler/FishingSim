shader_type spatial;
// Floating plants (lily pads, their flowers, pondweed): coloured by vertex x instance colour, riding the lake's waves
// (the Gerstner waves of shaders/water_waves.gdshaderinc, as on the water and in Water.cs), a little wet sheen.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/water_waves.gdshaderinc"

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// World-space lift: the model matrix may be scaled, so divide the lift back into model units. The water mesh has
	// a vertex every metre and its shortest moving wave is 3 m long, so between its vertices the surface strays from
	// the exact waves by a small fraction of an amplitude: the leaves ride that much higher, never under the water.
	float lift = water_height(w.xz) + water_amp * 0.15;
	float sy = length(MODEL_MATRIX[1].xyz);
	VERTEX.y += lift / max(sy, 0.0001);
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;     // both sides of a leaf lit alike
	}
	vec3 c = srgb_to_linear(COLOR.rgb);
	ALBEDO = c;
	ROUGHNESS = 0.6;
	SPECULAR = 0.22;
	BACKLIGHT = c * 0.25;
}
