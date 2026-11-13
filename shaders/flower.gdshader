shader_type spatial;
// Wild flowers (TreeFactory.Wildflower): UV.x marks the part (0 stalk and leaves, 0.5 the eye, 1 the petals), UV.y
// the height along the stalk. Petals take the instance colour; the stalk stays green and the eye golden whatever the
// flower. They nod in the wind and let the light through.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform float wind = 0.3;
uniform float time = 0.0;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

varying float part;
varying vec3 petal;

void vertex() {
	part = UV.x;
	petal = COLOR.rgb;
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float h = UV.y * UV.y;
	float phase = origin.x * 1.3 + origin.z * 0.9;
	VERTEX.x += sin(time * 1.9 + phase) * 0.03 * h * (0.3 + wind);
	VERTEX.z += cos(time * 1.5 + phase * 1.2) * 0.02 * h * (0.3 + wind);
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	vec3 stalk = vec3(0.07, 0.16, 0.035);
	vec3 eye = vec3(0.75, 0.48, 0.03);
	vec3 c = part < 0.25 ? stalk : (part < 0.75 ? eye : srgb_to_linear(petal));
	ALBEDO = c;
	BACKLIGHT = c * 0.45;
	ROUGHNESS = part > 0.75 ? 0.5 : 0.7;
	SPECULAR = 0.3;
}
