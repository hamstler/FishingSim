shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

// Wave constants must match scripts/water.gd (the bobber rides the same waves).
const vec2 D1 = vec2(0.8, 0.6);
const vec2 D2 = vec2(-0.5, 0.866);
const vec2 D3 = vec2(0.9, -0.436);

uniform sampler2D depth_map : filter_linear, repeat_disable;
uniform vec4 map_rect = vec4(-170.0, -200.0, 340.0, 240.0); // x0, z0, width, length in metres
uniform float max_depth = 16.0;
uniform float wave_time = 0.0;
uniform float amp = 0.03;
uniform float rain = 0.0;
// 0..1: polarized glasses cut the surface glare, the water looks clearer and fish below are easier to see.
uniform float glare_cut = 0.0;
uniform vec3 shallow_color : source_color = vec3(0.04, 0.26, 0.26);
uniform vec3 deep_color : source_color = vec3(0.01, 0.07, 0.11);

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
	vec2 p = wpos.xz;
	float e = 0.15;
	float h0 = waves(p);
	float hx = waves(p + vec2(e, 0.0));
	float hz = waves(p + vec2(0.0, e));
	// Slopes are exaggerated so small waves still read visually.
	vec3 n = normalize(vec3(-(hx - h0) / e * amp * 2.0, 1.0, -(hz - h0) / e * amp * 2.0));

	if (rain > 0.01) {
		vec2 g = p * 2.5;
		vec2 id = floor(g);
		float rnd = fract(sin(dot(id, vec2(12.9898, 78.233))) * 43758.5453);
		float phase = fract(wave_time * 1.3 + rnd);
		vec2 c = fract(g) - vec2(0.5);
		float ring = smoothstep(0.07, 0.0, abs(length(c) - phase * 0.5)) * (1.0 - phase);
		n.xz += normalize(c + vec2(0.0001)) * ring * rain * 0.5;
		n = normalize(n);
	}
	// Fade wave detail with distance to avoid shimmering/moire.
	float dist = length(VERTEX);
	n = normalize(mix(vec3(0.0, 1.0, 0.0), n, 1.0 - smoothstep(8.0, 70.0, dist)));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	vec2 uv = (p - map_rect.xy) / map_rect.zw;
	float d = texture(depth_map, uv).r * max_depth;
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);

	vec3 col = mix(shallow_color, deep_color, clamp(d / 5.0, 0.0, 1.0));
	float foam = smoothstep(0.4, 0.0, d) * (0.5 + 0.5 * sin(wave_time * 2.0 + p.x * 1.3 + p.y * 1.1));
	ALBEDO = mix(col, vec3(0.9), foam * 0.3);
	ALPHA = clamp(mix(0.45, 0.95, smoothstep(0.0, 3.0, d)) + fres * 0.35, 0.0, 1.0) * smoothstep(0.0, 0.12, d);
	ALPHA *= 1.0 - 0.35 * glare_cut * (1.0 - fres);
	ALPHA = max(ALPHA, foam * 0.3 * smoothstep(0.0, 0.05, d));
	ROUGHNESS = 0.06;
	SPECULAR = 0.35;
	METALLIC = 0.0;
}
