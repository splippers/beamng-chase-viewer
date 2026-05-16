#version 450

// Same vertex format as vehicles: [px,py,pz, nx,ny,nz, r,g,b,a]
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inColor;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 viewProj;
    vec4 fogColor;
    float fogDensity;
    float emissive;  // > 0 for light heads — ignore lighting
} pc;

layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out float fragFogFactor;
layout(location = 3) out float fragEmissive;

void main() {
    vec4 world   = pc.model * vec4(inPosition, 1.0);
    gl_Position  = pc.viewProj * world;
    fragNormal   = normalize(mat3(pc.model) * inNormal);
    fragColor    = inColor;
    fragEmissive = pc.emissive;

    // Exponential fog based on distance
    float dist      = length(world.xyz);
    fragFogFactor   = exp(-pc.fogDensity * dist * dist);
}
