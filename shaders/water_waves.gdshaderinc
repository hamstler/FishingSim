// The lake's wind waves, shared by water.gdshader, floating.gdshader and rise_ring.gdshader; World/Water.cs mirrors
// it exactly (Water.HeightAt: the bobber rides these waves). Keep the table and the functions in step with Water.cs.
//
// Gerstner (trochoidal) waves: every wave moves the water in circles, so crests are sharper and troughs flatter than
// a sine; each runs at the deep-water speed for its length (omega = sqrt(g k)) in a fan of directions round the wind.
// Waves 0..3 are long enough for the 1 m water mesh and move its vertices (and everything floating); 4..6 are short
// chop that only shapes the light (normals).

global uniform float water_time;
global uniform float water_amp;
global uniform vec2 water_dir;
global uniform float water_chop;

const int WAVES = 7;
const int VERTEX_WAVES = 4;
// x = angle from the wind (rad), y = wavelength (m), z = share of the amplitude, w = phase.
const vec4 WAVE[7] = vec4[7](
	vec4(0.0, 9.0, 0.40, 0.0),
	vec4(0.49, 6.2, 0.26, 1.7),
	vec4(-0.58, 4.4, 0.18, 4.1),
	vec4(1.08, 3.1, 0.10, 2.6),
	vec4(-0.26, 1.9, 0.05, 5.3),
	vec4(0.79, 1.25, 0.035, 0.9),
	vec4(-1.22, 0.8, 0.025, 3.7));

vec2 wave_direction(int i) {
	float a = WAVE[i].x;
	vec2 w = normalize(water_dir);
	return vec2(w.x * cos(a) - w.y * sin(a), w.x * sin(a) + w.y * cos(a));
}

// x, z: the horizontal sway of the water at rest point p; y: the height. Waves [first, last).
vec3 gerstner(vec2 p, int first, int last) {
	vec3 d = vec3(0.0);
	for (int i = first; i < last; i++) {
		float k = 6.2831853 / WAVE[i].y;
		float omega = sqrt(9.81 * k);
		vec2 dir = wave_direction(i);
		float f = k * dot(dir, p) - omega * water_time + WAVE[i].w;
		float a = water_amp * WAVE[i].z;
		float q = water_chop * a;
		d.xz += dir * q * cos(f);
		d.y += a * sin(f);
	}
	return d;
}

// The surface's slope (dh/dx, dh/dz) and its horizontal squeeze (< 1 on the crests) at rest point p.
vec3 gerstner_slope(vec2 p, int first, int last) {
	vec2 s = vec2(0.0);
	float squeeze = 1.0;
	for (int i = first; i < last; i++) {
		float k = 6.2831853 / WAVE[i].y;
		float omega = sqrt(9.81 * k);
		vec2 dir = wave_direction(i);
		float f = k * dot(dir, p) - omega * water_time + WAVE[i].w;
		float ka = k * water_amp * WAVE[i].z;
		s += dir * ka * cos(f);
		squeeze -= water_chop * ka * sin(f);
	}
	return vec3(s / max(squeeze, 0.2), squeeze);
}

// The water's height over a fixed point (x, z) of the world: the rest point that sways there, found by one step back.
float water_height(vec2 p) {
	vec2 rest = p - gerstner(p, 0, VERTEX_WAVES).xz;
	return gerstner(rest, 0, VERTEX_WAVES).y;
}
