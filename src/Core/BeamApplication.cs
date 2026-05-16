using BeamQuest.Modes;
using BeamQuest.Protocol;
using BeamQuest.UI;
using BeamQuest.World;
using Microsoft.Extensions.Logging;

// XR + Rendering shared from the same Vulkan layer (copy from SLQuest.src)
// See: BeamQuestViewer.csproj shared compile items
using BeamQuest.XR;
using BeamQuest.Rendering;

namespace BeamQuest.Core
{
    /// <summary>
    /// Application root.  Owns all subsystems; drives the XR frame loop.
    /// </summary>
    public sealed class BeamApplication : IAsyncDisposable
    {
        public static BeamApplication Instance { get; private set; } = null!;

        public ILoggerFactory   LogFactory { get; }
        public XRSession        XR         { get; }
        public VulkanContext    Vulkan     { get; }
        public SwapchainRenderer Swapchain { get; }
        public VehicleManager   Vehicles   { get; }
        public TerrainLoader    Terrain    { get; }
        public UIManager        UI         { get; }
        public SpectatorMode    Spectator  { get; }
        public CockpitMode      Cockpit    { get; }
        public IViewerMode      ActiveMode { get; private set; }

        private IVehicleDataSource? _source;
        private CancellationTokenSource _cts = new();
        private bool _disposed;

        public BeamApplication(ILoggerFactory logFactory, XRSession xr, VulkanContext vulkan)
        {
            Instance   = this;
            LogFactory = logFactory;
            XR         = xr;
            Vulkan     = vulkan;

            Vehicles  = new VehicleManager();
            Terrain   = new TerrainLoader(logFactory.CreateLogger<TerrainLoader>());
            Spectator = new SpectatorMode(Vehicles);
            Cockpit   = new CockpitMode(Vehicles);
            ActiveMode = Spectator;

            Swapchain  = new SwapchainRenderer(xr, vulkan);
            UI         = new UIManager(Vehicles, ActiveMode, logFactory.CreateLogger<UIManager>());

            EventBus.Subscribe<ViewerModeChangedEvent>(e =>
                LogFactory.CreateLogger<BeamApplication>().LogInformation("Mode: {Mode}", e.Mode));
        }

        public async Task ConnectAsync(IVehicleDataSource source)
        {
            await DisconnectAsync();
            _source = source;
            bool ok = await source.ConnectAsync(_cts.Token);
            if (!ok) return;

            _ = Task.Run(PumpFramesAsync, _cts.Token);
        }

        private async Task PumpFramesAsync()
        {
            if (_source == null) return;
            await foreach (var frame in _source.ReadFramesAsync(_cts.Token))
                Vehicles.ApplyFrame(frame);
        }

        public async Task DisconnectAsync()
        {
            if (_source != null)
            {
                _source.Disconnect();
                await _source.DisposeAsync();
                _source = null;
            }
            Vehicles.Clear();
        }

        public void SwitchMode(IViewerMode mode)
        {
            ActiveMode.Deactivate();
            ActiveMode = mode;
            mode.Activate();
        }

        public async Task RunAsync()
        {
            await XR.InitAsync();
            await Swapchain.InitAsync();

            Spectator.Activate();

            var log   = LogFactory.CreateLogger<BeamApplication>();
            var token = _cts.Token;
            log.LogInformation("BeamQuest Viewer running");

            while (!token.IsCancellationRequested && XR.IsRunning)
            {
                XR.PollEvents();
                if (!XR.SessionActive) { await Task.Yield(); continue; }

                var (waitOk, frameState) = XR.WaitFrame();
                if (!waitOk) continue;

                XR.BeginFrame();

                float dt = XR.DeltaTime;
                var input = XR.Input;
                MapInputToMode(input, dt);

                Vehicles.Interpolate(dt);
                EventBus.Flush();
                UI.Tick(dt, ActiveMode.CameraPosition, Quaternion.Identity);

                var views = XR.LocateViews();
                Swapchain.RenderFrame(views, frameState.PredictedDisplayTime, RenderScene);
                XR.EndFrame(frameState, Swapchain.ProjectionLayers);
            }

            log.LogInformation("BeamQuest Viewer exiting");
        }

        private void MapInputToMode(XRInput input, float dt)
        {
            input.SyncActions();
            var left  = input.GetController(Hand.Left);
            var right = input.GetController(Hand.Right);

            // Mode switch via wrist menu (left thumbstick click = toggle menu)
            UI.Wrist.IsVisible = left.ThumbstickClick;

            if (ActiveMode is SpectatorMode spec)
            {
                spec.MoveAxis   = right.ThumbstickAxis;
                spec.LookAxis   = left.ThumbstickAxis;
                spec.UpDown     = right.TriggerValue - left.TriggerValue;
                spec.BoostHeld  = left.GripValue > 0.5f;
                spec.CycleBtn   = right.ButtonA;
                spec.ReleaseBtn = right.ButtonB;
            }
            else if (ActiveMode is CockpitMode ck)
            {
                var (_, fState) = XR.WaitFrame();
                var views = XR.LocateViews();
                if (views.Count > 0)
                    ck.HeadViewMatrix = views[0].ViewMatrix;
            }
        }

        private void RenderScene(in RenderContext ctx)
        {
            // WorldRenderer (to be added) draws terrain + vehicles here
            UI.Tick(0f, ActiveMode.CameraPosition, Quaternion.Identity);
        }

        public void RequestExit() => _cts.Cancel();

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await DisconnectAsync();
            UI.Dispose();
            Swapchain.Dispose();
            Vulkan.Dispose();
            XR.Dispose();
        }
    }
}
