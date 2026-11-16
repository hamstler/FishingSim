shader_type spatial;
// The lake's surface, seen as real water is: smooth wind waves with fine ripples riding on them; what lies under the
// surface (the bottom, the weeds, the fish) seen through it, bent by the waves and coloured by the water it shows
// through (red goes first, so shallows are clear and the deep turns green-blue); the sky, the shore and the trees
// reflected on top, more and more as you look further and flatter across the lake (Fresnel); the sun glinting on the
// ripples; foam where the water laps the shore; rings where raindrops fall.
render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;

// The Gerstner wind waves (shared with the floating plants and the rise rings; mirrored by Water.cs).
#include "res://shaders/water_waves.gdshaderinc"

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;
uniform sampler2D depth_map : filter_linear, repeat_disable;
uniform vec4 map_rect = vec4(-170.0, -200.0, 340.0, 240.0);
uniform float max_depth = 16.0;
uniform float wind = 0.3;
uniform float rain = 0.0;
/// 0..1: polarized glasses cut the surface glare (you see into the water further out).
uniform float glare_cut = 0.0;
uniform vec3 shallow_color : source_color = vec3(0.04, 0.26, 0.26);
uniform vec3 deep_color : source_color = vec3(0.01, 0.07, 0.11);
/// Metres of water you can see into (per map).
uniform float clarity = 2.6;
// How fast red, green and blue go along the path through the water (green = 1), per map: a clear mountain lake loses
// red and keeps blue, a lowland lake full of humus and plankton loses blue nearly as fast as red and keeps green.
uniform vec3 extinction_tint = vec3(2.4, 1.0, 0.8);
uniform sampler2D foam_noise : repeat_enable, filter_linear_mipmap;
/// Tileable wind-ripple normals (Water.RippleNormals): RG = slope / 2 + 0.5, two drifting readings.
uniform sampler2D ripple_normals : repeat_enable, filter_linear_mipmap_anisotropic;
/// How strongly the ripple maps roughen the surface (Water.Detail).
uniform float detail = 1.0;
/// Screen-space reflection steps (0 = the sky's reflection only); set by the graphics quality.
uniform int ssr_steps = 20;

varying vec3 wpos;

// The slope carried by a ripple map at uv.
vec2 ripple_map(vec2 uv) {
	return texture(ripple_normals, uv).rg * 2.0 - 1.0;
}

varying float crest;

// Rings spreading where raindrops land (slope of the ring's ripple), in 0.6 m cells near the camera.
vec2 rain_slope(vec2 p) {
	vec2 cell = floor(p / 0.6);
	vec2 slope = vec2(0.0);
	for (int i = 0; i < 4; i++) {
		vec2 c = cell + vec2(float(i & 1), float(i >> 1)) - 0.5;
		float rnd = fract(sin(dot(c, vec2(12.9898, 78.233))) * 43758.5453);
		float phase = fract(water_time * (0.9 + rnd * 0.6) + rnd * 7.0);
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
	// The long waves move the mesh: up and down, and round in their orbits (sharper crests, flatter troughs).
	vec3 d = gerstner(w.xz, 0, VERTEX_WAVES);
	VERTEX += d;
	wpos = w + d;
	// How high this point rides on the swell (0 trough .. 1 crest): white caps break there in a strong wind.
	crest = clamp(d.y / max(water_amp * 0.9, 0.0001) * 0.5 + 0.5, 0.0, 1.0);
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
	// The surface's slope: every Gerstner wave exactly (the short chop fading out with distance so it does not
	// shimmer), the wind's ripples from two drifting normal maps (fine near, broad further out) and raindrops.
	vec2 rest = wpos.xz - gerstner(wpos.xz, 0, VERTEX_WAVES).xz;
	float near = 1.0 - smoothstep(25.0, 140.0, dist);
	// The short chop does not move the mesh, so it may be steeper than its share of the swell (lake chop is short
	// and steep); it fades out with distance so it does not shimmer.
	vec2 s = gerstner_slope(rest, 0, VERTEX_WAVES).xy + gerstner_slope(rest, VERTEX_WAVES, WAVES).xy * (0.6 + 2.4 * near);
	vec2 wd = normalize(water_dir);
	vec2 across = vec2(-wd.y, wd.x);
	// A calm evening lake is nearly a mirror; the wind roughens it.
	float chop = (0.08 + wind * 1.45) * detail;
	vec2 r1 = ripple_map(wpos.xz / 6.5 + wd * water_time * 0.045);
	vec2 r2 = ripple_map(wpos.xz / 2.3 - across * water_time * 0.07 + wd * water_time * 0.03);
	s += (r1 * 0.26 * (0.4 + 0.6 * near) + r2 * 0.17 * near) * chop;
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
	// Near the screen's edges the shift fades out: a shifted read past the edge repeats the last row as streaks.
	vec2 border = min(SCREEN_UV, 1.0 - SCREEN_UV);
	shift *= smoothstep(0.0, 0.07, min(border.x, border.y));
	vec2 ruv = SCREEN_UV + shift;
	vec3 rbehind = view_at(ruv, INV_PROJECTION_MATRIX);
	if (length(rbehind) < dist)
		ruv = SCREEN_UV;
	else
		thickness = max(length(rbehind) - dist, 0.0);
	vec3 refracted = textureLod(screen_tex, ruv, 0.0).rgb;

	// Absorption along the path through the water: red goes first, then green; clear lakes let you see further.
	vec3 extinction = extinction_tint * (1.4 / max(clarity, 0.3));
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

	// Far reflection: where the screen-space march found nothing (the far shore, the sky above it), the mirrored ray
	// is followed to whatever the screen shows in its direction (two steps to settle on that point's distance): the
	// twilight sky, the tree line, the lit windows lie mirrored on the water as on a real lake.
	if (fresnel > 0.02 && hit < 0.99) {
		vec3 R = reflect(-V, N);
		float D = 80.0;
		vec3 sp = vec3(-1.0);
		for (int k = 0; k < 3; k++) {
			sp = to_screen(VERTEX + R * D, PROJECTION_MATRIX);
			if (sp.x < 0.0 || sp.x > 1.0 || sp.y < 0.0 || sp.y > 1.0)
				break;
			D = clamp(length(view_at(sp.xy, INV_PROJECTION_MATRIX)), 4.0, 4000.0);
		}
		if (sp.x >= 0.0 && sp.x <= 1.0 && sp.y >= 0.0 && sp.y <= 1.0) {
			// Only what stands above the water is mirrored.
			vec3 scene_w = (INV_VIEW_MATRIX * vec4(view_at(sp.xy, INV_PROJECTION_MATRIX), 1.0)).xyz;
			float above_water = smoothstep(wpos.y - 0.05, wpos.y + 0.4, scene_w.y);
			vec2 edge = smoothstep(0.0, 0.06, sp.xy) * (1.0 - smoothstep(0.94, 1.0, sp.xy));
			float w_far = edge.x * edge.y * above_water * (1.0 - hit);
			vec3 far_col = textureLod(screen_tex, sp.xy, 1.5).rgb;
			reflected = mix(reflected, far_col, w_far / max(hit + w_far, 0.0001));
			hit = max(hit, w_far);
		}
	}

	// Foam where the water laps the shore: a thin, broken band that runs up and back with the swell.
	float swash = water_height(wpos.xz) / max(water_amp, 0.0001);
	float lap = 0.045 + 0.03 * swash * clamp(water_amp * 12.0, 0.2, 1.0);
	float foam_tex = texture(foam_noise, wpos.xz * 0.35 + vec2(water_time * 0.02)).r;
	float foam = (1.0 - smoothstep(0.0, max(lap, 0.01), below)) * smoothstep(0.55, 0.75, foam_tex);
	foam *= step(0.004, below);
	// White caps on the crests when the wind blows hard, breaking up into streaks downwind.
	float caps = smoothstep(0.55, 0.95, wind) * smoothstep(0.72, 0.95, crest)
		* smoothstep(0.62, 0.8, texture(foam_noise, wpos.xz * 0.21 - wd * water_time * 0.05).r) * near;
	foam = max(foam, caps * 0.8);
	// Over the last few centimetres the water clears completely, so the shore line is soft, not drawn.
	float shore = smoothstep(0.0, 0.06, below);

	float transmit = 1.0 - fresnel * shore;
	ALBEDO = mix(body * (1.0 - seen) * transmit * shore, vec3(0.85, 0.88, 0.88), foam);
	EMISSION = (refracted * mix(vec3(1.0), through, shore) * transmit + reflected * fresnel * hit * shore) * (1.0 - foam);
	SPECULAR = 0.25 * (1.0 - hit) * (1.0 - foam) * shore;
	ROUGHNESS = mix(0.035 + wind * 0.06 + rain * 0.08, 0.8, foam);
	METALLIC = 0.0;
	ALPHA = 1.0;
}
