using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.Audio
{
    /// <summary>
    /// Drives controller haptics to simulate spatial audio:
    ///   - Left controller: ground vibration (omnidirectional rumble, distance-based)
    ///   - Right controller: directional cue (pulses when threat is to the right)
    ///
    /// Also tracks engine "sound state" values that the renderer can use for
    /// audio middleware integration (e.g. Android AudioTrack) when added later.
    /// </summary>
    public sealed class SpatialAudioManager
    {
        private readonly ThreatTracker _threat;

        // Published to renderer / audio backend
        public float EngineVolume    { get; private set; }   // 0-1
        public float EnginePitch     { get; private set; } = 1f;  // normalised RPM ratio
        public float DirectionAngle  { get; private set; }   // radians, bearing to threat
        public float GroundRumble    { get; private set; }   // 0-1 haptic intensity

        // Heartbeat state
        private float _heartbeatPhase;
        private float _heartbeatRate = 1f;  // Hz

        public float HeartbeatPulse { get; private set; }  // 0-1 per pulse

        public SpatialAudioManager(ThreatTracker threat) => _threat = threat;

        public void Update(float dt, Quaternion playerRot)
        {
            float intensity = _threat.Intensity;

            // Engine volume falls off with distance, boosted by vehicle speed
            float distFactor  = 1f / (1f + _threat.NearestDist * 0.05f);
            float speedFactor = Math.Clamp(_threat.PrimarySpeed / 20f, 0f, 1f);
            EngineVolume      = Math.Clamp(distFactor * (0.3f + speedFactor * 0.7f), 0f, 1f);

            // Pitch from approach speed (Doppler approximation)
            float doppler = 1f + Math.Clamp(_threat.ApproachSpeed / 30f, -0.3f, 0.5f);
            EnginePitch   = MathEx.Lerp(EnginePitch, doppler, dt * 3f);

            // Directional bearing
            DirectionAngle = _threat.ThreatBearing(playerRot);

            // Ground rumble: felt in both controllers, fades with distance
            GroundRumble = Math.Clamp(intensity * distFactor * 2f, 0f, 1f);

            // Heartbeat rate: 1 Hz at safe, 3.5 Hz at critical
            float targetRate = MathEx.Lerp(0.8f, 3.5f, intensity);
            _heartbeatRate   = MathEx.Lerp(_heartbeatRate, targetRate, dt * 2f);
            _heartbeatPhase += _heartbeatRate * dt;

            float raw = MathF.Sin(_heartbeatPhase * MathF.PI * 2f);
            HeartbeatPulse = Math.Clamp(raw > 0.6f ? (raw - 0.6f) / 0.4f : 0f, 0f, 1f);

            if (intensity < 0.05f) HeartbeatPulse = 0f;
        }
    }
}
