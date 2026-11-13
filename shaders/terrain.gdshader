shader_type spatial;
// Painted ground: seven 2 m layers (World/Props/GroundTextures: meadow, forest floor, sand, dirt path, lake bed, rock,
// snow) blended by the weights the terrain mesh carries (UV = sand, path; UV2 = forest floor, lake bed; the meadow
// is the rest), rock on steep or high ground (triplanar) and snow on the peaks. The layers meet by height, so grass
// grows over the sand and pebbles poke out of the silt. Each layer is read at two scales and turns, mixed by a large
// noise, so the tile does not repeat visibly. The map's own colours tint the layers (layer_tint: multipliers that
// bring each layer's average to the map's colour). Wet band at the waterline, caustics on the lake bottom.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2DArray ground_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray ground_normal : filter_linear_mipmap_anisotropic, repeat_enable;     // RG: tilt along x, z (BC5)
uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
uniform sampler2D caustics_tex : repeat_enable, filter_linear_mipmap;
uniform vec3 layer_tint[7];
uniform float layer_rough[7];
uniform vec3 dark_grass_tint = vec3(1.0);
uniform float tile = 2.0;
uniform float bump = 1.0;
// The water's clock (set once per frame by Water as a global): the caustics drift with it.
global uniform float water_time;
uniform float daylight = 1.0;
uniform float water_level = 0.0;
uniform float snow_line = 26.0;
uniform float rock_line = 10.5;

varying vec3 wpos;
varying vec3 wnormal;
varying vec4 wts;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnormal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	wts = vec4(UV, UV2);
}

vec2 turn(vec2 p, float a) {
	float c = cos(a), s = sin(a);
	return vec2(c * p.x - s * p.y, s * p.x + c * p.y);
}

void fragment() {
	vec3 n = normalize(wnormal);
	float macro = texture(noise_tex, wpos.xz * 0.013).r;
	float mid = texture(noise_tex, wpos.xz * 0.09 + vec2(0.37, 0.11)).r;
	float fine = texture(noise_tex, wpos.xz * 0.43 + vec2(0.71, 0.29)).r;
	float slope = 1.0 - n.y;

	// ---- layer weights
	vec4 g = clamp(wts, 0.0, 1.0);
	// Let the borders between grounds wander (only where two grounds already meet), so they never follow the
	// straight edges of the terrain's 2.5 m triangles.
	float finer = texture(noise_tex, wpos.xz * 1.7 + vec2(0.13, 0.57)).r;
	float wander = fine * 0.55 + finer * 0.45 - 0.5;
	g += vec4(wander, mid - 0.5 + (finer - 0.5) * 0.5, wander, wander) * g * (1.0 - g) * 4.5;
	g = clamp(g, 0.0, 1.0);
	float rock = max(smoothstep(0.30, 0.50, slope + (mid - 0.5) * 0.3),
		smoothstep(rock_line, rock_line + 5.5, wpos.y + (mid - 0.5) * 8.0) * smoothstep(0.35, 0.65, fine * 0.7 + mid * 0.5));
	// Snow lies on the gentler ground; steep faces stay bare rock.
	float snow = smoothstep(snow_line - 1.0, snow_line + 5.0, wpos.y + (mid - 0.5) * 6.0) * (1.0 - smoothstep(0.3, 0.5, slope + (fine - 0.5) * 0.2));
	float w[7];
	w[0] = max(1.0 - g.x - g.y - g.z - g.w, 0.0);
	w[1] = g.z;
	w[2] = g.x;
	w[3] = g.y;
	w[4] = g.w;
	for (int i = 0; i < 5; i++) {
		w[i] *= (1.0 - rock) * (1.0 - snow);
	}
	w[5] = rock * (1.0 - snow);
	w[6] = snow;

	// ---- two readings of every layer against visible tiling
	vec2 uvA = wpos.xz / tile;
	vec2 uvB = turn(wpos.xz, 0.83) / (tile * 1.37) + vec2(0.31, 0.67);
	vec2 dAx = dFdx(uvA), dAy = dFdy(uvA), dBx = dFdx(uvB), dBy = dFdy(uvB);
	float mixB = smoothstep(0.4, 0.6, texture(noise_tex, wpos.xz * 0.027 + vec2(0.5, 0.2)).r);
	// Rock: triplanar.
	vec3 tw = pow(abs(n), vec3(4.0));
	tw /= (tw.x + tw.y + tw.z);
	vec2 uvX = wpos.zy / tile, uvZ = wpos.xy / tile;
	vec2 dXx = dFdx(uvX), dXy = dFdy(uvX), dZx = dFdx(uvZ), dZy = dFdy(uvZ);

	vec4 alb[7];
	float s[7];
	float top = -1.0;
	for (int i = 0; i < 7; i++) {
		alb[i] = vec4(0.0);
		s[i] = -1.0;
		if (w[i] < 0.002) {
			continue;
		}
		float L = float(i);
		if (i == 5) {
			if (tw.y > 0.01) alb[i] += textureGrad(ground_albedo, vec3(uvA, L), dAx, dAy) * tw.y;
			if (tw.x > 0.01) alb[i] += textureGrad(ground_albedo, vec3(uvX, L), dXx, dXy) * tw.x;
			if (tw.z > 0.01) alb[i] += textureGrad(ground_albedo, vec3(uvZ, L), dZx, dZy) * tw.z;
		} else {
			if (mixB < 0.999) alb[i] += textureGrad(ground_albedo, vec3(uvA, L), dAx, dAy) * (1.0 - mixB);
			if (mixB > 0.001) alb[i] += textureGrad(ground_albedo, vec3(uvB, L), dBx, dBy) * mixB;
		}
		s[i] = w[i] + alb[i].a * 0.45;
		top = max(top, s[i]);
	}

	// ---- height blend
	vec3 base = vec3(0.0);
	vec3 tilt = vec3(0.0);
	float rough = 0.0;
	float total = 0.0;
	vec3 grass_tint = mix(layer_tint[0], dark_grass_tint, smoothstep(2.0, 8.0, wpos.y));
	for (int i = 0; i < 7; i++) {
		float b = max(s[i] - top + 0.16, 0.0);
		if (b <= 0.0) {
			continue;
		}
		float L = float(i);
		vec4 nm = vec4(0.0);
		vec3 t3 = vec3(0.0);
		if (i == 5) {
			if (tw.y > 0.01) { vec4 q = textureGrad(ground_normal, vec3(uvA, L), dAx, dAy); nm += q * tw.y; t3 += vec3(q.r * 2.0 - 1.0, 0.0, q.g * 2.0 - 1.0) * tw.y; }
			if (tw.x > 0.01) { vec4 q = textureGrad(ground_normal, vec3(uvX, L), dXx, dXy); nm += q * tw.x; t3 += vec3(0.0, q.g * 2.0 - 1.0, q.r * 2.0 - 1.0) * tw.x; }
			if (tw.z > 0.01) { vec4 q = textureGrad(ground_normal, vec3(uvZ, L), dZx, dZy); nm += q * tw.z; t3 += vec3(q.r * 2.0 - 1.0, q.g * 2.0 - 1.0, 0.0) * tw.z; }
		} else {
			if (mixB < 0.999) nm += textureGrad(ground_normal, vec3(uvA, L), dAx, dAy) * (1.0 - mixB);
			if (mixB > 0.001) {
				vec4 q = textureGrad(ground_normal, vec3(uvB, L), dBx, dBy);
				// The second reading is turned: turn its tilt back into the world.
				vec2 tb = turn(q.rg * 2.0 - 1.0, -0.83) * 0.5 + 0.5;
				nm += vec4(tb, 0.0, 0.0) * mixB;
			}
			t3 = vec3(nm.r * 2.0 - 1.0, 0.0, nm.g * 2.0 - 1.0);
		}
		vec3 tint = i == 0 ? grass_tint : layer_tint[i];
		base += alb[i].rgb * tint * b;
		tilt += t3 * b;
		rough += layer_rough[i] * b;
		total += b;
	}
	base /= total;
	tilt /= total;
	rough /= total;

	// ---- large-scale variation (so the ground is never one flat colour across a field)
	float green = w[0] + w[1];
	base *= mix(0.86, 1.1, macro);
	base *= mix(vec3(1.0), mix(vec3(1.06, 1.03, 0.86), vec3(0.9, 1.0, 0.95), mid), clamp(green, 0.0, 1.0));

	// ---- water: wet band above the line, darker with depth below it
	float height = wpos.y - water_level;
	float wet = 1.0 - smoothstep(0.02, 0.45 + mid * 0.2, height);
	base *= mix(1.0, 0.6, wet);
	base = mix(base, base * 0.3, clamp(-height / 7.0, 0.0, 1.0));
	rough = mix(rough, 0.35, wet * (1.0 - step(height, -0.05)));

	vec3 pn = normalize(n + tilt * bump);
	NORMAL = normalize((VIEW_MATRIX * vec4(pn, 0.0)).xyz);

	// Caustics: sunlight focused by the waves, dancing on the shallow bottom.
	if (height < -0.02) {
		vec2 cuv = wpos.xz * 0.22;
		float c1 = texture(caustics_tex, cuv + vec2(water_time * 0.03, water_time * 0.02)).r;
		float c2 = texture(caustics_tex, cuv * 1.3 - vec2(water_time * 0.025, water_time * 0.035)).r;
		float caustic = smoothstep(0.55, 0.75, min(c1, c2)) * 0.8;
		float depth_fade = exp(height * 0.6);
		EMISSION = vec3(0.9, 1.0, 0.95) * caustic * depth_fade * daylight * 0.35 * base;
	}

	ALBEDO = base;
	ROUGHNESS = rough;
	SPECULAR = 0.3;
}
