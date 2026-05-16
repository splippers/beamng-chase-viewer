#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in vec3 fragWorldPos;

layout(location = 0) out vec4 outColor;

// Simple directional + ambient lighting
const vec3 SUN_DIR   = normalize(vec3(0.4, 1.0, 0.6));
const vec3 SUN_COLOR = vec3(1.0, 0.95, 0.85);
const vec3 AMB_COLOR = vec3(0.25, 0.28, 0.35);

void main() {
    float diff = max(dot(fragNormal, SUN_DIR), 0.0);
    vec3 lit   = fragColor.rgb * (AMB_COLOR + SUN_COLOR * diff);
    outColor   = vec4(lit, fragColor.a);
}
