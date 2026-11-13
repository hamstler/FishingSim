shader_type spatial;
// Floating plants (lily pads, their flowers, pondweed): coloured by vertex x instance colour, riding the lake's waves
// (the wave function and its globals match shaders/water.gdshader and Water.cs), a little wet sheen.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

global uniform float water_time;
global uniform float water_amp;

const vec2 D1 = vec2(0.8, 0.6);
const vec2 D2 = vec2(-0.5, 0.866);
const vec2 D3 = vec2(0.9, -0.436);

float waves(vec2 p) {
	return 0.5 * sin(dot(p, D1) * 0.9 + water_time * 1.1)
		+ 0.3 * sin(dot(p, D2) * 1.7 + water_time * 1.6)
		+ 0.2 * sin(dot(p, D3) * 3.1 + water_time * 2.3);
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// World-space lift: the model matrix may be scaled, so divide the lift back into model units. The water mesh has
	// a vertex every 2 m and its finest wave is about that long, so between its vertices the surface strays from the
	// exact waves by up to ~0.4 amplitudes: the leaves ride that much higher, never under the water.
	float lift = water_amp * (waves(w.xz) + 0.45);
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
