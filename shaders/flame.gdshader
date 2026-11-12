// Campfire flames (World/Props/Campfire): a few crossed quads, each a flickering tongue of fire cut out of scrolling
// noise and coloured from a white-yellow core through orange to deep red at the tips. Additive, no lighting.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
uniform float strength = 1.0;
uniform float seed = 0.0;

void fragment() {
	vec2 uv = UV;
	float t = TIME * (1.3 + 0.2 * seed);
	// Two noise layers scroll upwards at different speeds; the flame's edge wobbles with them.
	float n1 = texture(noise_tex, vec2(uv.x * 0.9 + seed, uv.y * 0.8 + t * 0.9)).r;
	float n2 = texture(noise_tex, vec2(uv.x * 1.7 - seed * 2.0, uv.y * 1.4 + t * 1.6)).r;
	float n = n1 * 0.6 + n2 * 0.4;
	float h = 1.0 - uv.y;                          // 0 at the base, 1 at the top
	float width = mix(0.42, 0.04, pow(h, 0.8));    // a teardrop: broad at the base, pointed at the top
	float x = abs(uv.x - 0.5 + (n - 0.5) * 0.35 * h);
	float shape = smoothstep(width, width * 0.35, x) * smoothstep(1.0, 0.55, h + (n - 0.5) * 0.5);
	shape *= smoothstep(0.0, 0.08, h);
	float heat = clamp(shape * (1.3 - h) + n * 0.25 - 0.1, 0.0, 1.0);
	vec3 col = mix(vec3(0.55, 0.06, 0.0), vec3(1.0, 0.45, 0.05), smoothstep(0.1, 0.5, heat));
	col = mix(col, vec3(1.0, 0.88, 0.55), smoothstep(0.55, 0.95, heat));
	ALBEDO = col * shape * strength * 1.6;
}
