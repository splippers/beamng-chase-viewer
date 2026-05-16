using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.UI
{
    /// <summary>
    /// Cockpit HUD overlay.  Positions a world-space panel just below the
    /// driver's line of sight showing speed, RPM, gear, and fuel.
    ///
    /// Data is read from VehicleManager each frame; no network dependency.
    /// </summary>
    public sealed class TelemetryHUD
    {
        private readonly VehicleManager _vehicles;
        public bool Visible { get; set; } = true;

        // Cached display values — updated from frame loop, read by renderer
        public float SpeedKph  { get; private set; }
        public float RPM       { get; private set; }
        public int   Gear      { get; private set; }
        public float FuelPct   { get; private set; }
        public float Throttle  { get; private set; }
        public float Brake     { get; private set; }
        public string GearLabel { get; private set; } = "N";

        public TelemetryHUD(VehicleManager vehicles) => _vehicles = vehicles;

        public void Update()
        {
            var v = _vehicles.GetLocal();
            if (v == null) return;

            SpeedKph  = v.SpeedMs * 3.6f;
            RPM       = v.RPM;
            Gear      = v.Gear;
            FuelPct   = v.Fuel;
            Throttle  = v.Throttle;
            Brake     = v.Brake;
            GearLabel = v.Gear switch { -1 => "R", 0 => "N", _ => v.Gear.ToString() };
        }

        /// <summary>
        /// Returns panel world transform: fixed 1.0m wide panel, 0.6m below HMD,
        /// pitched 35° toward driver.
        /// </summary>
        public Transform3D GetPanelTransform(Vector3 cameraPos, Quaternion headRot)
        {
            var down  = Vector3.Transform(-Vector3.UnitY, headRot);
            var fwd   = Vector3.Transform(Vector3.UnitZ,  headRot);
            var pos   = cameraPos + fwd * 0.9f + down * 0.35f;
            var rot   = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.18f) * headRot;
            return new Transform3D(pos, rot, new Vector3(1.0f, 0.3f, 1f));
        }
    }
}
