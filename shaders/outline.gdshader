shader_type spatial;
// Hand-drawn-style outlines: a full-screen pass drawn after everything else that darkens pixels where the depth or
// the surface direction jumps (object silhouettes and creases). Lines fade out with distance so far forests do
// not turn into noise. One pixel of the (low resolution) world image = one chunky outline pixel on screen.
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, blend_mix, fog_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D normal_tex : hint_normal_roughness_texture, filter_nearest, repeat_disable;
uniform vec3 line_color : source_color = vec3(0.16, 0.09, 0.06);
uniform float depth_threshold = 0.06;
uniform float normal_threshold = 0.55;
uniform float max_distance = 70.0;
uniform float strength = 0.85;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float linear_depth(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_tex, uv).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return -v.z / v.w;
}

vec3 normal_at(vec2 uv) {
	return texture(normal_tex, uv).xyz * 2.0 - 1.0;
}

void fragment() {
	vec2 px = 1.0 / VIEWPORT_SIZE;
	vec2 uv = SCREEN_UV;
	float d = linear_depth(uv, INV_PROJECTION_MATRIX);
	vec3 n = normal_at(uv);
	float edge = 0.0;
	// Only the nearer side of a depth jump gets the line, so silhouettes are one pixel thick.
	vec2 offs[4] = vec2[](vec2(px.x, 0.0), vec2(-px.x, 0.0), vec2(0.0, px.y), vec2(0.0, -px.y));
	for (int i = 0; i < 4; i++) {
		float dn = linear_depth(uv + offs[i], INV_PROJECTION_MATRIX);
		if ((dn - d) / max(d, 0.1) > depth_threshold)
			edge = 1.0;
		if (dot(n, normal_at(uv + offs[i])) < normal_threshold && d < max_distance * 0.6)
			edge = max(edge, 0.7);
	}
	float fade = 1.0 - smoothstep(max_distance * 0.5, max_distance, d);
	ALBEDO = line_color;
	ALPHA = edge * fade * strength;
}
