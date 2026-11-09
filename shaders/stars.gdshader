shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, blend_add, fog_disabled;

uniform float night = 0.0;

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
	ALBEDO = vec3(0.9, 0.95, 1.0) * star * twinkle * horizon * night * 2.0;
}
