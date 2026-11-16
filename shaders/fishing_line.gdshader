shader_type spatial;
// The fishing line (FishingRod.UpdateLine draws it as a camera-facing ribbon): its own colour (vertex colour: the line,
// braid bands, the tension tint) dimmed at night, and the sun glinting along it where the line lies square to the
// half-vector between the sun and the eye, the bright thread that shows a mono line against dark water.
render_mode unshaded, cull_disabled, shadows_disabled;

global uniform vec3 sun_dir;
global uniform float daylight;
global uniform vec3 sun_color;

varying vec3 along;
varying vec3 wpos;

void vertex() {
	// The ribbon's normal carries the line's direction (tangent) at this point.
	along = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 v = normalize(CAMERA_POSITION_WORLD - wpos);
	vec3 h = normalize(normalize(sun_dir) + v);
	float d = dot(along, h);
	float glint = pow(max(1.0 - d * d, 0.0), 260.0) * daylight;
	vec3 base = COLOR.rgb * mix(0.22, 1.0, daylight);
	ALBEDO = base + sun_color * glint * 1.6;
}
