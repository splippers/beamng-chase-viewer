using BeamQuest.Core;
using BeamQuest.Protocol;

namespace BeamQuest.Modes
{
    /// <summary>
    /// Wraps a ReplaySource and exposes play/pause/scrub controls.
    /// The camera reuses SpectatorMode — replay just provides the vehicle data.
    /// </summary>
    public sealed class ReplayMode : IViewerMode
    {
        private readonly ReplaySource  _replay;
        private readonly SpectatorMode _camera;

        public string  Name           => "Replay";
        public Vector3 CameraPosition => _camera.CameraPosition;

        public bool  IsPlaying { get; private set; }
        public float Speed     { get => _replay.Speed; set => _replay.Speed = value; }
        public float TotalSeconds   => _replay.TotalSeconds;
        public float CurrentSeconds => _replay.CurrentSeconds;

        // Same input bindings as SpectatorMode (forwarded)
        public Vector2 MoveAxis   { set => _camera.MoveAxis   = value; }
        public Vector2 LookAxis   { set => _camera.LookAxis   = value; }
        public float   UpDown     { set => _camera.UpDown     = value; }
        public bool    BoostHeld  { set => _camera.BoostHeld  = value; }
        public bool    CycleBtn   { set => _camera.CycleBtn   = value; }
        public bool    ReleaseBtn { set => _camera.ReleaseBtn = value; }

        private bool _lastPlayBtn;

        public ReplayMode(ReplaySource replay, SpectatorMode camera)
        {
            _replay = replay;
            _camera = camera;
        }

        public void Activate()
        {
            IsPlaying = true;
            EventBus.Publish(new ViewerModeChangedEvent(Name));
        }

        public void Deactivate() => IsPlaying = false;

        public void Tick(float dt)
        {
            _camera.Tick(dt);
        }

        public void TogglePlay() => IsPlaying = !IsPlaying;

        public void Seek(float seconds)    => _replay.Seek(seconds);
        public void StepSpeed(float delta) => Speed = Math.Clamp(Speed + delta, 0.1f, 8f);

        public Matrix4x4 GetViewMatrix(int eye) => _camera.GetViewMatrix(eye);
    }
}
