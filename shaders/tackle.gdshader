shader_type spatial;
// The rod's and reel's materials (Fishing/RodRig), procedural so they stay sharp at any distance from the eye:
// 0 lacquered carbon blank (twill weave under a clear coat), 1 cork (granules, pits and filler), 2 EVA foam (fine
// pores), 3 metal (brushed round the part, or polished), 4 anodised or graphite housing (fine flake), 5 line wound on
// the spool (cross-wound turns), 6 thread whipping (tight turns under varnish), 7 rubber. Patterns are laid out in the
// part's own metres (its local axis is Y, as for Godot's cylinders); relief is bump-mapped from screen derivatives.
render_mode diffuse_burley, specular_schlick_ggx;

uniform int kind = 0;
uniform vec3 base_color : source_color = vec3(0.24, 0.12, 0.09);
uniform float roughness_base = 0.4;
uniform float metallic_base = 0.0;
/// 0..1: a darker tone (cork filler rings, a smoked finish).
uniform float shade = 0.0;

varying vec3 lp;

void vertex() {
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	lp = VERTEX * sc;
}

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

// Distance to the nearest random cell centre (granules of cork, pores of foam).
float cells(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	float d = 8.0;
	for (int z = -1; z <= 1; z++)
		for (int y = -1; y <= 1; y++)
			for (int x = -1; x <= 1; x++) {
				vec3 o = vec3(float(x), float(y), float(z));
				vec3 c = o + vec3(hash(i + o), hash(i + o + 7.1), hash(i + o + 13.7));
				d = min(d, length(c - f));
			}
	return d;
}

// Bump mapping from screen-space derivatives of a height field h (metres): no tangents needed.
vec3 bump(vec3 n, vec3 pos, float h, float strength) {
	vec3 dpdx = dFdx(pos);
	vec3 dpdy = dFdy(pos);
	float dhdx = dFdx(h) * strength;
	float dhdy = dFdy(h) * strength;
	vec3 r1 = cross(dpdy, n);
	vec3 r2 = cross(n, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
	return normalize(abs(det) * n - grad);
}

void fragment() {
	float r = max(length(lp.xz), 0.0005);
	float around = atan(lp.z, lp.x) * r;        // arc length round the part (m)
	vec3 col = base_color * (1.0 - 0.45 * shade);
	float rough = roughness_base;
	float metal = metallic_base;
	float h = 0.0;
	float strength = 1.0;
	float ao = 1.0;
	if (kind == 0) {
		// Carbon twill (1.2 mm) seen through the lacquer: a faint weave in the colour and the sheen.
		float a = sin((lp.y + around) * 5200.0), b = sin((lp.y - around) * 5200.0);
		float weave = (a > 0.0 ? a : b) * 0.5 + 0.5;
		col *= 0.96 + 0.05 * weave;
		rough = roughness_base + 0.05 * weave;
		CLEARCOAT = 1.0;
		CLEARCOAT_ROUGHNESS = 0.04;
		h = weave * 0.000015;
	} else if (kind == 1) {
		// Cork: pressed granules of different tones with dark pits between them.
		float g = cells(lp * 650.0);
		float tone = noise(lp * 420.0);
		float pit = smoothstep(0.62, 0.86, cells(lp * 210.0 + 3.0));
		col *= mix(0.8, 1.1, tone) * (1.0 - 0.35 * pit);
		col = mix(col, col * vec3(0.8, 0.72, 0.6), smoothstep(0.35, 0.05, g) * 0.4);
		rough = 0.82 + 0.15 * pit;
		h = -pit * 0.0004 + g * 0.00012;
		ao = 1.0 - 0.4 * pit;
	} else if (kind == 2) {
		// EVA foam: a matte skin with fine pores.
		float pore = smoothstep(0.18, 0.05, cells(lp * 1300.0));
		col *= 1.0 - 0.5 * pore + 0.05 * noise(lp * 300.0);
		rough = 0.92;
		h = -pore * 0.0001;
	} else if (kind == 3) {
		// Metal brushed round the part (streaks along the circumference); polished when roughness_base is low.
		float streak = noise(vec3(around * 40.0, lp.y * 9000.0, r * 50.0));
		rough = clamp(roughness_base + 0.1 * (streak - 0.5), 0.04, 1.0);
		col *= 0.94 + 0.08 * streak;
		h = streak * 0.000008;
	} else if (kind == 4) {
		// Anodised or graphite housing: a fine flake that catches the light.
		float flake = hash(floor(lp * 6000.0));
		rough = roughness_base + 0.08 * (flake - 0.5);
		col *= 0.96 + 0.08 * flake;
		h = noise(lp * 900.0) * 0.00002;
	} else if (kind == 5) {
		// Line cross-wound on the spool: two sets of turns crossing at a shallow angle.
		float t1 = sin((lp.y * 1.0 + around * 0.12) * 9000.0);
		float t2 = sin((lp.y * 1.0 - around * 0.12) * 9000.0);
		float turns = max(t1, t2) * 0.5 + 0.5;
		col *= 0.82 + 0.22 * turns;
		rough = 0.35 + 0.15 * (1.0 - turns);
		h = turns * 0.00006;
	} else if (kind == 6) {
		// Thread whipping: tight turns round the blank under varnish.
		float turn = sin(lp.y * 15000.0) * 0.5 + 0.5;
		col *= 0.85 + 0.2 * turn;
		rough = 0.3;
		CLEARCOAT = 0.8;
		CLEARCOAT_ROUGHNESS = 0.08;
		h = turn * 0.00003;
	} else {
		// Rubber: matte, a faint grain.
		col *= 0.95 + 0.08 * noise(lp * 800.0);
		rough = 0.85;
	}
	NORMAL = bump(NORMAL, VERTEX, h, strength);
	ALBEDO = col;
	ROUGHNESS = rough;
	METALLIC = metal;
	AO = ao;
}
