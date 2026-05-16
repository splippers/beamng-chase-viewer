#version 450

layout(location = 0) in vec2 fragUV;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    float vignetteStrength;  // 0 = none, 1 = heavy
    float flashAlpha;        // white flash (0 = none, 1 = full white)
    float heartbeat;         // red pulse 0-1
} pc;

void main() {
    vec2  centred  = fragUV * 2.0 - 1.0;
    float dist     = length(centred);

    // Vignette: dark edges, motion sickness reduction + threat atmosphere
    float vig  = smoothstep(0.4, 1.4, dist) * pc.vignetteStrength;

    // Heartbeat: red tint from edges inward
    float hb   = smoothstep(0.5, 1.2, dist) * pc.heartbeat * 0.6;

    // Compose: dark vignette + red heartbeat + white flash
    vec4 color = vec4(hb, 0.0, 0.0, vig + hb);
    color      = mix(color, vec4(1.0, 1.0, 1.0, 1.0), pc.flashAlpha);

    outColor   = color;
}
