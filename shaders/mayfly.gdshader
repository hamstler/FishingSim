// Mayflies dancing over the water's edge at dusk (World/InsectSwarms): one MultiMesh instance per insect, the dance
// (a quick climb, then a slow floating fall, the swarm slowly circling) is worked out here, the quad faces the camera
// and the fragment draws the slender body, the upright wings and the three long tails.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

instance uniform float fade = 1.0;
uniform float light = 1.0;
uniform vec3 tint : source_color = vec3(0.95, 0.9, 0.78);

varying float v_flap;

float hash(float n) { return fract(sin(n * 12.9898) * 43758.5453); }

void vertex() {
	float id = float(INSTANCE_ID);
	float ph = hash(id) * 6.2831;
	float speed = 0.28 + 0.22 * hash(id + 3.1);
	float cyc = fract(TIME * speed + hash(id + 7.7));
	// The dance: up fast with beating wings, down slowly with the wings held still.
	float h = cyc < 0.28 ? smoothstep(0.0, 1.0, cyc / 0.28) : 1.0 - smoothstep(0.0, 1.0, (cyc - 0.28) / 0.72);
	float r = 0.15 + 0.85 * hash(id + 1.3);
	float a = TIME * (0.12 + 0.2 * hash(id + 5.0)) + ph;
	vec3 off = vec3(cos(a) * r, 0.8 + h * (0.7 + 0.7 * hash(id + 9.0)), sin(a) * r * 0.7);
	v_flap = cyc < 0.28 ? abs(sin(TIME * 55.0 + ph)) : 0.85;
	vec4 origin = MODEL_MATRIX[3] + vec4(off, 0.0);
	// Hidden insects (fewer at the start and end of the evening) fold away.
	float shown = step(hash(id + 11.0), fade);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], origin);
	VERTEX *= shown;
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	// Body along the diagonal, wings up from its middle, three tails behind.
	float body = smoothstep(0.16, 0.08, abs(p.y + p.x * 0.35)) * step(-0.55, p.x) * step(p.x, 0.75);
	float wing = smoothstep(1.0, 0.9, length(vec2((p.x - 0.1) * 2.3, (p.y + 0.55) * 1.3 / max(v_flap, 0.25))));
	float tails = step(abs(p.y - (p.x + 0.55) * 0.9), 0.04) + step(abs(p.y - (p.x + 0.55) * 0.2), 0.04) + step(abs(p.y + (p.x + 0.55) * 0.5), 0.04);
	tails *= step(p.x, -0.5) * step(-1.0, p.x);
	float a = max(body, max(wing * 0.7, tails * 0.8));
	if (a < 0.35) discard;
	vec3 c = mix(tint * 0.85, vec3(1.0, 0.97, 0.9), wing * (1.0 - body));
	ALBEDO = c * (0.08 + 0.92 * light);
}
