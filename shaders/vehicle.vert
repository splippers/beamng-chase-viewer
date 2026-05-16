#version 450

// Input: [px, py, pz, nx, ny, nz, r, g, b, a]
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inColor;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 viewProj;
} pc;

layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out vec3 fragWorldPos;

void main() {
    vec4 world = pc.model * vec4(inPosition, 1.0);
    gl_Position = pc.viewProj * world;
    fragWorldPos = world.xyz;
    fragNormal   = normalize(mat3(pc.model) * inNormal);
    fragColor    = inColor;
}
