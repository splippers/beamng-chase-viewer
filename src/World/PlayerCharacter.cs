using BeamQuest.Core;

namespace BeamQuest.World
{
    /// <summary>
    /// On-foot player: thumbstick locomotion, terrain-hugging, stamina, head-bob.
    /// Position is in BeamNG world space (Y-up) so it can be broadcast to the AI.
    /// </summary>
    public sealed class PlayerCharacter
    {
        private readonly TerrainLoader _terrain;

        // World-space position (Y-up, matches BeamNG)
        public Vector3    Position    { get; private set; }
        public Quaternion Rotation    { get; private set; } = Quaternion.Identity;
        public float      EyeHeight   => IsCrouching ? 0.9f : 1.68f;
        public bool       IsCrouching { get; private set; }
        public bool       IsSprinting { get; private set; }
        public float      Stamina     { get; private set; } = 1f;   // 0-1

        // Head-bob state
        private float _bobPhase;
        private float _bobMagnitude;
        public Vector3 HeadBobOffset { get; private set; }

        // Speed constants (m/s)
        private const float WalkSpeed    = 1.5f;
        private const float CrouchSpeed  = 0.7f;
        private const float SprintSpeed  = 5.2f;
        private const float StaminaDrain = 0.25f;   // per second sprinting
        private const float StaminaRegen = 0.12f;   // per second recovering
        private const float BobFreqWalk  = 2.0f;    // Hz
        private const float BobFreqRun   = 3.5f;    // Hz
        private const float BobAmplitude = 0.04f;   // metres vertical

        // Input — set each frame by ChaseMode
        public Vector2 MoveAxis  { get; set; }
        public float   YawDelta  { get; set; }   // radians/frame from controller
        public bool    SprintBtn { get; set; }
        public bool    CrouchBtn { get; set; }

        private float _yaw;

        public PlayerCharacter(TerrainLoader terrain, Vector3 startPos)
        {
            _terrain = terrain;
            Position = startPos;
        }

        public void Teleport(Vector3 pos) => Position = pos with { Y = GroundAt(pos) + EyeHeight };

        public void Tick(float dt)
        {
            UpdateStance(dt);
            Move(dt);
            UpdateBob(dt);
        }

        private void UpdateStance(float dt)
        {
            IsCrouching = CrouchBtn;

            if (SprintBtn && !IsCrouching && Stamina > 0f && MoveAxis.Length() > 0.3f)
            {
                IsSprinting = true;
                Stamina = Math.Max(0f, Stamina - StaminaDrain * dt);
            }
            else
            {
                IsSprinting = false;
                Stamina = Math.Min(1f, Stamina + StaminaRegen * dt);
            }
        }

        private void Move(float dt)
        {
            _yaw += YawDelta;
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);

            float speed = IsSprinting ? SprintSpeed
                        : IsCrouching ? CrouchSpeed
                        : WalkSpeed;

            if (MoveAxis.LengthSquared() > 0.01f)
            {
                var dir2 = Vector2.Normalize(MoveAxis);
                var fwd  = Vector3.Transform(new Vector3(dir2.Y, 0f, -dir2.X),
                           Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw));
                var next = Position + fwd * speed * dt;
                next.Y   = GroundAt(next) + EyeHeight;
                Position = next;
            }
            else
            {
                // Keep Y hugged to terrain even while standing still
                var p = Position;
                p.Y    = GroundAt(p) + EyeHeight;
                Position = p;
            }
        }

        private void UpdateBob(float dt)
        {
            float speed    = MoveAxis.Length();
            float targetMag = speed > 0.1f ? BobAmplitude * Math.Min(speed, 1f) : 0f;
            _bobMagnitude   = MathEx.Lerp(_bobMagnitude, targetMag, dt * 8f);

            float freq  = IsSprinting ? BobFreqRun : BobFreqWalk;
            _bobPhase  += freq * 2f * MathF.PI * dt * speed;
            float dy    = MathF.Sin(_bobPhase) * _bobMagnitude;
            float dx    = MathF.Sin(_bobPhase * 0.5f) * _bobMagnitude * 0.4f;
            HeadBobOffset = new Vector3(dx, dy, 0f);
        }

        private float GroundAt(Vector3 pos) => _terrain.SampleHeight(pos.X, pos.Z);

        public Vector3 EyePosition => Position + HeadBobOffset;
    }
}
