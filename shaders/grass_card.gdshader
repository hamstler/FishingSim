shader_type spatial;
// Meadow grass from photographed clumps (Poly Haven grass_medium_01, CC0; tools/assets/grass_cards.py): three crossed
// cards per clump (TreeFactory.MeadowCards), each instance showing one of the atlas's four cells (picked by its place),
// cut out by the alpha, tinted part way to the ground under it (instance colour) with the meadow's dry and lush patches
// (the same noise as terrain.gdshader and grass.gdshader), light through the blades, bending in gusts of wind.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D cards : source_color, filter_linear_mipmap_anisotropic;
uniform vec4 cell_height = vec4(0.5);     // used height of each cell (fraction; the clump stands on the bottom edge)
uniform vec4 cell_width = vec4(1.0);
uniform vec3 card_mean = vec3(0.1, 0.12, 0.05);     // the atlas's average colour (linear)
uniform sampler2D gust_noise : repeat_enable, filter_linear_mipmap;
uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
global uniform float foliage_wind;
global uniform float foliage_time;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

varying float tip;
varying vec2 cell_uv;
varying vec2 cell_size;
varying vec3 tint;

void vertex() {
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	// One of the four clumps, by place.
	float pick = fract(sin(dot(floor(origin.xz * 7.0), vec2(12.9898, 78.233))) * 43758.5453);
	int cell = int(min(pick * 4.0, 3.0));
	float h = cell_height[cell];
	float w = cell_width[cell];
	vec2 corner = vec2(float(cell % 2), float(cell / 2)) * 0.5;
	// The card keeps its cell's shape: as high as the clump drawn in it (the mesh is made for a half-high cell).
	VERTEX.y *= h / 0.5;
	VERTEX.xz *= w;
	cell_uv = corner + vec2(0.5 * (1.0 - w) * 0.5, 0.5 * (1.0 - h)) ;
	cell_size = vec2(0.5 * w, 0.5 * h);
	tip = 1.0 - UV.y;

	float g = texture(gust_noise, origin.xz * 0.02 + vec2(foliage_time * 0.04, foliage_time * 0.015)).r;
	float bend = (0.12 + foliage_wind * 0.8) * (0.3 + g) * tip * tip;
	float flutter = sin(foliage_time * 3.2 + origin.x * 1.7 + origin.z * 1.3 + VERTEX.x * 7.0) * 0.035 * tip * (0.3 + foliage_wind);
	VERTEX.x += bend * 0.3 + flutter;
	VERTEX.z += bend * 0.18;
	VERTEX.y -= bend * 0.06;

	// The ground's colour with the meadow's patches, as in grass.gdshader; the photo keeps most of its own colour.
	vec3 base = srgb_to_linear(COLOR.rgb);
	float macro = textureLod(noise_tex, origin.xz * 0.013, 0.0).r;
	float mid = textureLod(noise_tex, origin.xz * 0.09 + vec2(0.37, 0.11), 0.0).r;
	float fine = textureLod(noise_tex, origin.xz * 0.43 + vec2(0.71, 0.29), 0.0).r;
	float finer = textureLod(noise_tex, origin.xz * 1.7 + vec2(0.13, 0.57), 0.0).r;
	float dry = smoothstep(0.52, 0.78, macro * 0.6 + fine * 0.25 + finer * 0.15);
	float lush = smoothstep(0.55, 0.8, (1.0 - macro) * 0.55 + mid * 0.45);
	vec3 k = mix(vec3(1.0), base / max(card_mean, vec3(0.002)), 0.3);
	k *= mix(0.88, 1.08, macro);
	k = mix(k, k * vec3(1.16, 1.02, 0.7), dry * 0.55);
	k = mix(k, k * vec3(0.8, 0.9, 0.76), lush * 0.45);
	tint = clamp(k, vec3(0.3), vec3(2.2));
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;     // both sides share the (upward) normal
	}
	vec2 uv = cell_uv + UV * cell_size;
	vec4 c = texture(cards, uv);
	ALBEDO = c.rgb * tint;
	// Thin blades fade away in the smaller mip levels; lift the alpha there so distant clumps stay full.
	float lod = max(textureQueryLod(cards, uv).x, 0.0);
	ALPHA = clamp(c.a * (1.0 + lod * 0.22), 0.0, 1.0);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	float t = smoothstep(0.0, 1.0, tip);
	AO = mix(0.55, 1.0, t);
	AO_LIGHT_AFFECT = 0.3;
	BACKLIGHT = c.rgb * tint * 0.5 * t;
	ROUGHNESS = 0.6;
	SPECULAR = 0.25;
}
