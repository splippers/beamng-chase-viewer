using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.Modes
{
    /// <summary>
    /// Locks the camera inside the local vehicle's cabin.
    /// Head orientation from OpenXR drives the view; vehicle transform drives world placement.
    /// Publishes telemetry data each frame for the HUD to display.
    /// </summary>
    public sealed class CockpitMode : IViewerMode
    {
        private readonly VehicleManager _vehicles;

        public string  Name           => "Cockpit";
        public Vector3 CameraPosition => _worldPos;

        // Driver eye offset relative to vehicle centre (metres)
        private static readonly Vector3 EyeOffset = new(0f, 0.9f, 0.6f);

        private Vector3    _worldPos;
        private Matrix4x4  _headView = Matrix4x4.Identity;  // set each frame by BeamApplication

        /// <summary>Set by BeamApplication from OpenXR view matrix each frame.</summary>
        public Matrix4x4 HeadViewMatrix
        {
            set => _headView = value;
        }

        public CockpitMode(VehicleManager vehicles) => _vehicles = vehicles;

        public void Activate()   => EventBus.Publish(new ViewerModeChangedEvent(Name));
        public void Deactivate() { }

        public void Tick(float dt)
        {
            var local = _vehicles.GetLocal();
            if (local == null) return;

            // Eye position = vehicle transform + seat offset
            var eyeLocal = Vector3.Transform(EyeOffset, local.Rotation);
            _worldPos    = local.Position + eyeLocal;
        }

        public Matrix4x4 GetViewMatrix(int eye)
        {
            var local = _vehicles.GetLocal();
            if (local == null)
                return Matrix4x4.CreateLookAt(_worldPos, _worldPos + Vector3.UnitZ, Vector3.UnitY);

            // Combine vehicle orientation with head orientation
            var vehicleRot = Matrix4x4.CreateFromQuaternion(local.Rotation);
            // _headView already encodes the HMD pose in local space; compose with vehicle
            return _headView * Matrix4x4.Invert(vehicleRot, out var inv) ? inv : _headView;
        }
    }
}
