#version 450

// Input: [px, py, pz, nx, ny, nz, u, v]
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

layout(push_constant) uniform PushConstants {
    mat4 viewProj;
    vec4 grassColor;
    vec4 rockColor;
} pc;

layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec2 fragUV;
layout(location = 2) out float fragHeight;

void main() {
    gl_Position = pc.viewProj * vec4(inPosition, 1.0);
    fragNormal  = inNormal;
    fragUV      = inUV;
    fragHeight  = inPosition.y;
}
