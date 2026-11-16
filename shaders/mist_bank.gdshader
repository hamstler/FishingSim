shader_type spatial;
// Low mist over the water: a few horizontal layers stacked within a metre and a half of the surface. Seen at a
// grazing angle they add up to a soft bank lying on the lake; seen from above (or from inside) they stay thin. Each
// layer's density is drifting noise, thinner the higher it lies, fading near the camera and far away.
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
uniform vec3 mist_color : source_color = vec3(0.86, 0.88, 0.9);
uniform float density = 0.0;
global uniform float water_time;

varying vec3 wpos;
varying float layer;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	layer = INSTANCE_CUSTOM.x;     // 0 lowest .. 1 highest
}

void fragment() {
	vec2 drift = vec2(water_time * 0.015, water_time * 0.005) * (1.0 + layer);
	float n = texture(noise_tex, wpos.xz * 0.012 + drift + layer * 3.1).r * 0.65
		+ texture(noise_tex, wpos.xz * 0.045 - drift * 1.6).r * 0.35;
	float sheet = smoothstep(0.38, 0.8, n) * (1.0 - layer * 0.65);
	float d = length(VERTEX);
	float near = smoothstep(3.0, 18.0, d);
	float far = 1.0 - smoothstep(260.0, 420.0, d);
	// Faces seen edge-on gather more mist along the eye's path (clamped so the horizon does not turn solid).
	float graze = clamp(1.0 / max(abs(dot(normalize(NORMAL), normalize(VERTEX))), 0.08), 1.0, 6.0);
	ALBEDO = mist_color;
	ALPHA = clamp(sheet * near * far * density * 0.055 * graze, 0.0, 0.42);
}
