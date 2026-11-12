shader_type spatial;
// Tree crowns and bushes built from leaf cards: alpha-tested leaf clusters, light shining through the leaves
// (backlight), self-shadowing from the vertex colour (inner cards are darker) and wind sway / leaf flutter.
render_mode cull_disabled, diffuse_toon, specular_disabled;

uniform sampler2D leaf_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform float wind = 0.3;
uniform float time = 0.0;
uniform vec3 translucency : source_color = vec3(0.55, 0.65, 0.25);

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float phase = origin.x * 0.31 + origin.z * 0.27;
	float h = clamp(VERTEX.y + 2.0, 0.0, 6.0);
	float gust = 0.6 + 0.4 * sin(time * 0.35 + phase * 0.2);
	vec2 sway = vec2(sin(time * 1.1 + phase), cos(time * 0.9 + phase * 1.3)) * 0.025 * h * wind * gust;
	float flutter = sin(time * 8.0 + dot(world, vec3(3.1, 4.7, 2.3))) * 0.02 * wind;
	VERTEX.xz += sway + vec2(flutter);
	VERTEX.y += flutter * 0.5;
}

void fragment() {
	vec4 leaf = texture(leaf_tex, UV);
	vec3 tint = srgb_to_linear(COLOR.rgb);
	ALBEDO = leaf.rgb * tint;
	ALPHA = leaf.a;
	// Far crowns cover only a few pixels: accept thinner alpha there so they stay solid instead of fading to sticks.
	ALPHA_SCISSOR_THRESHOLD = mix(0.5, 0.12, smoothstep(25.0, 110.0, length(VERTEX)));
	BACKLIGHT = translucency * tint * 1.5;
	ROUGHNESS = 0.75;
}
