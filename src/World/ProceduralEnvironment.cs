using BeamQuest.Core;

namespace BeamQuest.World
{
    public sealed class PropInstance
    {
        public enum PropType { Barrier, Container, AbandonedCar, ConcreteBlock, StreetLight, EscapeBeacon }

        public PropType   Type      { get; init; }
        public Transform3D Transform { get; init; }
        public bool       LightOn   { get; set; } = true;
    }

    /// <summary>
    /// Generates the chase arena procedurally:
    /// flat ground (no heightmap needed), scattered cover props, flickering lights.
    ///
    /// Inspired by BeamNG Flatland — industrial flatness that gives you nowhere to hide
    /// unless you're smart about it.
    /// </summary>
    public sealed class ProceduralEnvironment
    {
        public List<PropInstance> Props   { get; } = new();
        public float              FogDensity { get; private set; } = 0.02f;
        public Vector3            FogColor   { get; private set; } = new(0.05f, 0.05f, 0.08f);

        // Flickering light state
        private readonly float[] _lightPhases;
        private readonly float[] _lightFreqs;
        private float            _globalTime;

        // Target fog density (driven by ThreatTracker intensity)
        private float _targetFog = 0.02f;
        private float _fogBias   = 0f;  // per-profile tweak applied to _targetFog

        // Escape beacon: a fixed landmark the player must physically reach to win.
        // Placed at the far end of the arena along the +X axis.
        public static readonly Vector3 BeaconPosition = new(85f, 0f, 0f);

        private const float ArenaRadius = 120f;
        private const int   LightCount  = 24;
        private const int   Seed        = 42;

        public ProceduralEnvironment()
        {
            var rng = new Random(Seed);
            _lightPhases = new float[LightCount];
            _lightFreqs  = new float[LightCount];
            for (int i = 0; i < LightCount; i++)
            {
                _lightPhases[i] = (float)rng.NextDouble() * MathF.PI * 2f;
                _lightFreqs[i]  = 0.5f + (float)rng.NextDouble() * 3f;
            }

            GenerateProps(rng);
        }

        private void GenerateProps(Random rng)
        {
            // Ring of concrete barriers — partial cover, not full walls
            for (int i = 0; i < 30; i++)
            {
                float angle = i * MathF.PI * 2f / 30f + (float)rng.NextDouble() * 0.2f;
                float r     = 20f + (float)rng.NextDouble() * 50f;
                var pos     = new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
                float yaw   = (float)rng.NextDouble() * MathF.PI * 2f;

                Props.Add(new PropInstance
                {
                    Type      = PropType.Barrier,
                    Transform = new Transform3D(pos,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
                        Vector3.One),
                });
            }

            // Scattered containers — bigger cover blocks
            for (int i = 0; i < 8; i++)
            {
                float angle = (float)rng.NextDouble() * MathF.PI * 2f;
                float r     = 15f + (float)rng.NextDouble() * 60f;
                var pos     = new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
                float yaw   = (float)rng.NextDouble() * MathF.PI * 2f;

                var colors = new[] {
                    new Vector4(0.6f,0.1f,0.1f,1f),  // rust red
                    new Vector4(0.1f,0.2f,0.5f,1f),  // faded blue
                    new Vector4(0.15f,0.35f,0.15f,1f),// army green
                };
                _ = colors[i % colors.Length]; // will be used when prop builder takes color arg

                Props.Add(new PropInstance
                {
                    Type      = PropType.Container,
                    Transform = new Transform3D(pos,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
                        Vector3.One),
                });
            }

            // Abandoned cars
            for (int i = 0; i < 12; i++)
            {
                float angle = (float)rng.NextDouble() * MathF.PI * 2f;
                float r     = 8f + (float)rng.NextDouble() * 80f;
                var pos     = new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
                float yaw   = (float)rng.NextDouble() * MathF.PI * 2f;

                Props.Add(new PropInstance
                {
                    Type      = PropType.AbandonedCar,
                    Transform = new Transform3D(pos,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
                        Vector3.One),
                });
            }

            // Street lights in a grid
            for (int i = 0; i < LightCount; i++)
            {
                float angle = i * MathF.PI * 2f / LightCount;
                float r     = ArenaRadius * 0.5f;
                var pos     = new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);

                Props.Add(new PropInstance
                {
                    Type      = PropType.StreetLight,
                    Transform = new Transform3D(pos, Quaternion.Identity, Vector3.One),
                    LightOn   = true,
                });
            }

            // Escape beacon — single landmark the player must reach to win.
            Props.Add(new PropInstance
            {
                Type      = PropType.EscapeBeacon,
                Transform = new Transform3D(BeaconPosition, Quaternion.Identity, Vector3.One),
                LightOn   = true,
            });
        }

        public void SetFogBias(float bias) => _fogBias = bias;

        public void Update(float dt, float threatIntensity)
        {
            _globalTime += dt;

            // Counter-intuitive horror tool: fog thickens at safe distance, clears as
            // threat closes in — you see the thing more clearly the closer it gets.
            // Dense fog (hiding distance) → thin fog (full clarity at impact range).
            _targetFog  = MathEx.Lerp(0.15f, 0.008f, threatIntensity) + _fogBias;
            FogDensity  = MathEx.Lerp(FogDensity, _targetFog, dt * 1.5f);

            // Lights flicker faster when threat is close
            var lightProps = Props.Where(p => p.Type == PropType.StreetLight).ToList();
            for (int i = 0; i < lightProps.Count && i < LightCount; i++)
            {
                float phase    = _globalTime * _lightFreqs[i] + _lightPhases[i];
                float flicker  = MathF.Sin(phase);
                // When threat is close, more lights go out unpredictably
                float outThresh = MathEx.Lerp(0.98f, 0.3f, threatIntensity);
                lightProps[i].LightOn = flicker > outThresh - 1f;
            }
        }
    }
}
