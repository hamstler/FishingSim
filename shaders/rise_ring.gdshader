// A rise ring (World/RiseRings): a flat quad riding the lake's waves (the Gerstner waves of
// shaders/water_waves.gdshaderinc, as on the water) whose fragment draws two rings spreading out and fading; instance
// uniform age runs 0..1 over the ring's life.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_draw_never;

#include "res://shaders/water_waves.gdshaderinc"

instance uniform float age = 1.0;
uniform vec3 tint : source_color = vec3(0.7, 0.78, 0.84);
uniform float light = 1.0;

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float lift = water_height(w.xz) + water_amp * 0.15;
	float sy = length(MODEL_MATRIX[1].xyz);
	VERTEX.y += lift / max(sy, 0.0001);
}

float ring(float d, float r, float w) {
	return smoothstep(w, 0.0, abs(d - r));
}

void fragment() {
	float d = length(UV - 0.5) * 2.0;
	float r1 = 0.12 + 0.85 * sqrt(age);
	float r2 = max(0.0, r1 - 0.3);
	// Thin bright crests with a faint dark trough inside each: a ripple, not a painted line.
	float a = ring(d, r1, 0.035) + 0.6 * ring(d, r2, 0.03) * step(0.05, r2);
	float trough = ring(d, r1 - 0.06, 0.04) + 0.6 * ring(d, r2 - 0.05, 0.035) * step(0.1, r2);
	float fade = (1.0 - age) * (1.0 - age) * step(d, 1.0);
	ALBEDO = mix(tint * 0.25, tint, a / max(a + trough, 0.001)) * light;
	ALPHA = clamp((a * 0.45 + trough * 0.18) * fade, 0.0, 1.0);
}
