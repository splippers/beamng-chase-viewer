using BeamQuest.Audio;
using BeamQuest.Chase;
using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.UI
{
    /// <summary>
    /// Chase mode HUD — horror-atmosphere-first.
    ///
    /// Elements (all world-space geometry, interpreted by WorldRenderer):
    ///   - Threat vignette   : edge darkening that intensifies with proximity
    ///   - Heartbeat ring    : thin circle pulsing red with heartbeat rhythm
    ///   - Threat arrow      : bottom-of-view directional indicator
    ///   - Stamina bar       : dim horizontal bar, only visible when low or sprinting
    ///   - Caught overlay    : red flash → black screen with stats + restart prompt
    ///   - Escaped overlay   : green pulse → overlay with escape time + restart prompt
    /// </summary>
    public sealed class ChaseHUD
    {
        private readonly ThreatTracker      _threat;
        private readonly SpatialAudioManager _audio;
        private readonly ChaseGameState     _state;
        private readonly PlayerCharacter    _player;

        // ── Readable by renderer ─────────────────────────────────────────────

        public float      HeartbeatPulse  => _audio.HeartbeatPulse;
        public float      ThreatIntensity => _threat.Intensity;
        public float      ThreatBearing   => _audio.DirectionAngle;
        public float      StaminaFraction => _player.Stamina;
        public bool       ShowStamina     => _player.IsSprinting || _player.Stamina < 0.5f;
        public float      SurvivalSeconds => _state.SurvivalSeconds;
        public ChasePhase Phase           => _state.Phase;
        public Vector3    PlayerPosition  => _player.Position;
        public Quaternion PlayerRotation  => _player.Rotation;

        // Vignette (motion sickness mitigation + threat atmosphere)
        public float VignetteStrength { get; private set; }

        // Full-screen flash — color + alpha
        public float   FlashAlpha { get; private set; }
        public Vector3 FlashColor { get; private set; } = Vector3.Zero;

        // Overlay (caught / escaped results screen)
        public bool   ShowOverlay   { get; private set; }
        public float  OverlayAlpha  { get; private set; }
        public string OverlayTitle  { get; private set; } = "";
        public string OverlayLine1  { get; private set; } = "";
        public string OverlayLine2  { get; private set; } = "";
        public string OverlayPrompt { get; private set; } = "";

        // ── Internal state ───────────────────────────────────────────────────

        private enum OverlayState { None, CaughtFlash, CaughtScreen, EscapedFlash, EscapedScreen }

        private OverlayState _overlay      = OverlayState.None;
        private float        _overlayTimer = 0f;
        private float        _flashTarget  = 0f;
        private float        _overlayTarget = 0f;

        private const float CaughtFlashDuration   = 0.45f;
        private const float EscapedFlashDuration  = 0.8f;

        public ChaseHUD(ThreatTracker threat, SpatialAudioManager audio,
                        ChaseGameState state, PlayerCharacter player)
        {
            _threat  = threat;
            _audio   = audio;
            _state   = state;
            _player  = player;

            EventBus.Subscribe<PlayerCaughtEvent>(OnCaught);
            EventBus.Subscribe<ChaseEscapedEvent>(OnEscaped);
            EventBus.Subscribe<ChaseRestartedEvent>(_ => ClearOverlay());
            EventBus.Subscribe<ChaseStartedEvent>(_ => ClearOverlay());
        }

        public void Update(float dt)
        {
            // Vignette: speed + threat intensity
            float moveMag   = _player.IsSprinting ? 1f : _player.MoveAxis.Length();
            float targetVig = Math.Clamp(moveMag * 0.35f + _threat.Intensity * 0.55f, 0f, 0.88f);
            VignetteStrength = MathEx.Lerp(VignetteStrength, targetVig, dt * 4f);

            // Flash animation
            FlashAlpha  = MathEx.Lerp(FlashAlpha, _flashTarget, dt * 14f);
            _flashTarget = MathEx.Lerp(_flashTarget, 0f, dt * 2.5f);
            if (_flashTarget < 0.005f) _flashTarget = 0f;

            // Overlay state machine
            _overlayTimer += dt;
            switch (_overlay)
            {
                case OverlayState.CaughtFlash:
                    if (_overlayTimer >= CaughtFlashDuration)
                    {
                        _overlay      = OverlayState.CaughtScreen;
                        _overlayTimer = 0f;
                        _overlayTarget = 0.88f;
                        ShowOverlay   = true;
                    }
                    break;

                case OverlayState.EscapedFlash:
                    if (_overlayTimer >= EscapedFlashDuration)
                    {
                        _overlay      = OverlayState.EscapedScreen;
                        _overlayTimer = 0f;
                        _overlayTarget = 0.88f;
                        ShowOverlay   = true;
                    }
                    break;
            }

            OverlayAlpha = MathEx.Lerp(OverlayAlpha, _overlayTarget, dt * 6f);
        }

        /// <summary>
        /// World-space transform of the threat direction arrow.
        /// Floats 0.9m ahead, 0.35m below eye level, rotates in XZ toward threat.
        /// </summary>
        public Transform3D GetArrowTransform(Vector3 eyePos, Quaternion headRot)
        {
            var fwd  = Vector3.Transform(Vector3.UnitZ, headRot);
            var down = Vector3.Transform(-Vector3.UnitY, headRot);
            var pos  = eyePos + fwd * 0.9f + down * 0.35f;
            var yaw  = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ThreatBearing);
            return new Transform3D(pos, yaw * headRot, new Vector3(0.04f, 0.04f, 0.04f));
        }

        // ── Event handlers ───────────────────────────────────────────────────

        private void OnCaught(PlayerCaughtEvent e)
        {
            _flashTarget  = 1f;
            FlashColor    = new Vector3(1f, 0.05f, 0.05f); // red
            _overlay      = OverlayState.CaughtFlash;
            _overlayTimer = 0f;
            ShowOverlay   = false;
            _overlayTarget = 0f;
            OverlayAlpha  = 0f;

            OverlayTitle  = "YOU WERE CAUGHT";
            OverlayLine1  = $"Survived: {FormatTime(e.SurvivalSeconds)}  |  Attempt #{_state.AttemptNumber}";
            OverlayLine2  = _state.BestSeconds > 0
                ? $"Best: {FormatTime(_state.BestSeconds)}"
                : "";
            OverlayPrompt = "Press B to try again";
        }

        private void OnEscaped(ChaseEscapedEvent e)
        {
            _flashTarget  = 0.75f;
            FlashColor    = new Vector3(0.1f, 1f, 0.25f); // green
            _overlay      = OverlayState.EscapedFlash;
            _overlayTimer = 0f;
            ShowOverlay   = false;
            _overlayTarget = 0f;
            OverlayAlpha  = 0f;

            OverlayTitle  = "ESCAPED";
            OverlayLine1  = $"Survived: {FormatTime(e.SurvivalSeconds)}";
            OverlayLine2  = _state.BestSeconds >= e.SurvivalSeconds - 0.5f
                ? "NEW BEST"
                : $"Best: {FormatTime(_state.BestSeconds)}";
            OverlayPrompt = "Press B to run again";
        }

        private void ClearOverlay()
        {
            _overlay       = OverlayState.None;
            ShowOverlay    = false;
            _overlayTarget = 0f;
            FlashAlpha     = 0f;
            _flashTarget   = 0f;
        }

        private static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60);
            float s = seconds % 60f;
            return m > 0 ? $"{m}m {s:F1}s" : $"{s:F1}s";
        }
    }
}
