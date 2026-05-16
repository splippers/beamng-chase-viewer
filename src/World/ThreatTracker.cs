using BeamQuest.Core;

namespace BeamQuest.World
{
    /// <summary>
    /// Monitors all vehicles against the player's position.
    /// Maintains a threat zone and publishes events on zone transitions.
    /// The "primary threat" is whichever vehicle is closest and moving toward the player.
    /// </summary>
    public sealed class ThreatTracker
    {
        private readonly VehicleManager _vehicles;

        // Proximity thresholds (metres) — TenseRadius overridden by active ThreatProfile
        public const float ImpactRadius   =  2.5f;
        public const float CriticalRadius =  8f;
        public const float DangerRadius   = 18f;
        public const float TenseRadius    = 35f;

        private float _aggroRadius = TenseRadius;

        public ThreatZone  CurrentZone    { get; private set; } = ThreatZone.Safe;
        public float       NearestDist    { get; private set; } = float.MaxValue;
        public string?     PrimaryId      { get; private set; }
        public Vector3     PrimaryPos     { get; private set; }
        public Vector3     PrimaryDir     { get; private set; }  // unit vector toward threat
        public float       PrimarySpeed   { get; private set; }

        // Approach speed (positive = closing in, negative = receding)
        public float ApproachSpeed { get; private set; }

        private float _lastDist = float.MaxValue;

        public ThreatTracker(VehicleManager vehicles) => _vehicles = vehicles;

        public void ApplyProfile(ThreatProfileConfig cfg) => _aggroRadius = cfg.AggroRadiusM;

        public void Update(Vector3 playerPos, float dt)
        {
            float nearest = float.MaxValue;
            string? nearestId = null;
            VehicleManager.TrackedVehicle? nearestVehicle = null;

            foreach (var (id, v) in _vehicles.Vehicles)
            {
                float d = Vector3.Distance(playerPos, v.Position);
                if (d < nearest)
                {
                    nearest        = d;
                    nearestId      = id;
                    nearestVehicle = v;
                }
            }

            NearestDist  = nearest;
            PrimaryId    = nearestId;
            ApproachSpeed = dt > 0 ? (_lastDist - nearest) / dt : 0f;
            _lastDist    = nearest;

            if (nearestVehicle != null)
            {
                PrimaryPos   = nearestVehicle.Position;
                PrimaryDir   = nearest > 0.1f
                    ? Vector3.Normalize(nearestVehicle.Position - playerPos)
                    : Vector3.UnitZ;
                PrimarySpeed = nearestVehicle.SpeedMs;
            }

            var newZone = nearest switch
            {
                <= ImpactRadius   => ThreatZone.Impact,
                <= CriticalRadius => ThreatZone.Critical,
                <= DangerRadius   => ThreatZone.Danger,
                var d when d <= _aggroRadius => ThreatZone.Tense,
                _                 => ThreatZone.Safe,
            };

            if (newZone != CurrentZone)
            {
                CurrentZone = newZone;
                EventBus.Publish(new ThreatZoneChangedEvent(newZone, nearest));

                if (newZone == ThreatZone.Impact && nearestId != null)
                    EventBus.Publish(new ThreatImpactEvent(nearestId, nearestVehicle?.SpeedMs ?? 0f));
            }
        }

        /// <summary>
        /// Bearing in radians from player's forward direction to the threat.
        /// 0 = directly ahead, π = directly behind, positive = right.
        /// </summary>
        public float ThreatBearing(Quaternion playerRot)
        {
            var localDir = Vector3.Transform(PrimaryDir,
                Quaternion.Inverse(playerRot));
            return MathF.Atan2(localDir.X, -localDir.Z);
        }

        /// <summary>
        /// Normalised intensity 0-1 based on proximity.  Drives fog, haptics, heartbeat.
        /// </summary>
        public float Intensity => CurrentZone switch
        {
            ThreatZone.Safe     => 0f,
            ThreatZone.Tense    => MathEx.Lerp(0f,   0.35f, 1f - (NearestDist - DangerRadius)  / (_aggroRadius - DangerRadius)),
            ThreatZone.Danger   => MathEx.Lerp(0.35f, 0.7f, 1f - (NearestDist - CriticalRadius)/ (DangerRadius - CriticalRadius)),
            ThreatZone.Critical => MathEx.Lerp(0.7f,  0.95f,1f - (NearestDist - ImpactRadius)  / (CriticalRadius - ImpactRadius)),
            _                   => 1f,
        };
    }
}
