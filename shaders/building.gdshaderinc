// Photographed CC0 materials of buildings and props (BuildingFactory.Photo): plaster, timber, logs, boards, stone, roofs.
// Mapped like a StandardMaterial3D did (triplanar in object space at the set's real size, or by the mesh's UVs in metres
// for roofs) and then weathered the way old village buildings are:
//  - a large-scale colour wander in world space, so a long wall never shows the same tile twice in the same light;
//  - rising damp and splashed dirt at the foot of a wall (darker, browner, rougher), its top edge wandering; the height
//    is measured from the building's ground (instance uniform base_y, set by World/Building);
//  - rain streaks running down vertical faces;
//  - moss and lichen on faces turned to the sky (stone footings, sills, roofs), in patches.
// The amount of each comes from the material (weathering 0..1, moss 0..1) and the house's own seed.

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 tint : source_color = vec3(1.0);
uniform float roughness_base = 0.85;
uniform float normal_strength = 1.1;
/// 1 / the set's real size in metres (times the material's scale).
uniform float uv_scale = 0.5;
uniform bool triplanar = true;
/// Dirt at the foot, streaks and colour wander (0 = factory fresh).
uniform float weathering = 1.0;
/// Moss on faces turned up.
uniform float moss = 0.4;
/// How tall the damp band at the foot of a wall is (m).
uniform float damp_height = 0.7;

instance uniform float base_y = -10000.0;

varying vec3 lpos;
varying vec3 lnormal;
varying vec3 wpos;
varying vec3 wnormal;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	return noise(p) * 0.55 + noise(p * 2.13 + 7.1) * 0.3 + noise(p * 4.37 + 3.3) * 0.15;
}

void vertex() {
	lpos = VERTEX;
	lnormal = NORMAL;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnormal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// A tangent-space normal from the map turned onto a projection plane (whiteout blend for the triplanar weights).
vec3 unpack(vec4 t) {
	vec3 n = t.xyz * 2.0 - 1.0;
	n.xy *= normal_strength;
	return n;
}

void fragment() {
	vec3 col;
	vec3 nrm_obj;
	bool flip = TWO_SIDED && !FRONT_FACING;
	vec3 ln = normalize(lnormal) * (flip ? -1.0 : 1.0);
	if (triplanar) {
		vec3 w = pow(abs(ln), vec3(5.0));
		w /= (w.x + w.y + w.z);
		vec3 p = lpos * uv_scale;
		vec2 uvx = vec2(p.z * sign(ln.x), -p.y);
		vec2 uvy = vec2(p.x, p.z * sign(ln.y));
		vec2 uvz = vec2(-p.x * sign(ln.z), -p.y);
		col = vec3(0.0);
		nrm_obj = vec3(0.0);
		if (w.x > 0.01) {
			col += texture(albedo_tex, uvx).rgb * w.x;
			vec3 t = unpack(texture(normal_tex, uvx));
			nrm_obj += vec3(t.z * sign(ln.x), -t.y, -t.x * sign(ln.x)) * w.x;
		}
		if (w.y > 0.01) {
			col += texture(albedo_tex, uvy).rgb * w.y;
			vec3 t = unpack(texture(normal_tex, uvy));
			nrm_obj += vec3(t.x, t.z * sign(ln.y), -t.y * sign(ln.y)) * w.y;
		}
		if (w.z > 0.01) {
			col += texture(albedo_tex, uvz).rgb * w.z;
			vec3 t = unpack(texture(normal_tex, uvz));
			nrm_obj += vec3(-t.x * sign(ln.z), -t.y, t.z * sign(ln.z)) * w.z;
		}
		// Keep the bumps but anchor them to the surface's own normal.
		nrm_obj = normalize(ln * 1.0 + (nrm_obj - ln * dot(nrm_obj, ln)));
		vec3 wn = normalize((MODEL_MATRIX * vec4(nrm_obj, 0.0)).xyz);
		NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	} else {
		vec2 uv = UV * uv_scale;
		col = texture(albedo_tex, uv).rgb;
		NORMAL_MAP = texture(normal_tex, uv).rgb;
		NORMAL_MAP_DEPTH = normal_strength;
		if (flip) {
			NORMAL = -NORMAL;
		}
	}
	col *= tint;
	float rough = roughness_base;
	vec3 wn_geo = normalize(wnormal) * (flip ? -1.0 : 1.0);

	// Colour wander over metres: a little lighter/darker, warmer/cooler.
	float big = fbm(wpos.xz * 0.11 + wpos.y * 0.07);
	float mid = fbm(vec2(wpos.x + wpos.z, wpos.y) * 0.55);
	col *= mix(vec3(1.0), mix(0.86, 1.08, big) * mix(vec3(0.97, 0.99, 1.03), vec3(1.04, 1.0, 0.94), mid), weathering);

	// Rain streaks down vertical faces: long thin darker runs.
	float vertical = 1.0 - abs(wn_geo.y);
	float along = dot(wpos.xz, normalize(vec2(-wn_geo.z, wn_geo.x) + 1e-4));
	float streak = smoothstep(0.55, 0.85, noise(vec2(along * 6.0, wpos.y * 0.35)) * 0.7 + noise(vec2(along * 1.3, 3.1)) * 0.5);
	col *= 1.0 - 0.16 * streak * vertical * weathering;

	// Damp and splashed dirt at the foot of the walls.
	float h = wpos.y - base_y;
	if (h > -2.0 && h < damp_height * 2.5) {
		float edge = damp_height * (0.7 + 0.6 * fbm(vec2(along, wpos.y) * 1.3 + wpos.xz * 0.2));
		float damp = 1.0 - smoothstep(edge * 0.35, edge, h);
		damp *= weathering * vertical;
		col = mix(col, col * vec3(0.66, 0.62, 0.55), damp * 0.85);
		// Splashes from the rain off the ground, the lowest hand's breadth.
		float splash = (1.0 - smoothstep(0.0, 0.22, h)) * smoothstep(0.35, 0.7, noise(vec2(along, wpos.y) * 9.0));
		col = mix(col, vec3(0.3, 0.26, 0.2), splash * 0.45 * weathering * vertical);
		rough = mix(rough, min(rough + 0.08, 1.0), damp);
	}

	// Moss and lichen where rain and dew stay: faces turned up, in patches, greener in damp corners.
	float up = smoothstep(0.35, 0.8, wn_geo.y);
	float patch = smoothstep(0.5, 0.75, fbm(wpos.xz * 0.9 + 11.0) + noise(wpos.xz * 7.0) * 0.25);
	float mossy = clamp(up * patch * moss, 0.0, 1.0);
	vec3 moss_col = mix(vec3(0.22, 0.27, 0.1), vec3(0.36, 0.4, 0.16), noise(wpos.xz * 13.0));
	col = mix(col, moss_col * (0.75 + 0.5 * dot(col, vec3(0.333))), mossy * 0.75);
	rough = mix(rough, 0.95, mossy);

	ALBEDO = col;
	ROUGHNESS = rough;
	SPECULAR = 0.35;
}
