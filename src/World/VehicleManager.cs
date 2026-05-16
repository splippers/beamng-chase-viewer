using BeamQuest.Core;
using BeamQuest.Protocol;

namespace BeamQuest.World
{
    /// <summary>
    /// Maintains the live vehicle table.  On each frame, reconciles adds/removes
    /// and interpolates transforms for smooth rendering between network ticks.
    /// </summary>
    public sealed class VehicleManager
    {
        public sealed class TrackedVehicle
        {
            public string VehicleId   { get; }
            public string PlayerName  { get; }
            public string ModelName   { get; }

            // Interpolation state
            public Vector3    Position    { get; internal set; }
            public Quaternion Rotation    { get; internal set; } = Quaternion.Identity;
            public Vector3    Velocity    { get; internal set; }

            // Latest telemetry
            public float SpeedMs   { get; internal set; }
            public float RPM       { get; internal set; }
            public int   Gear      { get; internal set; }
            public float Throttle  { get; internal set; }
            public float Brake     { get; internal set; }
            public float Steering  { get; internal set; }
            public float Damage    { get; internal set; }
            public float[]? WheelHeights { get; internal set; }

            // Target for interpolation
            internal Vector3    TargetPosition;
            internal Quaternion TargetRotation = Quaternion.Identity;

            public TrackedVehicle(string id, string name, string model)
            {
                VehicleId  = id;
                PlayerName = name;
                ModelName  = model;
            }
        }

        private readonly Dictionary<string, TrackedVehicle> _vehicles = new();
        public  IReadOnlyDictionary<string, TrackedVehicle> Vehicles => _vehicles;

        public string? LocalVehicleId { get; set; }

        public void ApplyFrame(WorldFrame frame)
        {
            var seen = new HashSet<string>();

            foreach (var vs in frame.Vehicles)
            {
                seen.Add(vs.VehicleId);

                if (!_vehicles.TryGetValue(vs.VehicleId, out var tv))
                {
                    tv = new TrackedVehicle(vs.VehicleId, vs.PlayerName, vs.ModelName);
                    tv.Position       = vs.Position;
                    tv.Rotation       = vs.Rotation;
                    tv.TargetPosition = vs.Position;
                    tv.TargetRotation = vs.Rotation;
                    _vehicles[vs.VehicleId] = tv;
                    EventBus.Publish(new VehicleSpawnedEvent(vs.VehicleId, vs.PlayerName));
                }
                else
                {
                    tv.TargetPosition = vs.Position;
                    tv.TargetRotation = vs.Rotation;
                }

                tv.Velocity    = vs.Velocity;
                tv.SpeedMs     = vs.SpeedMs;
                tv.RPM         = vs.RPM;
                tv.Gear        = vs.Gear;
                tv.Throttle    = vs.Throttle;
                tv.Brake       = vs.Brake;
                tv.Steering    = vs.Steering;
                tv.Damage      = vs.Damage;
                tv.WheelHeights = vs.WheelHeights;
            }

            // Remove departed vehicles
            var removed = _vehicles.Keys.Where(k => !seen.Contains(k)).ToList();
            foreach (var id in removed)
            {
                _vehicles.Remove(id);
                EventBus.Publish(new VehicleRemovedEvent(id));
            }
        }

        /// <summary>
        /// Smoothly interpolate all vehicles toward their network-received target.
        /// Call once per frame with the frame delta time.
        /// </summary>
        public void Interpolate(float dt)
        {
            float alpha = Math.Clamp(dt * 20f, 0f, 1f);   // ~20 Hz network, smooth at any render rate
            foreach (var v in _vehicles.Values)
            {
                v.Position = MathEx.Lerp(v.Position, v.TargetPosition, alpha);
                v.Rotation = MathEx.Slerp(v.Rotation, v.TargetRotation, alpha);
            }
        }

        public TrackedVehicle? GetLocal() =>
            LocalVehicleId != null && _vehicles.TryGetValue(LocalVehicleId, out var v) ? v : null;

        public void Clear()
        {
            _vehicles.Clear();
        }
    }
}
