// The swimming wave shared by a close-up fish's body, fins and tail (model space: unit length, nose at +z 0.5,
// tail tip at -0.5). A wave travels from the head to the tail, its amplitude growing towards the tail; the head
// swings a touch the other way. wiggle_amount = how hard the fish swims (0 = still).
instance uniform float wiggle_amount = 0.0;
instance uniform float swim_phase = 0.0;

vec3 fish_bend(vec3 v, float time) {
	float s = clamp((0.3 - v.z) / 0.8, 0.0, 1.0);
	float wave = sin(time * 9.0 + swim_phase - v.z * 7.5);
	float amp = wiggle_amount * (0.003 + 0.075 * s * s);
	v.x += amp * wave;
	v.x -= wiggle_amount * 0.006 * (1.0 - s) * wave;
	return v;
}
