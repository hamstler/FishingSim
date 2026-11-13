shader_type spatial;
// Tree crowns and bushes built from leaf cards: alpha-tested sprays of painted leaves (an atlas of four leaf kinds,
// or conifer needles), lit as one soft volume (every card keeps the crown's outward normal on both sides), darker
// deep inside the crown (vertex colour), light shining through the leaves (backlight), a faint waxy sheen, wind
// sway and leaf flutter.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D leaf_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float wind = 0.3;
uniform float time = 0.0;
uniform vec3 translucency : source_color = vec3(0.55, 0.65, 0.25);

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

varying float shade;

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
	shade = COLOR.a;    // the vertex alpha carries the card's depth in the crown
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	vec4 leaf = texture(leaf_tex, UV);
	// The vertex colour is the depth shade times the tree's colour: take the shade out, keep a little of it.
	vec3 tint = srgb_to_linear(COLOR.rgb / max(shade, 0.05));
	ALBEDO = leaf.rgb * tint * mix(0.72, 1.0, shade);
	ALPHA = leaf.a;
	// Far crowns cover only a few pixels: accept thinner alpha there so they stay solid instead of fading to sticks.
	ALPHA_SCISSOR_THRESHOLD = mix(0.5, 0.12, smoothstep(25.0, 110.0, length(VERTEX)));
	AO = mix(0.4, 1.0, clamp(shade * 1.4 - 0.4, 0.0, 1.0));
	AO_LIGHT_AFFECT = 0.35;
	BACKLIGHT = translucency * tint * 1.6;
	ROUGHNESS = 0.62;
	SPECULAR = 0.3;
}
