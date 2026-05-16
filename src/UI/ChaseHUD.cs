using BeamQuest.Audio;
using BeamQuest.Chase;
using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.UI
{
    /// <summary>
    /// Chase mode HUD — minimal, horror-atmosphere-first.
    ///
    /// Elements (all world-space, rendered as geometry by WorldRenderer):
    ///   - Heartbeat ring    : thin circle at eye level, pulses red with heartbeat
    ///   - Threat arrow      : small directional indicator at bottom of view
    ///   - Stamina bar       : dim horizontal bar, only visible when sprinting
    ///   - Survival clock    : top-left corner text (minimal, monospace style)
    ///   - Caught/escaped    : full-screen flash on state change
    /// </summary>
    public sealed class ChaseHUD
    {
        private readonly ThreatTracker     _threat;
        private readonly SpatialAudioManager _audio;
        private readonly ChaseGameState    _state;
        private readonly PlayerCharacter   _player;

        // Readable state for renderer
        public float HeartbeatPulse  => _audio.HeartbeatPulse;
        public float ThreatIntensity => _threat.Intensity;
        public float ThreatBearing   => _audio.DirectionAngle;
        public float StaminaFraction => _player.Stamina;
        public bool  ShowStamina     => _player.IsSprinting || _player.Stamina < 0.5f;
        public float SurvivalSeconds => _state.SurvivalSeconds;
        public ChasePhase Phase      => _state.Phase;

        // Screen-flash state (caught/escaped)
        public float FlashAlpha    { get; private set; }
        private float _flashTarget;

        // Vignette (motion sickness mitigation + threat atmosphere)
        public float VignetteStrength { get; private set; }

        public ChaseHUD(ThreatTracker threat, SpatialAudioManager audio,
                        ChaseGameState state, PlayerCharacter player)
        {
            _threat  = threat;
            _audio   = audio;
            _state   = state;
            _player  = player;

            EventBus.Subscribe<PlayerCaughtEvent>(_ =>
            {
                _flashTarget = 1f;   // white flash on impact
            });
        }

        public void Update(float dt)
        {
            // Vignette: intensifies with speed and threat
            float moveMag = _player.IsSprinting ? 1f : _player.MoveAxis.Length();
            float targetVig = Math.Clamp(moveMag * 0.4f + _threat.Intensity * 0.5f, 0f, 0.85f);
            VignetteStrength = MathEx.Lerp(VignetteStrength, targetVig, dt * 4f);

            // Flash decay
            FlashAlpha  = MathEx.Lerp(FlashAlpha, _flashTarget, dt * 12f);
            _flashTarget = MathEx.Lerp(_flashTarget, 0f, dt * 3f);
            if (_flashTarget < 0.01f) _flashTarget = 0f;
        }

        /// <summary>
        /// World-space transform of the threat direction arrow.
        /// The arrow floats 0.9m ahead, 0.35m below eye level, always faces the player.
        /// It rotates in the XZ plane to point toward the threat.
        /// </summary>
        public Transform3D GetArrowTransform(Vector3 eyePos, Quaternion headRot)
        {
            var fwd   = Vector3.Transform(Vector3.UnitZ, headRot);
            var down  = Vector3.Transform(-Vector3.UnitY, headRot);
            var pos   = eyePos + fwd * 0.9f + down * 0.35f;
            var yaw   = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ThreatBearing);
            return new Transform3D(pos, yaw * headRot, new Vector3(0.04f, 0.04f, 0.04f));
        }
    }
}
