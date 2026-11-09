// Rain without the particle system: every drop is one instance of a thin quad in a MultiMesh. The vertex shader makes
// them fall (TIME), wraps them in a box that follows the player (but stays anchored in the world) and turns each quad
// to face the camera around the vertical axis. INSTANCE_CUSTOM.xyz = the drop's place in the box (0..1).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_mix;

uniform vec3 area = vec3(44.0, 14.0, 44.0);
uniform float speed = 18.0;
uniform float slant = 0.12;
uniform vec4 tint : source_color = vec4(0.75, 0.82, 0.9, 0.45);

void vertex() {
	vec3 anchor = NODE_POSITION_WORLD;
	float fall = TIME * speed;
	vec3 base = INSTANCE_CUSTOM.xyz * area;
	vec3 p;
	p.x = mod(base.x + slant * fall - anchor.x, area.x) - area.x * 0.5;
	p.y = mod(base.y - fall, area.y) - 1.0;
	p.z = mod(base.z - anchor.z, area.z) - area.z * 0.5;
	vec3 cam = (inverse(MODEL_MATRIX) * vec4(INV_VIEW_MATRIX[3].xyz, 1.0)).xyz;
	vec2 to_cam = cam.xz - p.xz;
	to_cam = length(to_cam) > 0.001 ? normalize(to_cam) : vec2(0.0, 1.0);
	vec3 right = vec3(to_cam.y, 0.0, -to_cam.x);
	VERTEX = p + right * VERTEX.x + vec3(slant * VERTEX.y, VERTEX.y, 0.0);
}

void fragment() {
	ALBEDO = tint.rgb;
	ALPHA = tint.a;
}
