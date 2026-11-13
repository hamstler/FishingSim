shader_type spatial;
// Grass blades (and rushes, iris leaves, ferns): colour from the ground under them (instance colour) with the same
// large-scale variation as the terrain (noise_tex, terrain.gdshader), a smooth gradient from a shaded root to a
// lighter, slightly sun-bleached tip, light shining through the blade (backlight), bending in gusts of wind.
// UV.y runs from 0 at the root to 1 at the tip.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D gust_noise : repeat_enable, filter_linear_mipmap;
uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
// Wind strength and the clock come from globals set once per frame (Scenery -> TreeFactory.SetWind).
global uniform float foliage_wind;
global uniform float foliage_time;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

varying float tip;
varying vec3 origin;

void vertex() {
	tip = UV.y;
	origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float g = texture(gust_noise, origin.xz * 0.02 + vec2(foliage_time * 0.04, foliage_time * 0.015)).r;
	float bend = (0.15 + foliage_wind * 0.9) * (0.3 + g) * tip * tip;
	float flutter = sin(foliage_time * 3.5 + origin.x * 1.7 + origin.z * 1.3 + VERTEX.x * 9.0) * 0.06 * tip * (0.3 + foliage_wind);
	VERTEX.x += bend * 0.35 + flutter;
	VERTEX.z += bend * 0.2;
	VERTEX.y -= bend * 0.08;
	// Mostly upward normals: blades lit like the lawn they make up instead of flickering dark cards.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.7));
}

void fragment() {
	// Both sides of a blade share its (upward) normal: undo the flip the engine gives back faces.
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	vec3 base = srgb_to_linear(COLOR.rgb);
	float macro = texture(noise_tex, origin.xz * 0.013).r;
	float mid = texture(noise_tex, origin.xz * 0.09 + vec2(0.37, 0.11)).r;
	base *= mix(0.86, 1.1, macro) * mix(vec3(1.06, 1.03, 0.86), vec3(0.9, 1.0, 0.95), mid);
	base = mix(vec3(dot(base, vec3(0.2126, 0.7152, 0.0722))), base, 0.85) * 0.92;     // as calm as the meadow under it
	float t = smoothstep(0.0, 1.0, tip);
	vec3 col = base * mix(0.42, 1.12, t);
	col = mix(col, col * vec3(1.12, 1.06, 0.72), t * t * 0.35);     // sun-bleached tips
	ALBEDO = col;
	AO = mix(0.45, 1.0, t);
	AO_LIGHT_AFFECT = 0.25;
	BACKLIGHT = base * 0.55 * t;
	ROUGHNESS = 0.55;
	SPECULAR = 0.25;
}
