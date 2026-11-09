shader_type sky;
// Physically based sky (Rayleigh + Mie scattering, adapted from Godot's MIT-licensed PhysicalSkyMaterial) with a
// procedural cloud layer whose cover follows the weather. Only the sun is in the sky (the moon light is LightOnly).

uniform float rayleigh : hint_range(0, 64) = 2.0;
uniform vec4 rayleigh_color : source_color = vec4(0.18, 0.4, 0.85, 1.0);
uniform float mie : hint_range(0, 1) = 0.004;
uniform float mie_eccentricity : hint_range(-1, 1) = 0.8;
uniform vec4 mie_color : source_color = vec4(0.69, 0.729, 0.812, 1.0);
uniform float turbidity : hint_range(0, 1000) = 4.0;
/// The scattering model is physically pale; this restores the deep blue of a clear sky.
uniform float saturation = 1.7;
uniform float sun_disk_scale = 1.0;
uniform vec4 ground_color : source_color = vec4(0.1, 0.07, 0.034, 1.0);
uniform float exposure = 0.1;

uniform sampler2D cloud_noise : repeat_enable, filter_linear_mipmap;
uniform float cloud_cover : hint_range(0, 1) = 0.3;
// Cloud drift (seconds), a global parameter so updating it does not re-bake the sky radiance.
global uniform float cloud_drift;
uniform float night : hint_range(0, 1) = 0.0;
uniform vec3 night_color : source_color = vec3(0.004, 0.006, 0.014);

const vec3 UP = vec3(0.0, 1.0, 0.0);
const float SUN_ENERGY = 1000.0;
const float RAYLEIGH_ZENITH_SIZE = 8.4e3;
const float MIE_ZENITH_SIZE = 1.25e3;

float henyey_greenstein(float cos_theta, float g) {
	const float k = 0.0795774715459;
	return k * (1.0 - g * g) / (pow(1.0 + g * g - 2.0 * g * cos_theta, 1.5));
}

float clouds(vec2 uv) {
	float n = texture(cloud_noise, uv).r * 0.55
		+ texture(cloud_noise, uv * 2.03 + vec2(0.31, 0.17)).r * 0.28
		+ texture(cloud_noise, uv * 4.11 + vec2(0.71, 0.53)).r * 0.17;
	float threshold = mix(0.72, 0.18, cloud_cover);
	return smoothstep(threshold, threshold + 0.22, n);
}

void sky() {
	vec3 sun_dir = normalize(LIGHT0_DIRECTION);
	float cos_theta = dot(EYEDIR, sun_dir);
	vec3 color = vec3(0.0);
	float sun_up = 0.0;
	if (LIGHT0_ENABLED) {
		float zenith_angle = clamp(dot(UP, sun_dir), -1.0, 1.0);
		float sun_energy = max(0.0, 1.0 - exp(-((PI * 0.5) - acos(zenith_angle)))) * SUN_ENERGY * max(LIGHT0_ENERGY, 0.05);
		float sun_fade = 1.0 - clamp(1.0 - exp(sun_dir.y), 0.0, 1.0);
		sun_up = clamp(sun_dir.y * 4.0 + 0.3, 0.0, 1.0);

		float rayleigh_coefficient = rayleigh - (1.0 * (1.0 - sun_fade));
		vec3 rayleigh_beta = rayleigh_coefficient * rayleigh_color.rgb * 0.0001;
		vec3 mie_beta = turbidity * mie * mie_color.rgb * 0.000434;

		float zenith = acos(max(0.0, dot(UP, EYEDIR)));
		float optical_mass = 1.0 / (cos(zenith) + 0.15 * pow(93.885 - degrees(zenith), -1.253));
		float rayleigh_scatter = RAYLEIGH_ZENITH_SIZE * optical_mass;
		float mie_scatter = MIE_ZENITH_SIZE * optical_mass;
		vec3 extinction = exp(-(rayleigh_beta * rayleigh_scatter + mie_beta * mie_scatter));

		float rayleigh_phase = (3.0 / (16.0 * PI)) * (1.0 + pow(cos_theta * 0.5 + 0.5, 2.0));
		vec3 beta_r_theta = rayleigh_beta * rayleigh_phase;
		float mie_phase = henyey_greenstein(cos_theta, mie_eccentricity);
		vec3 beta_m_theta = mie_beta * mie_phase;

		vec3 lin = pow(sun_energy * ((beta_r_theta + beta_m_theta) / (rayleigh_beta + mie_beta)) * (1.0 - extinction), vec3(1.5));
		lin *= mix(vec3(1.0), pow(sun_energy * ((beta_r_theta + beta_m_theta) / (rayleigh_beta + mie_beta)) * extinction, vec3(0.5)),
			clamp(pow(1.0 - zenith_angle, 5.0), 0.0, 1.0));
		lin *= mix(ground_color.rgb, vec3(1.0), smoothstep(-0.1, 0.1, dot(UP, EYEDIR)));

		float size = max(LIGHT0_SIZE, 0.0093) * sun_disk_scale;
		float sundisk = smoothstep(cos(size), cos(size * 0.5), cos_theta) * (1.0 - cloud_cover * 0.9);
		vec3 l0 = (sun_energy * 1900.0 * extinction) * sundisk * LIGHT0_COLOR;
		l0 += vec3(0.1) * extinction;

		color = (lin + l0) * 0.04;
		color = pow(color, vec3(1.0 / (1.2 + (1.2 * sun_fade))));
		color *= exposure;
		float l = dot(color, vec3(0.3, 0.59, 0.11));
		// Full boost for a blue day sky, much less at sunrise and sunset (or the whole sky turns blood red).
		float sat = mix(1.0, saturation, smoothstep(0.02, 0.4, sun_dir.y));
		color = max(mix(vec3(l), color, sat), vec3(0.0));
		// At dusk the upper sky turns deep blue; the red glow stays low around the sun and the horizon.
		float dusk = 1.0 - smoothstep(0.05, 0.35, sun_dir.y);
		float high = smoothstep(0.05, 0.6, EYEDIR.y);
		float glow = pow(max(cos_theta, 0.0), 6.0);
		vec3 twilight_blue = vec3(0.035, 0.06, 0.15) + vec3(l) * vec3(0.4, 0.5, 0.9);
		color = mix(color, twilight_blue, dusk * high * (1.0 - glow) * 0.8);
	}
	color += night_color * night;

	// Overcast: the whole dome greys out, darker when the sun is low.
	float lum = dot(color, vec3(0.3, 0.59, 0.11));
	vec3 overcast = vec3(0.62, 0.65, 0.7) * max(lum * 1.1, 0.002 + 0.25 * sun_up);
	color = mix(color, overcast, cloud_cover * cloud_cover * 0.85);

	// Cloud layer: a flat sheet projected onto the dome, lit by the sun with a silver lining.
	if (EYEDIR.y > 0.0 && !AT_CUBEMAP_PASS) {
		vec2 uv = EYEDIR.xz / (EYEDIR.y + 0.08) * 0.08 + vec2(cloud_drift * 0.004, cloud_drift * 0.0017);
		float density = clouds(uv);
		float shade = clouds(uv + sun_dir.xz * 0.02);     // denser towards the sun = darker base
		vec3 lit = vec3(1.0, 0.97, 0.94) * mix(0.25, 1.05, sun_up) * LIGHT0_COLOR;
		vec3 base = mix(lit, lit * 0.45 + vec3(0.04, 0.05, 0.07), shade * 0.8);
		base += LIGHT0_COLOR * pow(max(cos_theta, 0.0), 12.0) * (1.0 - density) * 1.5 * sun_up;
		base = mix(base, overcast * 0.9, cloud_cover * 0.6);
		base *= mix(1.0, 0.04, night);
		float fade = smoothstep(0.0, 0.25, EYEDIR.y);
		color = mix(color, base * 0.9, density * fade * 0.92);
	} else if (EYEDIR.y > 0.0) {
		// Cubemap pass (lighting/reflections): only the average effect of the clouds.
		vec3 overcast_flat = vec3(0.62, 0.65, 0.7) * (0.02 + 0.6 * sun_up);
		color = mix(color, overcast_flat, cloud_cover * 0.35);
	}
	COLOR = color;
}
