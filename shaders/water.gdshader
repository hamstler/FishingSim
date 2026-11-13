shader_type spatial;
// The lake's surface, seen as real water is: smooth wind waves with fine ripples riding on them; what lies under the
// surface (the bottom, the weeds, the fish) seen through it, bent by the waves and coloured by the water it shows
// through (red goes first, so shallows are clear and the deep turns green-blue); the sky, the shore and the trees
// reflected on top, more and more as you look further and flatter across the lake (Fresnel); the sun glinting on the
// ripples; foam where the water laps the shore; rings where raindrops fall.
render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;

// Wave constants must match Water.cs (the bobber rides the same waves).
const vec2 D1 = vec2(0.8, 0.6);
const vec2 D2 = vec2(-0.5, 0.866);
const vec2 D3 = vec2(0.9, -0.436);

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;
uniform sampler2D depth_map : filter_linear, repeat_disable;
uniform vec4 map_rect = vec4(-170.0, -200.0, 340.0, 240.0);
uniform float max_depth = 16.0;
uniform float wave_time = 0.0;
uniform float amp = 0.03;
uniform float wind = 0.3;
uniform float rain = 0.0;
/// 0..1: polarized glasses cut the surface glare (you see into the water further out).
uniform float glare_cut = 0.0;
uniform vec3 shallow_color : source_color = vec3(0.04, 0.26, 0.26);
uniform vec3 deep_color : source_color = vec3(0.01, 0.07, 0.11);
/// Metres of water you can see into (per map).
uniform float clarity = 2.6;
uniform sampler2D foam_noise : repeat_enable, filter_linear_mipmap;
/// Screen-space reflection steps (0 = the sky's reflection only); set by the graphics quality.
uniform int ssr_steps = 20;

varying vec3 wpos;

// The three swells (also in Water.HeightAt) and their slope.
float waves(vec2 p) {
	return 0.5 * sin(dot(p, D1) * 0.9 + wave_time * 1.1)
		+ 0.3 * sin(dot(p, D2) * 1.7 + wave_time * 1.6)
		+ 0.2 * sin(dot(p, D3) * 3.1 + wave_time * 2.3);
}

vec2 wave_slope(vec2 p) {
	return amp * (D1 * 0.5 * 0.9 * cos(dot(p, D1) * 0.9 + wave_time * 1.1)
		+ D2 * 0.3 * 1.7 * cos(dot(p, D2) * 1.7 + wave_time * 1.6)
		+ D3 * 0.2 * 3.1 * cos(dot(p, D3) * 3.1 + wave_time * 2.3));
}

// The slope of the noise at a point (fine ripples), by differences of the noise texture.
vec2 ripple_slope(vec2 uv, float texel) {
	float h = texture(foam_noise, uv).r;
	float hx = texture(foam_noise, uv + vec2(texel, 0.0)).r;
	float hz = texture(foam_noise, uv + vec2(0.0, texel)).r;
	return vec2(hx - h, hz - h) / texel;
}

// Rings spreading where raindrops land (slope of the ring's ripple), in 0.6 m cells near the camera.
vec2 rain_slope(vec2 p) {
	vec2 cell = floor(p / 0.6);
	vec2 slope = vec2(0.0);
	for (int i = 0; i < 4; i++) {
		vec2 c = cell + vec2(float(i & 1), float(i >> 1)) - 0.5;
		float rnd = fract(sin(dot(c, vec2(12.9898, 78.233))) * 43758.5453);
		float phase = fract(wave_time * (0.9 + rnd * 0.6) + rnd * 7.0);
		vec2 centre = (c + 0.25 + 0.5 * fract(vec2(rnd * 13.1, rnd * 7.7))) * 0.6;
		vec2 d = p - centre;
		float r = length(d);
		float ring = phase * 0.32;
		float wave = sin((r - ring) * 60.0) * exp(-abs(r - ring) * 40.0) * (1.0 - phase);
		slope += (r > 0.001 ? d / r : vec2(0.0)) * wave;
	}
	return slope * 0.25 * rain;
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += amp * waves(w.xz);
	wpos = w;
}

// A view-space point back to the screen.
vec3 to_screen(vec3 view, mat4 proj) {
	vec4 clip = proj * vec4(view, 1.0);
	return vec3(clip.xy / clip.w * 0.5 + 0.5, clip.z / clip.w);
}

vec3 view_at(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_tex, uv).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return v.xyz / v.w;
}

void fragment() {
	float dist = length(VERTEX);
	// The surface's slope: the swells, the wind's ripples (two sizes, drifting downwind, fading out with distance
	// so they do not shimmer) and raindrops.
	vec2 s = wave_slope(wpos.xz);
	vec2 downwind = vec2(0.8, 0.6) * wave_time;
	float near = 1.0 - smoothstep(25.0, 140.0, dist);
	float chop = (0.25 + wind * 1.1) * near;
	s += ripple_slope(wpos.xz * 0.11 + downwind * 0.012, 0.004) * 0.010 * chop;
	s += ripple_slope(wpos.xz * 0.37 - downwind.yx * 0.021, 0.004) * 0.0045 * chop;
	if (rain > 0.01)
		s += rain_slope(wpos.xz) * (1.0 - smoothstep(6.0, 25.0, dist));
	vec3 n = normalize(vec3(-s.x, 1.0, -s.y));
	vec3 N = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	NORMAL = N;
	vec3 V = normalize(-VERTEX);

	// Reflection grows towards grazing views (water's F0 is 2 %); polarized glasses cut part of it.
	float cosv = clamp(dot(N, V), 0.0, 1.0);
	float fresnel = 0.02 + 0.98 * pow(1.0 - cosv, 5.0);
	fresnel *= 1.0 - 0.6 * glare_cut;

	// How much water lies behind this point of the surface (along the view), and how deep it is there.
	vec3 behind = view_at(SCREEN_UV, INV_PROJECTION_MATRIX);
	float thickness = max(length(behind) - dist, 0.0);
	vec3 up_view = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	float below = thickness * clamp(dot(V, up_view), 0.05, 1.0);

	// Refraction: the bottom is seen through the moving surface, shifted more where the water is deeper; a shifted
	// point that lies in front of the water (a pier post) is not used.
	vec2 shift = N.xy * 0.045 * clamp(thickness * 0.7, 0.0, 1.0) / (1.0 + dist * 0.03);
	vec2 ruv = SCREEN_UV + shift;
	vec3 rbehind = view_at(ruv, INV_PROJECTION_MATRIX);
	if (length(rbehind) < dist)
		ruv = SCREEN_UV;
	else
		thickness = max(length(rbehind) - dist, 0.0);
	vec3 refracted = textureLod(screen_tex, ruv, 0.0).rgb;

	// Absorption along the path through the water: red goes first, then green; clear lakes let you see further.
	vec3 extinction = vec3(2.4, 1.0, 0.8) * (1.4 / max(clarity, 0.3));
	vec3 through = exp(-thickness * extinction);
	float seen = (through.r + through.g + through.b) / 3.0;
	// The water's own colour (light scattered in it): greener in the shallows, darker blue over the deep.
	vec2 muv = (wpos.xz - map_rect.xy) / map_rect.zw;
	float bed = texture(depth_map, muv).r * max_depth;
	vec3 body = mix(shallow_color, deep_color, smoothstep(0.5, 6.0, max(bed, below)));

	// Screen-space reflection of the shore, the trees and the hills (the sky's reflection comes from the specular).
	vec3 reflected = vec3(0.0);
	float hit = 0.0;
	if (ssr_steps > 0 && fresnel > 0.03) {
		vec3 R = reflect(-V, N);
		float t = 0.4 + dist * 0.01;
		for (int i = 0; i < ssr_steps; i++) {
			vec3 p = VERTEX + R * t;
			vec3 sp = to_screen(p, PROJECTION_MATRIX);
			if (sp.x < 0.0 || sp.x > 1.0 || sp.y < 0.0 || sp.y > 1.0)
				break;
			vec3 scene = view_at(sp.xy, INV_PROJECTION_MATRIX);
			float gap = length(p) - length(scene);
			if (gap > 0.0 && gap < t * 0.35 + 0.6) {
				vec2 edge = smoothstep(0.0, 0.08, sp.xy) * (1.0 - smoothstep(0.92, 1.0, sp.xy));
				hit = edge.x * edge.y * (1.0 - float(i) / float(ssr_steps));
				reflected = textureLod(screen_tex, sp.xy, 0.0).rgb;
				break;
			}
			t *= 1.32;
		}
	}

	// Foam where the water laps the shore (a thin, broken edge that comes and goes with the swell).
	float lap = 0.05 + 0.03 * sin(wave_time * 1.4 + wpos.x * 0.6 + wpos.z * 0.4);
	float foam = (1.0 - smoothstep(0.0, lap, below)) * smoothstep(0.55, 0.75, texture(foam_noise, wpos.xz * 0.35 + vec2(wave_time * 0.02)).r);
	foam *= step(0.004, below);

	float transmit = 1.0 - fresnel;
	ALBEDO = mix(body * (1.0 - seen) * transmit, vec3(0.85, 0.88, 0.88), foam);
	EMISSION = (refracted * through * transmit + reflected * fresnel * hit) * (1.0 - foam);
	SPECULAR = 0.25 * (1.0 - hit) * (1.0 - foam);
	ROUGHNESS = mix(0.035 + wind * 0.06 + rain * 0.08, 0.8, foam);
	METALLIC = 0.0;
	ALPHA = 1.0;
}
