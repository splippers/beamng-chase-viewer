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
    float stamina;          // offset 44  — 0..1; bar drawn when < 1
    float showStamina;      // offset 48  — 0 or 1 (float bool)
    float _pad1, _pad2;     // offset 52, 56
} pc;                       // total 60 bytes (rounded to 64)

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

    // Stamina bar: thin strip at bottom, green → amber → red, visible when active
    if (pc.showStamina > 0.5 && fragUV.y > 0.935 && fragUV.y < 0.965) {
        float filled = step(fragUV.x, 0.03 + pc.stamina * 0.94);
        vec3  barCol = mix(vec3(1.0, 0.25, 0.0), vec3(0.0, 0.9, 0.2), pc.stamina);
        outColor     = mix(outColor, vec4(barCol, 1.0), filled * 0.88);
    }
}
