#version 450

layout(location = 0) in vec2 fragUV;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PC {
    float vignetteStrength; // offset  0
    float flashAlpha;       // offset  4
    float heartbeat;        // offset  8
    float overlayAlpha;     // offset 12
    vec3  flashColor;       // offset 16  (16-byte aligned)
    float _pad0;            // offset 28
    vec3  overlayColor;     // offset 32  (16-byte aligned)
    float _pad1;            // offset 44
} pc;                       // total 48 bytes

void main() {
    vec2  centred = fragUV * 2.0 - 1.0;
    float dist    = length(centred);

    // Edge darkening — motion-sickness mitigation + threat atmosphere
    float vig = smoothstep(0.4, 1.4, dist) * pc.vignetteStrength;

    // Heartbeat: red pulse radiating inward from edges
    float hb  = smoothstep(0.5, 1.2, dist) * pc.heartbeat * 0.6;

    // Base: vignette darkness + red heartbeat ring
    vec4 base = vec4(hb, 0.0, 0.0, vig + hb);

    // Full-screen colored flash (red=caught, green=escaped)
    base = mix(base, vec4(pc.flashColor, 1.0), pc.flashAlpha);

    // Overlay: dark semi-transparent panel for results screen
    base = mix(base, vec4(pc.overlayColor, 1.0), pc.overlayAlpha);

    outColor = base;
}
