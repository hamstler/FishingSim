shader_type canvas_item;
// Pixel-art finish for the low-resolution world image: snaps to the source pixel grid, adds dark outlines where
// brightness jumps (object silhouettes), boosts saturation a little and reduces the colours with ordered
// (Bayer) dithering, like a limited palette.

uniform vec2 src_size = vec2(426.0, 240.0);
uniform float levels = 20.0;
uniform float dither = 0.7;
uniform float outline = 0.45;
uniform float edge_threshold = 0.14;
uniform float saturation = 1.18;

float bayer4(vec2 p) {
	int x = int(mod(p.x, 4.0));
	int y = int(mod(p.y, 4.0));
	int i = x + y * 4;
	int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[i]) + 0.5) / 16.0;
}

float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

vec3 fetch(vec2 px) { return texture(TEXTURE, (px + 0.5) / src_size).rgb; }

void fragment() {
	vec2 px = floor(UV * src_size);
	vec3 c = fetch(px);
	float l = luma(c);
	// Outline the brighter side of a strong edge with the darker neighbour's colour (keeps silhouettes crisp).
	float ln = luma(fetch(px + vec2(0.0, -1.0)));
	float ls = luma(fetch(px + vec2(0.0, 1.0)));
	float le = luma(fetch(px + vec2(1.0, 0.0)));
	float lw = luma(fetch(px + vec2(-1.0, 0.0)));
	float darkest = min(min(ln, ls), min(le, lw));
	float edge = smoothstep(edge_threshold, edge_threshold * 2.0, l - darkest);
	c *= 1.0 - edge * outline;

	c = mix(vec3(luma(c)), c, saturation);
	float b = bayer4(px) - 0.5;
	c = floor(c * levels + b * dither + 0.5) / levels;
	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
