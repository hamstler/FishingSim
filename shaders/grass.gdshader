shader_type spatial;
// Grass blades: colour from the terrain under them (instance colour), darker at the root, lighter and more
// translucent at the tips, bending in gusts of wind. UV.y runs from 0 at the root to 1 at the tip.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D gust_noise : repeat_enable, filter_linear_mipmap;
uniform float wind = 0.3;
uniform float time = 0.0;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

varying float tip;

void vertex() {
	tip = UV.y;
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float g = texture(gust_noise, origin.xz * 0.02 + vec2(time * 0.04, time * 0.015)).r;
	float bend = (0.15 + wind * 0.9) * (0.3 + g) * tip * tip;
	float flutter = sin(time * 3.5 + origin.x * 1.7 + origin.z * 1.3) * 0.08 * tip * (0.3 + wind);
	VERTEX.x += bend * 0.35 + flutter;
	VERTEX.z += bend * 0.2;
	VERTEX.y -= bend * 0.08;
	// Soft, mostly upward normals: blades lit like a lawn instead of flickering cards.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.65));
}

void fragment() {
	vec3 base = srgb_to_linear(COLOR.rgb);
	ALBEDO = base * mix(0.45, 1.15, tip);
	BACKLIGHT = base * 0.6 * tip;
	ROUGHNESS = 0.7;
	SPECULAR = 0.3;
}
