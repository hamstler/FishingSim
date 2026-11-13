shader_type spatial;
// Boulders: the terrain's painted rock layer (World/Props/GroundTextures, layer 5) mapped triplanar in world space,
// tinted by the instance colour (each stone a little lighter or darker), moss on the faces that look up.
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2DArray ground_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray ground_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
uniform vec3 rock_mean = vec3(0.15);
uniform vec3 moss_tint : source_color = vec3(0.22, 0.32, 0.1);
uniform float moss = 0.7;
uniform float tile = 2.0;

varying vec3 wpos;
varying vec3 wn;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = normalize(wn);
	vec3 tw = pow(abs(n), vec3(4.0));
	tw /= (tw.x + tw.y + tw.z);
	// A larger scale than the ground (stones are seen up close and from all sides).
	vec2 uy = wpos.xz / (tile * 0.8), ux = wpos.zy / (tile * 0.8), uz = wpos.xy / (tile * 0.8);
	vec4 a = texture(ground_albedo, vec3(uy, 5.0)) * tw.y + texture(ground_albedo, vec3(ux, 5.0)) * tw.x + texture(ground_albedo, vec3(uz, 5.0)) * tw.z;
	vec4 qy = texture(ground_normal, vec3(uy, 5.0)), qx = texture(ground_normal, vec3(ux, 5.0)), qz = texture(ground_normal, vec3(uz, 5.0));
	vec3 tilt = vec3(qy.r * 2.0 - 1.0, 0.0, qy.g * 2.0 - 1.0) * tw.y
		+ vec3(0.0, qx.g * 2.0 - 1.0, qx.r * 2.0 - 1.0) * tw.x
		+ vec3(qz.r * 2.0 - 1.0, qz.g * 2.0 - 1.0, 0.0) * tw.z;
	float rough = 0.75;
	vec3 col = a.rgb * srgb_to_linear(COLOR.rgb) / rock_mean;
	// Moss on the tops, ragged at its edge, thicker in the hollows of the stone.
	float nz = texture(noise_tex, wpos.xz * 0.35 + wpos.y * 0.21).r;
	float m = smoothstep(0.45, 0.8, n.y + (nz - 0.5) * 0.7 + (0.5 - a.a) * 0.4) * moss;
	vec3 mossc = srgb_to_linear(moss_tint) * (0.7 + 0.6 * nz) * mix(0.8, 1.2, a.a);
	col = mix(col, mossc, m);
	vec3 pn = normalize(n + tilt * mix(1.2, 0.4, m));
	NORMAL = normalize((VIEW_MATRIX * vec4(pn, 0.0)).xyz);
	ALBEDO = clamp(col, 0.0, 1.0);
	ROUGHNESS = mix(rough, 0.95, m);
	SPECULAR = 0.3;
}
