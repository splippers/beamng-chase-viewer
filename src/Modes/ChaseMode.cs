using BeamQuest.Audio;
using BeamQuest.Chase;
using BeamQuest.Core;
using BeamQuest.Protocol;
using BeamQuest.UI;
using BeamQuest.World;
using Microsoft.Extensions.Logging;

namespace BeamQuest.Modes
{
    /// <summary>
    /// The horror-chase experience.
    ///
    /// The player is a pedestrian on foot in the arena.  A BeamNG vehicle —
    /// controlled by BeamNG's AI and targeted at the player's real-time
    /// position — hunts them through fog-filled flatland.
    ///
    /// Pipeline per frame:
    ///   1. Read controller input → PlayerCharacter.Tick (movement, stamina)
    ///   2. VehicleManager already updated from network source
    ///   3. ThreatTracker.Update  → zone transitions / impact detection
    ///   4. SpatialAudio.Update   → engine volume, haptic intensities
    ///   5. Environment.Update    → fog density, light flicker
    ///   6. ChaseGameState.Update → survival timer, caught/escaped
    ///   7. PlayerPositionBroadcaster.Update → send pos to BeamNG AI
    ///   8. ChaseHUD.Update       → vignette, flash, stamina
    /// </summary>
    public sealed class ChaseMode : IViewerMode
    {
        public string  Name           => "Chase";
        public Vector3 CameraPosition => _player.EyePosition;

        // Sub-systems owned by this mode
        public  PlayerCharacter      Player      { get; }
        public  ThreatTracker        Threat      { get; }
        public  ProceduralEnvironment Environment { get; }
        public  SpatialAudioManager  Audio       { get; }
        public  ChaseGameState       GameState   { get; }
        public  ChaseHUD             HUD         { get; }

        private readonly PlayerPositionBroadcaster? _broadcaster;
        private readonly ILogger<ChaseMode>         _log;

        // HMD head orientation — set each frame by BeamApplication
        public Quaternion HeadRotation { private get; set; } = Quaternion.Identity;

        // Controller input — set each frame by BeamApplication
        public Vector2 MoveAxis  { private get; set; }
        public float   YawDelta  { private get; set; }
        public bool    Sprint    { private get; set; }
        public bool    Crouch    { private get; set; }
        public bool    StartBtn  { private get; set; }
        public bool    RestartBtn{ private get; set; }

        private bool _lastStart, _lastRestart;

        public ThreatProfile ActiveProfile { get; private set; } = ThreatProfile.Truck;
        public ThreatProfileConfig ActiveProfileConfig { get; private set; } = ThreatProfileConfig.Truck;

        // Spawn point — the vehicle starts facing this position in BeamNG
        private static readonly Vector3 SpawnPos = new(0f, 0f, 10f);

        public ChaseMode(
            VehicleManager     vehicles,
            TerrainLoader      terrain,
            PlayerPositionBroadcaster? broadcaster,
            ILogger<ChaseMode> log)
        {
            _broadcaster = broadcaster;
            _log         = log;

            Player      = new PlayerCharacter(terrain, SpawnPos);
            Threat      = new ThreatTracker(vehicles);
            Environment = new ProceduralEnvironment();
            Audio       = new SpatialAudioManager(Threat);
            GameState   = new ChaseGameState(Threat);
            HUD         = new ChaseHUD(Threat, Audio, GameState, Player);

            EventBus.Subscribe<PlayerCaughtEvent>(OnCaught);
        }

        public void Activate()
        {
            Player.Teleport(SpawnPos);
            EventBus.Publish(new ViewerModeChangedEvent(Name));
            _log.LogInformation("Chase mode activated — profile: {Profile}", ActiveProfile);
        }

        /// <summary>
        /// Switch the vehicle profile.  Safe to call between rounds.
        /// </summary>
        public void SetProfile(ThreatProfile profile)
        {
            ActiveProfile       = profile;
            ActiveProfileConfig = ThreatProfileConfig.ForProfile(profile);
            Threat.ApplyProfile(ActiveProfileConfig);
            Environment.SetFogBias(ActiveProfileConfig.FogBias);
            _log.LogInformation("Threat profile → {Name}", ActiveProfileConfig.DisplayName);
            EventBus.Publish(new ThreatProfileChangedEvent(profile, ActiveProfileConfig));
        }

        public void Deactivate() { }

        public void Tick(float dt)
        {
            HandleMenuInput();

            // Feed input to player
            Player.MoveAxis  = MoveAxis;
            Player.YawDelta  = YawDelta;
            Player.SprintBtn = Sprint;
            Player.CrouchBtn = Crouch;

            // Only move when the round is running
            if (GameState.Phase == Chase.ChasePhase.Running)
                Player.Tick(dt);

            Threat.Update(Player.Position, dt);
            Audio.Update(dt, HeadRotation);
            Environment.Update(dt, Threat.Intensity);
            GameState.Update(dt, Player.Position);
            HUD.Update(dt);

            _broadcaster?.Update(dt, Player.Position);
        }

        private void HandleMenuInput()
        {
            if (StartBtn && !_lastStart)
            {
                if (GameState.Phase == Chase.ChasePhase.WaitingToStart)
                    GameState.StartRound();
            }
            if (RestartBtn && !_lastRestart)
            {
                if (GameState.Phase is Chase.ChasePhase.Caught or Chase.ChasePhase.Escaped)
                {
                    Player.Teleport(SpawnPos);
                    GameState.Restart();
                    GameState.StartRound();
                }
            }
            _lastStart   = StartBtn;
            _lastRestart = RestartBtn;
        }

        private void OnCaught(PlayerCaughtEvent e)
        {
            _log.LogInformation("Caught after {Secs:F1}s", e.SurvivalSeconds);
            EventBus.Publish(new NotificationEvent(
                $"CAUGHT — {e.SurvivalSeconds:F1}s survived", 0f));
        }

        public Matrix4x4 GetViewMatrix(int eye)
        {
            var eyePos   = Player.EyePosition;
            var combined = HeadRotation * Player.Rotation;
            var forward  = Vector3.Transform(Vector3.UnitZ, combined);
            var up       = Vector3.Transform(Vector3.UnitY, combined);
            return Matrix4x4.CreateLookAt(eyePos, eyePos + forward, up);
        }
    }
}
