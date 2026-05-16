using BeamQuest.Core;
using BeamQuest.Modes;
using BeamQuest.Protocol;
using BeamQuest.World;
using Microsoft.Extensions.Logging;

namespace BeamQuest.UI
{
    /// <summary>
    /// Owns the HUD, wrist menu, and connection panel.
    /// Renders world-space UI geometry each frame.  The actual draw calls
    /// are issued by WorldRenderer which reads HUD/panel data from here.
    /// </summary>
    public sealed class UIManager : IDisposable
    {
        private readonly ILogger<UIManager> _log;

        public TelemetryHUD    HUD            { get; }
        public ConnectionPanel ConnectPanel   { get; }
        public WristMenu       Wrist          { get; }

        public UIManager(
            VehicleManager    vehicles,
            IViewerMode       activeMode,
            ILogger<UIManager> log)
        {
            _log        = log;
            HUD         = new TelemetryHUD(vehicles);
            ConnectPanel = new ConnectionPanel(log);
            Wrist        = new WristMenu(activeMode);

            EventBus.Subscribe<SourceConnectedEvent>(e =>
            {
                ConnectPanel.Hide();
                EventBus.Publish(new NotificationEvent($"Connected to {e.SourceName}", 3f));
            });
            EventBus.Subscribe<SourceDisconnectedEvent>(e =>
            {
                ConnectPanel.Show();
                EventBus.Publish(new NotificationEvent($"Disconnected: {e.Reason}", 5f));
            });
        }

        public void Tick(float dt, Vector3 camPos, Quaternion headRot)
        {
            HUD.Update();
            Wrist.Update(dt);
        }

        public void Dispose() { }
    }

    // ── Connection Panel ─────────────────────────────────────────────────────

    public sealed class ConnectionPanel
    {
        private readonly ILogger _log;
        public bool IsVisible { get; private set; } = true;

        // Bound by UI input (set by BeamApplication after user types in wrist keyboard)
        public string HostInput { get; set; } = "192.168.1.1";
        public int    PortInput { get; set; } = 37420;
        public string SourceType { get; set; } = "udp";  // "udp" | "ws" | "outgauge" | "replay"
        public string ReplayPath { get; set; } = "";

        public ConnectionPanel(ILogger log) => _log = log;

        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
    }

    // ── Wrist Menu ───────────────────────────────────────────────────────────

    public sealed class WristMenu
    {
        private IViewerMode _mode;
        public bool IsVisible { get; set; }

        // Pressed-button state for one frame (cleared after Tick)
        public bool SpectatorPressed { get; private set; }
        public bool CockpitPressed   { get; private set; }
        public bool ReplayPressed    { get; private set; }
        public bool RecordPressed    { get; private set; }
        public bool HUDTogglePressed { get; private set; }

        // Raw button input — set each frame by BeamApplication
        public bool BtnSpectator { private get; set; }
        public bool BtnCockpit   { private get; set; }
        public bool BtnReplay    { private get; set; }
        public bool BtnRecord    { private get; set; }
        public bool BtnHUDToggle { private get; set; }

        private bool _lastSpec, _lastCock, _lastReplay, _lastRec, _lastHud;

        public WristMenu(IViewerMode mode) => _mode = mode;

        public void Update(float dt)
        {
            SpectatorPressed = BtnSpectator && !_lastSpec;
            CockpitPressed   = BtnCockpit   && !_lastCock;
            ReplayPressed    = BtnReplay    && !_lastReplay;
            RecordPressed    = BtnRecord    && !_lastRec;
            HUDTogglePressed = BtnHUDToggle && !_lastHud;

            _lastSpec   = BtnSpectator;
            _lastCock   = BtnCockpit;
            _lastReplay = BtnReplay;
            _lastRec    = BtnRecord;
            _lastHud    = BtnHUDToggle;
        }

        /// <summary>
        /// World-space transform for wrist menu: follows left wrist pose.
        /// </summary>
        public Transform3D GetWristTransform(Vector3 leftWristPos, Quaternion leftWristRot)
        {
            // Position panel slightly above the wrist, facing the user
            var up  = Vector3.Transform(Vector3.UnitY, leftWristRot);
            var pos = leftWristPos + up * 0.12f;
            return new Transform3D(pos, leftWristRot, new Vector3(0.2f, 0.12f, 1f));
        }
    }
}
