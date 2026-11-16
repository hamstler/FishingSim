shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, blend_add, fog_disabled;

uniform float night = 0.0;
// The same cloud layer as the sky (shaders/sky.gdshader): stars hide behind the clouds.
uniform sampler2D cloud_noise : repeat_enable, filter_linear_mipmap;
uniform float cloud_cover = 0.3;
global uniform float cloud_drift;

float cloud_shape(vec2 uv) {
	return texture(cloud_noise, uv).r * 0.55
		+ texture(cloud_noise, uv * 2.03 + vec2(0.31, 0.17)).r * 0.28
		+ texture(cloud_noise, uv * 4.11 + vec2(0.71, 0.53)).r * 0.17;
}

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

void fragment() {
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(VERTEX, 0.0)).xyz);
	vec3 p = dir * 120.0;
	vec3 id = floor(p);
	float h = hash(id);
	vec3 f = fract(p) - vec3(0.5);
	float star = step(0.985, h) * smoothstep(0.3, 0.0, length(f));
	float twinkle = 0.7 + 0.3 * sin(TIME * 3.0 + h * 40.0);
	float horizon = smoothstep(0.02, 0.25, dir.y);
	vec2 uv = dir.xz / (max(dir.y, 0.0) + 0.08) * 0.08 + vec2(cloud_drift * 0.004, cloud_drift * 0.0017);
	float threshold = mix(0.72, 0.18, cloud_cover);
	float cloud = smoothstep(threshold - 0.05, threshold + 0.12, cloud_shape(uv));
	ALBEDO = vec3(0.9, 0.95, 1.0) * star * twinkle * horizon * night * 2.0 * (1.0 - cloud);
}
