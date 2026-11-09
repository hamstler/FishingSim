shader_type canvas_item;
// Finish for the world image. Two styles:
//  * palette (pixel art): every pixel is mapped to a hand-picked warm palette (sky blues, honey woods, cream lights,
//    leafy greens, navy-violet shadows) with ordered dithering, like a hand-pixelled scene;
//  * sharp: a light posterize and a luminance outline at native resolution.
// Geometry outlines come from the depth/normal pass in the 3D view (shaders/outline.gdshader).

uniform vec2 src_size = vec2(426.0, 240.0);
uniform bool use_palette = true;
uniform float levels = 32.0;
uniform float dither = 0.45;
uniform float outline = 0.0;
uniform float edge_threshold = 0.2;
uniform float saturation = 1.12;
uniform float palette_dither = 0.022;

const int PAL_N = 61;
const vec3 PAL[PAL_N] = vec3[](
	// darks and shadows (navy, violet, brown)
	vec3(0.102, 0.078, 0.137), vec3(0.169, 0.114, 0.102), vec3(0.118, 0.165, 0.227), vec3(0.227, 0.184, 0.310),
	vec3(0.341, 0.275, 0.431), vec3(0.490, 0.416, 0.565),
	// wood ramp
	vec3(0.231, 0.141, 0.086), vec3(0.353, 0.204, 0.125), vec3(0.478, 0.290, 0.157), vec3(0.608, 0.392, 0.200),
	vec3(0.725, 0.510, 0.271), vec3(0.839, 0.639, 0.361), vec3(0.925, 0.780, 0.533),
	// oranges, golds and reds
	vec3(0.769, 0.388, 0.169), vec3(0.878, 0.529, 0.227), vec3(0.941, 0.678, 0.306), vec3(0.969, 0.831, 0.416),
	vec3(0.478, 0.165, 0.149), vec3(0.659, 0.227, 0.173), vec3(0.816, 0.325, 0.227),
	// skin, cream, white
	vec3(0.949, 0.816, 0.643), vec3(0.984, 0.914, 0.784), vec3(1.0, 0.969, 0.902), vec3(1.0, 1.0, 1.0),
	// greens
	vec3(0.122, 0.227, 0.141), vec3(0.180, 0.353, 0.173), vec3(0.263, 0.478, 0.196), vec3(0.373, 0.604, 0.227),
	vec3(0.525, 0.722, 0.290), vec3(0.706, 0.839, 0.416),
	// muted foliage: olive and blue-green conifers
	vec3(0.200, 0.255, 0.149), vec3(0.290, 0.333, 0.180), vec3(0.400, 0.447, 0.220), vec3(0.133, 0.220, 0.200),
	vec3(0.204, 0.302, 0.267), vec3(0.302, 0.400, 0.329),
	// water teals
	vec3(0.071, 0.208, 0.290), vec3(0.110, 0.329, 0.439), vec3(0.165, 0.471, 0.588), vec3(0.247, 0.604, 0.706),
	vec3(0.424, 0.753, 0.816),
	// sky blues
	vec3(0.290, 0.561, 0.831), vec3(0.416, 0.682, 0.902), vec3(0.561, 0.796, 0.949), vec3(0.749, 0.890, 0.969),
	vec3(0.910, 0.965, 0.984), vec3(0.329, 0.447, 0.580),
	// warm plaster greys and sunlit leaves
	vec3(0.859, 0.820, 0.741), vec3(0.741, 0.690, 0.620), vec3(0.620, 0.620, 0.302), vec3(0.502, 0.553, 0.251),
	// stone greys (slightly warm)
	vec3(0.239, 0.227, 0.251), vec3(0.361, 0.345, 0.376), vec3(0.502, 0.478, 0.502), vec3(0.659, 0.635, 0.635),
	vec3(0.812, 0.784, 0.761),
	// night and dusk accents
	vec3(0.051, 0.059, 0.110), vec3(0.090, 0.110, 0.200), vec3(0.165, 0.157, 0.290), vec3(0.420, 0.243, 0.341),
	vec3(0.690, 0.380, 0.400)
);

float bayer4(vec2 p) {
	int x = int(mod(p.x, 4.0));
	int y = int(mod(p.y, 4.0));
	int i = x + y * 4;
	int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[i]) + 0.5) / 16.0;
}

float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

vec3 fetch(sampler2D tex, vec2 px) { return texture(tex, (px + 0.5) / src_size).rgb; }

vec3 nearest_palette(vec3 c) {
	vec3 best = PAL[0];
	float bd = 1e9;
	for (int i = 0; i < PAL_N; i++) {
		vec3 d = c - PAL[i];
		// Perceptual-ish weighting: the eye is most sensitive to green, least to blue.
		float dist = d.r * d.r * 0.30 + d.g * d.g * 0.59 + d.b * d.b * 0.11;
		if (dist < bd) {
			bd = dist;
			best = PAL[i];
		}
	}
	return best;
}

void fragment() {
	vec2 px = floor(UV * src_size);
	vec3 c = fetch(TEXTURE, px);
	if (outline > 0.0) {
		float l = luma(c);
		float darkest = min(min(luma(fetch(TEXTURE, px + vec2(0.0, -1.0))), luma(fetch(TEXTURE, px + vec2(0.0, 1.0)))),
			min(luma(fetch(TEXTURE, px + vec2(1.0, 0.0))), luma(fetch(TEXTURE, px + vec2(-1.0, 0.0)))));
		c *= 1.0 - smoothstep(edge_threshold, edge_threshold * 2.0, l - darkest) * outline;
	}
	c = mix(vec3(luma(c)), c, saturation);
	float b = bayer4(px) - 0.5;
	if (use_palette)
		c = nearest_palette(clamp(c + b * palette_dither, 0.0, 1.0));
	else
		c = floor(c * levels + b * dither + 0.5) / levels;
	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
