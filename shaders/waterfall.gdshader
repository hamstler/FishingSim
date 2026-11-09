// A falling sheet of water: bright streaks that run down (TIME), foamier near the bottom. No per-frame CPU work.
shader_type spatial;
render_mode cull_disabled, diffuse_toon, specular_disabled;

uniform vec4 water : source_color = vec4(0.55, 0.75, 0.86, 1.0);
uniform vec4 foam : source_color = vec4(0.94, 0.97, 1.0, 1.0);
uniform float speed = 1.6;

float hash(float n) { return fract(sin(n * 91.345) * 47453.21); }

void fragment() {
	// UV.x across the sheet, UV.y top (0) to bottom (1). Columns of streaks with their own speed.
	float col = floor(UV.x * 18.0);
	float v = fract(UV.y * 3.0 - TIME * speed * (0.8 + 0.5 * hash(col)) + hash(col + 7.0));
	float streak = step(0.62, v);
	float bottom = smoothstep(0.75, 1.0, UV.y);
	vec3 c = mix(water.rgb, foam.rgb, clamp(streak * 0.8 + bottom, 0.0, 1.0));
	ALBEDO = c;
	EMISSION = c * 0.25;
	ROUGHNESS = 0.4;
}
