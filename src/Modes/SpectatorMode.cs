using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.Modes
{
    /// <summary>
    /// Free-fly camera + click-to-follow any vehicle.
    ///
    /// Controls (Quest 3):
    ///   Right thumbstick   → move forward/back, strafe left/right
    ///   Left thumbstick    → ascend/descend + yaw
    ///   Right trigger      → hold near a vehicle → lock follow
    ///   A button           → cycle follow target
    ///   B button           → release follow / return to free-fly
    ///   Left grip          → speed boost (×5)
    /// </summary>
    public sealed class SpectatorMode : IViewerMode
    {
        private readonly VehicleManager _vehicles;

        public string  Name           => "Spectator";
        public Vector3 CameraPosition => _position;

        private Vector3    _position    = new(0f, 10f, 20f);
        private float      _yaw;
        private float      _pitch;
        private string?    _followId;
        private float      _followDist  = 12f;
        private float      _followHeight = 4f;

        // Input state (set each frame by BeamApplication)
        public Vector2 MoveAxis   { get; set; }
        public Vector2 LookAxis   { get; set; }
        public float   UpDown     { get; set; }
        public bool    BoostHeld  { get; set; }
        public bool    CycleBtn   { get; set; }
        public bool    ReleaseBtn { get; set; }

        private bool _lastCycle, _lastRelease;

        public SpectatorMode(VehicleManager vehicles) => _vehicles = vehicles;

        public void Activate()   => EventBus.Publish(new ViewerModeChangedEvent(Name));
        public void Deactivate() { }

        public void Tick(float dt)
        {
            HandleFollowInput();

            if (_followId != null && _vehicles.Vehicles.TryGetValue(_followId, out var target))
            {
                ChaseTarget(target, dt);
            }
            else
            {
                FlyFree(dt);
            }
        }

        private void HandleFollowInput()
        {
            if (CycleBtn && !_lastCycle)
            {
                var ids = _vehicles.Vehicles.Keys.ToList();
                if (ids.Count > 0)
                {
                    int idx = _followId != null ? (ids.IndexOf(_followId) + 1) % ids.Count : 0;
                    _followId = ids[idx];
                    EventBus.Publish(new FollowTargetChangedEvent(_followId));
                }
            }
            if (ReleaseBtn && !_lastRelease && _followId != null)
            {
                _followId = null;
                EventBus.Publish(new FollowTargetChangedEvent(null));
            }
            _lastCycle   = CycleBtn;
            _lastRelease = ReleaseBtn;
        }

        private void ChaseTarget(VehicleManager.TrackedVehicle t, float dt)
        {
            // Orbit behind vehicle with smooth lag
            var fwd = Vector3.Transform(Vector3.UnitZ, t.Rotation);
            var wantPos = t.Position - fwd * _followDist + Vector3.UnitY * _followHeight;
            _position = MathEx.Lerp(_position, wantPos, dt * 5f);

            // Look at vehicle
            var dir  = Vector3.Normalize(t.Position - _position);
            _pitch   = MathF.Asin(dir.Y);
            _yaw     = MathF.Atan2(dir.X, dir.Z);
        }

        private void FlyFree(float dt)
        {
            float speed = BoostHeld ? 25f : 5f;

            _yaw   -= LookAxis.X * 1.5f * dt;
            _pitch  = Math.Clamp(_pitch - LookAxis.Y * 1.5f * dt, -1.4f, 1.4f);

            var rot = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
            var fwd = Vector3.Transform(Vector3.UnitZ, rot);
            var rgt = Vector3.Transform(Vector3.UnitX, rot);

            _position += fwd * MoveAxis.Y * speed * dt;
            _position += rgt * MoveAxis.X * speed * dt;
            _position += Vector3.UnitY * UpDown * speed * dt;
        }

        public Matrix4x4 GetViewMatrix(int eye)
        {
            var rot = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
            var fwd = Vector3.Transform(Vector3.UnitZ, rot);
            return Matrix4x4.CreateLookAt(_position, _position + fwd, Vector3.UnitY);
        }
    }
}
