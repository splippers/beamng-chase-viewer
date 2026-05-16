#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec2 fragUV;
layout(location = 2) in float fragHeight;

layout(push_constant) uniform PushConstants {
    mat4 viewProj;
    vec4 grassColor;
    vec4 rockColor;
} pc;

layout(location = 0) out vec4 outColor;

const vec3 SUN_DIR   = normalize(vec3(0.4, 1.0, 0.6));
const vec3 SUN_COLOR = vec3(1.0, 0.95, 0.85);
const vec3 AMB_COLOR = vec3(0.2, 0.25, 0.3);

void main() {
    // Slope-based blend: flat=grass, steep=rock
    float slope  = 1.0 - abs(dot(fragNormal, vec3(0,1,0)));
    vec4  base   = mix(pc.grassColor, pc.rockColor, smoothstep(0.3, 0.7, slope));

    float diff   = max(dot(fragNormal, SUN_DIR), 0.0);
    vec3  lit    = base.rgb * (AMB_COLOR + SUN_COLOR * diff);
    outColor     = vec4(lit, 1.0);
}
