shader_type spatial;
// Tree trunks: painted bark (World/Props/GroundTextures.Bark) tinted by the tree's bark colour (instance colour)
// so that the bark's average becomes that colour; relief from its normal map, cavities darker.
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D bark_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 bark_mean = vec3(0.3);
uniform float relief = 1.2;
uniform float roughness = 0.88;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	vec4 a = texture(bark_albedo, UV);
	vec4 n = texture(bark_normal, UV);
	vec3 tint = srgb_to_linear(COLOR.rgb);
	ALBEDO = clamp(tint * a.rgb / bark_mean, 0.0, 1.0);
	NORMAL_MAP = vec3(n.r, 1.0 - n.g, 1.0);     // the ground painter's green points down the picture; Godot's up
	NORMAL_MAP_DEPTH = relief;
	AO = mix(0.5, 1.0, a.a);      // the bark's height: deep furrows get less light
	AO_LIGHT_AFFECT = 0.2;
	ROUGHNESS = roughness;
	SPECULAR = 0.25;
}
