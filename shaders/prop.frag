#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in float fragFogFactor;
layout(location = 3) in float fragEmissive;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 viewProj;
    vec4 fogColor;
    float fogDensity;
    float emissive;
} pc;

layout(location = 0) out vec4 outColor;

const vec3 SUN_DIR   = normalize(vec3(0.3, 1.0, 0.5));
const vec3 SUN_COLOR = vec3(0.7, 0.6, 0.5);   // dim, overcast
const vec3 AMB_COLOR = vec3(0.08, 0.08, 0.12); // near-black ambient

void main() {
    vec3 lit;
    if (fragEmissive > 0.5) {
        // Street-light head: pure emissive, glows through fog slightly
        lit = fragColor.rgb * 1.5;
    } else {
        float diff = max(dot(fragNormal, SUN_DIR), 0.0);
        lit = fragColor.rgb * (AMB_COLOR + SUN_COLOR * diff);
    }

    // Blend to fog
    vec3 fogged = mix(pc.fogColor.rgb, lit, clamp(fragFogFactor, 0.0, 1.0));
    outColor    = vec4(fogged, fragColor.a);
}
