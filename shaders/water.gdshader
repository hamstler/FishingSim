shader_type spatial;
// Pixel-art lake: colour in flat bands by depth, see-through shallows (fish and the bottom show), blocky ripple
// highlights and shore foam on a world texel grid, banded sun glints. Works on every renderer.
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_toon, specular_toon;

// Wave constants must match Water.cs (the bobber rides the same waves).
const vec2 D1 = vec2(0.8, 0.6);
const vec2 D2 = vec2(-0.5, 0.866);
const vec2 D3 = vec2(0.9, -0.436);
const float TEXELS = 8.0;

uniform sampler2D depth_map : filter_linear, repeat_disable;
uniform vec4 map_rect = vec4(-170.0, -200.0, 340.0, 240.0);
uniform float max_depth = 16.0;
uniform float wave_time = 0.0;
uniform float amp = 0.03;
uniform float rain = 0.0;
uniform float glare_cut = 0.0;
uniform vec3 shallow_color : source_color = vec3(0.04, 0.26, 0.26);
uniform vec3 deep_color : source_color = vec3(0.01, 0.07, 0.11);
/// Metres of water you can see into (per map).
uniform float clarity = 2.6;
uniform sampler2D foam_noise : repeat_enable, filter_nearest_mipmap;

varying vec3 wpos;

float waves(vec2 p) {
	return 0.5 * sin(dot(p, D1) * 0.9 + wave_time * 1.1)
		+ 0.3 * sin(dot(p, D2) * 1.7 + wave_time * 1.6)
		+ 0.2 * sin(dot(p, D3) * 3.1 + wave_time * 2.3);
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += amp * waves(w.xz);
	wpos = w;
}

void fragment() {
	vec2 p = (floor(wpos.xz * TEXELS) + 0.5) / TEXELS;
	float e = 0.15;
	float h0 = waves(p);
	float hx = waves(p + vec2(e, 0.0));
	float hz = waves(p + vec2(0.0, e));
	vec3 n = normalize(vec3(-(hx - h0) / e * amp * 3.0, 1.0, -(hz - h0) / e * amp * 3.0));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	vec2 uv = (p - map_rect.xy) / map_rect.zw;
	float d = texture(depth_map, uv).r * max_depth;

	// Four flat colour bands from the shallows to the deep.
	float band = floor(clamp(d / 4.0, 0.0, 1.0) * 3.0 + 0.5) / 3.0;
	vec3 col = mix(shallow_color * 1.35, deep_color * 1.8 + vec3(0.01, 0.02, 0.03), band);

	// Ripple highlights: short horizontal dashes drifting with the wind.
	float rip = texture(foam_noise, p * 0.09 + vec2(wave_time * 0.015, wave_time * 0.006)).r;
	float stripe = step(0.88, fract(p.y * 0.9 + rip * 2.2 + wave_time * 0.08));
	float sparkle = stripe * step(0.6, rip) * (0.4 + amp * 10.0) * smoothstep(4.0, 12.0, length(VERTEX));   // none right under your feet
	// Rain rings: pixel dots popping on the surface.
	float rnd = fract(sin(dot(floor(p * 2.0), vec2(12.9898, 78.233))) * 43758.5453);
	float drop = rain * step(0.985, fract(rnd + wave_time * 0.9));
	// Foam where the water meets the shore.
	float foam = step(d, 0.04 + 0.025 * sin(wave_time * 1.5 + p.x * 0.7)) * step(0.68, texture(foam_noise, p * 1.3 + vec2(wave_time * 0.03)).r);

	float highlight = clamp(max(max(sparkle, drop), foam), 0.0, 1.0);
	ALBEDO = mix(col, vec3(0.92, 0.95, 0.95), highlight);
	ROUGHNESS = mix(0.15, 0.9, highlight);
	SPECULAR = 0.5 * (1.0 - 0.7 * glare_cut);

	// See-through shallows; how deep you can look depends on the lake's clarity (and polarized glasses).
	float see = clarity * (1.0 + glare_cut * 0.8);
	float a = mix(0.42, 0.93, smoothstep(0.0, see, d));
	a = floor(a * 4.0 + 0.5) / 4.0;
	ALPHA = max(a * smoothstep(0.0, 0.08, d), highlight * 0.9 * step(0.02, d));
}
