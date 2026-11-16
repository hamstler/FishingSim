shader_type spatial;
// Weathered photo materials of buildings and props, one-sided (see building.gdshaderinc).
render_mode diffuse_burley, specular_schlick_ggx, cull_back;
#define TWO_SIDED false
#include "res://shaders/building.gdshaderinc"
