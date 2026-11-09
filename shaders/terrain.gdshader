shader_type spatial;
// Terrain: the vertex colour carries the biome (grass, sand, path, snow) at map scale; this shader adds the
// detail you see up close: grass/soil variation, rock on steep slopes, wet dark sand at the waterline,
// micro-relief normals and moving light caustics on the lake bottom. Works in world space (no UVs needed).
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D detail_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D caustics_tex : repeat_enable, filter_linear_mipmap;
uniform vec3 rock_color : source_color = vec3(0.42, 0.40, 0.37);
uniform vec3 soil_color : source_color = vec3(0.30, 0.23, 0.15);
uniform float time = 0.0;
uniform float daylight = 1.0;
uniform float water_level = 0.0;

varying vec3 wpos;
varying vec3 wnormal;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnormal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float tri_noise(vec3 p, vec3 n, float scale) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	return texture(noise_tex, p.zy * scale).r * w.x + texture(noise_tex, p.xz * scale).r * w.y + texture(noise_tex, p.xy * scale).r * w.z;
}

void fragment() {
	vec3 base = srgb_to_linear(COLOR.rgb);
	vec3 n = normalize(wnormal);
	float macro = texture(noise_tex, wpos.xz * 0.013).r;
	float mid = texture(noise_tex, wpos.xz * 0.11 + vec2(0.37, 0.11)).r;
	float fine = texture(noise_tex, wpos.xz * 0.9).r;

	// Break up the vertex colour so the ground does not look painted.
	base *= mix(0.78, 1.18, macro);
	float green = clamp((base.g - max(base.r, base.b)) * 14.0, 0.0, 1.0);
	// Bare soil showing through the grass in patches.
	float soil = smoothstep(0.58, 0.72, mid + (1.0 - macro) * 0.15) * green;
	base = mix(base, srgb_to_linear(soil_color) * mix(0.75, 1.1, fine), soil * 0.7);
	// Dry and lush blades: hue variation at small scale.
	base *= mix(vec3(1.08, 1.02, 0.85), vec3(0.85, 1.0, 0.9), fine) * mix(1.0, 1.0, 1.0 - green);

	// Rock on steep slopes.
	float slope = 1.0 - n.y;
	float rock = smoothstep(0.32, 0.5, slope + (mid - 0.5) * 0.25);
	float r = tri_noise(wpos, n, 0.35) * 0.6 + tri_noise(wpos, n, 1.7) * 0.4;
	vec3 rockc = srgb_to_linear(rock_color) * mix(0.55, 1.25, r);
	base = mix(base, rockc, rock);

	// Wet band just above the waterline, mud below.
	float height = wpos.y - water_level;
	float wet = 1.0 - smoothstep(0.02, 0.45 + mid * 0.2, height);
	base *= mix(1.0, 0.55, wet);
	float rough = mix(0.92, 0.4, wet * (1.0 - step(height, -0.05)));

	// Micro relief: world-space detail normals, stronger on rock.
	vec3 dn = texture(detail_normal, wpos.xz * 0.45).rgb * 2.0 - 1.0;
	vec3 dn2 = texture(detail_normal, wpos.xz * 1.9 + vec2(0.5)).rgb * 2.0 - 1.0;
	float bump = mix(0.35, 0.9, rock);
	vec3 pn = normalize(n + vec3(dn.x + dn2.x * 0.5, 0.0, dn.y + dn2.y * 0.5) * bump);
	NORMAL = normalize((VIEW_MATRIX * vec4(pn, 0.0)).xyz);

	// Caustics: sunlight focused by the waves, dancing on the shallow bottom.
	if (height < -0.02) {
		vec2 cuv = wpos.xz * 0.22;
		float c1 = texture(caustics_tex, cuv + vec2(time * 0.03, time * 0.02)).r;
		float c2 = texture(caustics_tex, cuv * 1.3 - vec2(time * 0.025, time * 0.035)).r;
		float caustic = pow(min(c1, c2), 2.0) * 3.0;
		float depth_fade = exp(height * 0.6);     // height is negative under water
		EMISSION = vec3(0.9, 1.0, 0.95) * caustic * depth_fade * daylight * 0.35 * base;
	}

	ALBEDO = base;
	ROUGHNESS = rough;
	SPECULAR = 0.35;
}
