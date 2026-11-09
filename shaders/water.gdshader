shader_type spatial;
// Realistic lake surface for Forward+: refraction of what is under the water (screen texture), light absorption
// that grows with the thickness of water in front of the bottom (depth texture), scattered water colour,
// layered detail normals, sun glints and sky reflection (Fresnel), soft shoreline and foam.
render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;

// Wave constants must match Water.cs (the bobber rides the same waves).
const vec2 D1 = vec2(0.8, 0.6);
const vec2 D2 = vec2(-0.5, 0.866);
const vec2 D3 = vec2(0.9, -0.436);

uniform sampler2D depth_map : filter_linear, repeat_disable;
uniform vec4 map_rect = vec4(-170.0, -200.0, 340.0, 240.0);
uniform float max_depth = 16.0;
uniform float wave_time = 0.0;
uniform float amp = 0.03;
uniform float rain = 0.0;
uniform float glare_cut = 0.0;
uniform vec3 shallow_color : source_color = vec3(0.04, 0.26, 0.26);
uniform vec3 deep_color : source_color = vec3(0.01, 0.07, 0.11);
/// Metres of water after which ~60% of green light is gone (lake clarity).
uniform float clarity = 2.6;

uniform sampler2D normal_a : hint_normal, repeat_enable, filter_linear_mipmap;
uniform sampler2D normal_b : hint_normal, repeat_enable, filter_linear_mipmap;
uniform sampler2D foam_noise : repeat_enable, filter_linear_mipmap;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

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

float scene_distance(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_tex, uv).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return -v.z / v.w;
}

void fragment() {
	vec2 p = wpos.xz;
	float e = 0.15;
	float h0 = waves(p);
	float hx = waves(p + vec2(e, 0.0));
	float hz = waves(p + vec2(0.0, e));
	vec3 n = normalize(vec3(-(hx - h0) / e * amp * 2.0, 1.0, -(hz - h0) / e * amp * 2.0));

	// Two layers of small ripples drifting with the wind; stronger in wind.
	float strength = 0.12 + amp * 6.0;
	vec3 na = texture(normal_a, p * 0.045 + vec2(wave_time * 0.011, wave_time * 0.007)).rgb * 2.0 - 1.0;
	vec3 nb = texture(normal_b, p * 0.16 + vec2(-wave_time * 0.019, wave_time * 0.023)).rgb * 2.0 - 1.0;
	n = normalize(n + vec3(na.x + nb.x * 0.6, 0.0, na.y + nb.y * 0.6) * strength);

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
	float dist = length(VERTEX);
	n = normalize(mix(vec3(0.0, 1.0, 0.0), n, 1.0 - smoothstep(30.0, 160.0, dist) * 0.7));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// Thickness of water between the surface and whatever is behind it.
	float surface = -VERTEX.z;
	float behind = scene_distance(SCREEN_UV, INV_PROJECTION_MATRIX);
	float thick = max(behind - surface, 0.0);
	vec2 ruv = SCREEN_UV + n.xz * 0.035 * clamp(thick, 0.0, 1.0) / max(1.0, dist * 0.05);
	float behind_r = scene_distance(ruv, INV_PROJECTION_MATRIX);
	if (behind_r < surface) {      // the offset sample is in front of the water: do not refract it
		ruv = SCREEN_UV;
		behind_r = behind;
	}
	float path = max(behind_r - surface, 0.0);

	float clear = clarity * (1.0 + glare_cut * 0.6);
	vec3 absorb = vec3(0.42, 0.12, 0.09) * (2.6 / clear);
	vec3 transmit = exp(-absorb * path);
	vec3 refracted = textureLod(screen_tex, ruv, 0.0).rgb;

	// Scattered colour: shallow tint near the shore, deep tint further out.
	vec2 muv = (p - map_rect.xy) / map_rect.zw;
	float bed = texture(depth_map, muv).r * max_depth;
	vec3 scatter = mix(shallow_color, deep_color, clamp(bed / 4.0, 0.0, 1.0));

	float foam_mask = smoothstep(0.35, 0.0, thick) * smoothstep(0.0, 0.03, thick);
	float foam = foam_mask * smoothstep(0.45, 0.75, texture(foam_noise, p * 0.35 + vec2(wave_time * 0.02)).r + 0.25 * sin(wave_time * 1.5 + p.x + p.y));

	ALBEDO = mix(scatter * (1.0 - transmit), vec3(0.85), foam);
	EMISSION = refracted * transmit * (1.0 - foam);
	ROUGHNESS = mix(0.03, 0.6, foam);
	SPECULAR = 0.3 * (1.0 - 0.65 * glare_cut);
	METALLIC = 0.0;
	ALPHA = smoothstep(0.0, 0.06, thick);
}
