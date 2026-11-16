shader_type spatial;
// Weathered photo materials seen from both sides: roofs, canvas (see building.gdshaderinc).
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;
#define TWO_SIDED true
#include "res://shaders/building.gdshaderinc"
