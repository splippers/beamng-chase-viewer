using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using BeamQuest.Core;
using BeamQuest.Modes;
using BeamQuest.UI;
using BeamQuest.World;
using Buffer = Silk.NET.Vulkan.Buffer;
using Format = Silk.NET.Vulkan.Format;

namespace BeamQuest.Rendering
{
    // ── Push-constant structs ────────────────────────────────────────────────

    // Shared by vehicle.vert/frag and prop.vert/frag
    // 2×mat4 + vec4 + 2 floats + 2 pad = 160 bytes — fits Quest 3's 256-byte limit
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct BeamPush
    {
        public Matrix4x4 Model;        // 64
        public Matrix4x4 ViewProj;     // 64
        public Vector4   FogColor;     // 16
        public float     FogDensity;   //  4
        public float     Emissive;     //  4
        public float     _pad0, _pad1; //  8
    }

    // HUD text overlay — matches ui.vert / ui.frag push_constant block (96 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct UIPush
    {
        public Matrix4x4 Transform;   // 64
        public Vector4   Color;       // 16
        public float     UvOffsetX;   //  4
        public float     UvOffsetY;   //  4
        public float     UvScaleX;    //  4
        public float     UvScaleY;    //  4
    }

    // Vignette / flash / overlay / stamina bar — 64 bytes, matches vignette.frag
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct VignettePush
    {
        public float   Strength;      // offset  0
        public float   FlashAlpha;    // offset  4
        public float   Heartbeat;     // offset  8
        public float   OverlayAlpha;  // offset 12
        public Vector3 FlashColor;    // offset 16
        public float   _pad0;         // offset 28
        public Vector3 OverlayColor;  // offset 32
        public float   Stamina;       // offset 44
        public float   ShowStamina;   // offset 48  (1f = visible)
        public float   _pad1;         // offset 52
        public float   _pad2;         // offset 56
        public float   _pad3;         // offset 60
    }

    public sealed unsafe class WorldRenderer : IDisposable
    {
        private readonly VulkanContext       _vk;
        private readonly VehicleManager      _vehicles;
        private readonly TerrainLoader       _terrain;
        private readonly ProceduralEnvironment? _environment;
        private SwapchainRenderer            _swapchain = null!;

        // ── Pipelines ────────────────────────────────────────────────────────
        private Pipeline       _vehiclePipeline,  _propPipeline, _terrainPipeline;
        private Pipeline       _vignettePipeline;
        private PipelineLayout _geomLayout;        // shared by vehicle/prop/terrain
        private PipelineLayout _vignetteLayout;
        private bool           _disposed;

        // ── Terrain ──────────────────────────────────────────────────────────
        private Buffer      _terrainVb, _terrainIb;
        private DeviceMemory _terrainVbMem, _terrainIbMem;
        private uint         _terrainIndexCount;
        private bool         _terrainDirty = true;

        // ── Per-prop-type shared geometry ────────────────────────────────────
        private readonly Dictionary<PropInstance.PropType, (Buffer vb, Buffer ib, uint count, DeviceMemory vm, DeviceMemory im)>
            _propMeshes = new();

        // ── Per-vehicle GPU buffers ───────────────────────────────────────────
        private sealed class VehicleGpu
        {
            public Buffer Vb, Ib;
            public DeviceMemory VbMem, IbMem;
            public uint IndexCount;
            public float[]? LastWheelHeights;
            public int ColorSlot;
        }
        private readonly Dictionary<string, VehicleGpu> _vehicleGpu = new();
        private int _vehicleColorCounter;

        // ── HUD text pipeline ─────────────────────────────────────────────────
        private Pipeline            _hudPipeline;
        private PipelineLayout      _hudLayout;
        private DescriptorSetLayout _hudDescLayout;
        private DescriptorPool      _hudDescPool;
        private Sampler             _hudSampler;
        private Silk.NET.Vulkan.Image _whiteTex;
        private DeviceMemory          _whiteTexMem;
        private ImageView             _whiteTexView;
        private DescriptorSet         _whiteDescSet;
        private Silk.NET.Vulkan.Image _fontTex;
        private DeviceMemory          _fontTexMem;
        private ImageView             _fontTexView;
        private DescriptorSet         _fontDescSet;
        private Buffer       _hudVb;
        private DeviceMemory _hudVbMem;
        private void*        _hudMapped = null;
        private int          _hudCursor = 0;
        private const int    HudVbCapacity = 65536;

        // Font atlas: 5×7 glyphs, 8×8 cells, 8 cols × 12 rows = 96 chars (ASCII 32-127)
        private const int GlyphW = 5, GlyphH = 7, CellW = 8, CellH = 8;
        private const int AtlasCols = 8, AtlasRows = 12;
        private const int AtlasW = AtlasCols * CellW, AtlasH = AtlasRows * CellH;

        private static readonly byte[] _fontBits = BuildFontBits();

        private static byte[] BuildFontBits()
        {
            var d = new byte[96 * 7];
            void G(int idx, byte r0,byte r1,byte r2,byte r3,byte r4,byte r5,byte r6)
            { int b=idx*7; d[b]=r0;d[b+1]=r1;d[b+2]=r2;d[b+3]=r3;d[b+4]=r4;d[b+5]=r5;d[b+6]=r6; }
            G(0,  0,0,0,0,0,0,0);                                         // space
            G(1,  0x04,0x04,0x04,0x04,0x00,0x04,0x00);                   // !
            G(2,  0x0A,0x0A,0x00,0x00,0x00,0x00,0x00);                   // "
            G(3,  0x0A,0x1F,0x0A,0x0A,0x1F,0x0A,0x00);                   // #
            G(4,  0x04,0x0F,0x10,0x0E,0x01,0x1E,0x04);                   // $
            G(5,  0x11,0x09,0x02,0x04,0x08,0x13,0x11);                   // %
            G(6,  0x08,0x14,0x14,0x08,0x15,0x12,0x0D);                   // &
            G(7,  0x04,0x04,0x00,0x00,0x00,0x00,0x00);                   // '
            G(8,  0x02,0x04,0x08,0x08,0x08,0x04,0x02);                   // (
            G(9,  0x08,0x04,0x02,0x02,0x02,0x04,0x08);                   // )
            G(10, 0x00,0x04,0x15,0x0E,0x15,0x04,0x00);                   // *
            G(11, 0x00,0x04,0x04,0x1F,0x04,0x04,0x00);                   // +
            G(12, 0x00,0x00,0x00,0x00,0x04,0x04,0x08);                   // ,
            G(13, 0x00,0x00,0x00,0x1F,0x00,0x00,0x00);                   // -
            G(14, 0x00,0x00,0x00,0x00,0x00,0x04,0x00);                   // .
            G(15, 0x01,0x02,0x02,0x04,0x08,0x08,0x10);                   // /
            G(16, 0x0E,0x11,0x13,0x15,0x19,0x11,0x0E);                   // 0
            G(17, 0x04,0x0C,0x04,0x04,0x04,0x04,0x0E);                   // 1
            G(18, 0x0E,0x11,0x01,0x02,0x04,0x08,0x1F);                   // 2
            G(19, 0x1F,0x02,0x04,0x02,0x01,0x11,0x0E);                   // 3
            G(20, 0x02,0x06,0x0A,0x12,0x1F,0x02,0x02);                   // 4
            G(21, 0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E);                   // 5
            G(22, 0x06,0x08,0x10,0x1E,0x11,0x11,0x0E);                   // 6
            G(23, 0x1F,0x01,0x02,0x04,0x08,0x08,0x08);                   // 7
            G(24, 0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E);                   // 8
            G(25, 0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C);                   // 9
            G(26, 0x00,0x04,0x00,0x00,0x04,0x00,0x00);                   // :
            G(27, 0x00,0x04,0x00,0x00,0x04,0x04,0x08);                   // ;
            G(28, 0x02,0x04,0x08,0x10,0x08,0x04,0x02);                   // <
            G(29, 0x00,0x00,0x1F,0x00,0x1F,0x00,0x00);                   // =
            G(30, 0x08,0x04,0x02,0x01,0x02,0x04,0x08);                   // >
            G(31, 0x0E,0x11,0x01,0x02,0x04,0x00,0x04);                   // ?
            G(32, 0x0E,0x11,0x17,0x15,0x17,0x10,0x0E);                   // @
            // A-Z (indices 33-58)
            byte[][,] azGlyphs = {
                new byte[,]{{0x0E},{0x11},{0x11},{0x1F},{0x11},{0x11},{0x11}}, // A
                new byte[,]{{0x1E},{0x11},{0x11},{0x1E},{0x11},{0x11},{0x1E}}, // B
                new byte[,]{{0x0E},{0x11},{0x10},{0x10},{0x10},{0x11},{0x0E}}, // C
                new byte[,]{{0x1E},{0x11},{0x11},{0x11},{0x11},{0x11},{0x1E}}, // D
                new byte[,]{{0x1F},{0x10},{0x10},{0x1E},{0x10},{0x10},{0x1F}}, // E
                new byte[,]{{0x1F},{0x10},{0x10},{0x1E},{0x10},{0x10},{0x10}}, // F
                new byte[,]{{0x0E},{0x11},{0x10},{0x17},{0x11},{0x11},{0x0F}}, // G
                new byte[,]{{0x11},{0x11},{0x11},{0x1F},{0x11},{0x11},{0x11}}, // H
                new byte[,]{{0x1F},{0x04},{0x04},{0x04},{0x04},{0x04},{0x1F}}, // I
                new byte[,]{{0x0F},{0x01},{0x01},{0x01},{0x01},{0x11},{0x0E}}, // J
                new byte[,]{{0x11},{0x12},{0x14},{0x18},{0x14},{0x12},{0x11}}, // K
                new byte[,]{{0x10},{0x10},{0x10},{0x10},{0x10},{0x10},{0x1F}}, // L
                new byte[,]{{0x11},{0x1B},{0x15},{0x15},{0x11},{0x11},{0x11}}, // M
                new byte[,]{{0x11},{0x19},{0x15},{0x13},{0x11},{0x11},{0x11}}, // N
                new byte[,]{{0x0E},{0x11},{0x11},{0x11},{0x11},{0x11},{0x0E}}, // O
                new byte[,]{{0x1E},{0x11},{0x11},{0x1E},{0x10},{0x10},{0x10}}, // P
                new byte[,]{{0x0E},{0x11},{0x11},{0x11},{0x15},{0x12},{0x0D}}, // Q
                new byte[,]{{0x1E},{0x11},{0x11},{0x1E},{0x14},{0x12},{0x11}}, // R
                new byte[,]{{0x0E},{0x11},{0x10},{0x0E},{0x01},{0x11},{0x0E}}, // S
                new byte[,]{{0x1F},{0x04},{0x04},{0x04},{0x04},{0x04},{0x04}}, // T
                new byte[,]{{0x11},{0x11},{0x11},{0x11},{0x11},{0x11},{0x0E}}, // U
                new byte[,]{{0x11},{0x11},{0x11},{0x11},{0x11},{0x0A},{0x04}}, // V
                new byte[,]{{0x11},{0x11},{0x11},{0x15},{0x15},{0x1B},{0x11}}, // W
                new byte[,]{{0x11},{0x11},{0x0A},{0x04},{0x0A},{0x11},{0x11}}, // X
                new byte[,]{{0x11},{0x11},{0x0A},{0x04},{0x04},{0x04},{0x04}}, // Y
                new byte[,]{{0x1F},{0x01},{0x02},{0x04},{0x08},{0x10},{0x1F}}, // Z
            };
            for (int i = 0; i < 26; i++)
            {
                int idx = 33 + i;
                int b2  = idx * 7;
                for (int r = 0; r < 7; r++) d[b2 + r] = azGlyphs[i][r, 0];
            }
            // Misc punctuation (59-94)
            G(59, 0x0E,0x08,0x08,0x08,0x08,0x08,0x0E); // [
            G(60, 0x10,0x08,0x08,0x04,0x02,0x02,0x01); // backslash
            G(61, 0x0E,0x02,0x02,0x02,0x02,0x02,0x0E); // ]
            G(62, 0x04,0x0A,0x11,0x00,0x00,0x00,0x00); // ^
            G(63, 0x00,0x00,0x00,0x00,0x00,0x00,0x1F); // _
            G(64, 0x08,0x04,0x00,0x00,0x00,0x00,0x00); // `
            // a-z: mirror uppercase (caps-only display)
            for (int i = 0; i < 26; i++)
            {
                int srcB = (33 + i) * 7, dstB = (65 + i) * 7;
                for (int r = 0; r < 7; r++) d[dstB + r] = d[srcB + r];
            }
            G(91, 0x02,0x04,0x04,0x08,0x04,0x04,0x02); // {
            G(92, 0x04,0x04,0x04,0x04,0x04,0x04,0x04); // |
            G(93, 0x08,0x04,0x04,0x02,0x04,0x04,0x08); // }
            G(94, 0x00,0x04,0x0A,0x11,0x00,0x00,0x00); // ~
            G(95, 0,0,0,0,0,0,0);                      // DEL = blank
            return d;
        }

        private static readonly byte[] _mainUtf8 = [(byte)'m',(byte)'a',(byte)'i',(byte)'n',0];

        public WorldRenderer(
            VulkanContext        vk,
            VehicleManager       vehicles,
            TerrainLoader        terrain,
            ProceduralEnvironment? environment = null)
        {
            _vk          = vk;
            _vehicles    = vehicles;
            _terrain     = terrain;
            _environment = environment;
        }

        public void BindSwapchain(SwapchainRenderer sc) => _swapchain = sc;

        public async Task InitAsync()
        {
            await Task.Run(() =>
            {
                CreateGeomLayout();
                CreateVignetteLayout();
                CreateVehiclePipeline();
                CreatePropPipeline();
                CreateTerrainPipeline();
                CreateVignettePipeline();
                BuildPropMeshes();
                CreateHudSampler();
                CreateHudWhiteTexture();
                CreateHudDescriptorSetLayout();
                CreateHudDescriptorPool();
                AllocWriteHudWhiteDesc();
                CreateHudFontAtlas();
                LoadHudPipeline();
                CreateHudVertexBuffer();
            });

            EventBus.Subscribe<TerrainLoadedEvent>(_ => _terrainDirty = true);
            EventBus.Subscribe<VehicleSpawnedEvent>(e => EnsureVehicleGpu(e.VehicleId));
            EventBus.Subscribe<VehicleRemovedEvent>(e => DestroyVehicleGpu(e.VehicleId));
        }

        // ── Public render entry point ─────────────────────────────────────────

        public void Render(in RenderContext ctx, IViewerMode mode, ChaseHUD? hud = null)
        {
            if (_terrainDirty && _terrain.Heights != null)
            {
                RebuildTerrain();
                _terrainDirty = false;
            }

            SyncVehicleBuffers();

            var cmd         = ctx.Cmd;
            var vp          = new Viewport(0, 0, ctx.Width, ctx.Height, 0, 1);
            var scissor     = new Rect2D(default, new Extent2D(ctx.Width, ctx.Height));
            var fogColor    = _environment?.FogColor ?? new Vector3(0.05f, 0.05f, 0.08f);
            var fogDensity  = _environment?.FogDensity ?? 0.02f;
            var fogVec4     = new Vector4(fogColor, 1f);
            var viewProj    = ctx.ViewMatrix * ctx.ProjectionMatrix;

            _vk.Vk.CmdSetViewport(cmd, 0, 1, &vp);
            _vk.Vk.CmdSetScissor(cmd,  0, 1, &scissor);

            DrawTerrain(cmd, viewProj, fogVec4, fogDensity);
            DrawProps(cmd, viewProj, fogVec4, fogDensity);
            DrawVehicles(cmd, viewProj, fogVec4, fogDensity);

            if (hud != null)
            {
                DrawVignette(cmd, hud);
                DrawResultsOverlay(cmd, hud, ctx.Width, ctx.Height);
            }
        }

        // ── Terrain ───────────────────────────────────────────────────────────

        private void DrawTerrain(CommandBuffer cmd, Matrix4x4 viewProj,
                                 Vector4 fogColor, float fogDensity)
        {
            if (_terrainIndexCount == 0) return;

            _vk.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _terrainPipeline);
            ulong zero = 0;
            _vk.Vk.CmdBindVertexBuffers(cmd, 0, 1, &_terrainVb, &zero);
            _vk.Vk.CmdBindIndexBuffer(cmd, _terrainIb, 0, IndexType.Uint32);

            var push = new BeamPush
            {
                Model      = Matrix4x4.Identity,
                ViewProj   = viewProj,
                FogColor   = fogColor,
                FogDensity = fogDensity,
            };
            _vk.Vk.CmdPushConstants(cmd, _geomLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0, (uint)sizeof(BeamPush), &push);

            _vk.Vk.CmdDrawIndexed(cmd, _terrainIndexCount, 1, 0, 0, 0);
        }

        private void RebuildTerrain()
        {
            var (verts, idxArr) = _terrain.BuildMesh(256);
            if (verts.Length == 0) return;

            DestroyBuffer(ref _terrainVb, ref _terrainVbMem);
            DestroyBuffer(ref _terrainIb, ref _terrainIbMem);

            CreateBuffer(verts,  BufferUsageFlags.VertexBufferBit, out _terrainVb,  out _terrainVbMem);
            CreateBuffer(idxArr, BufferUsageFlags.IndexBufferBit,  out _terrainIb,  out _terrainIbMem);
            _terrainIndexCount = (uint)idxArr.Length;
        }

        // ── Props ─────────────────────────────────────────────────────────────

        private void BuildPropMeshes()
        {
            var types = new Dictionary<PropInstance.PropType, (float[] verts, uint[] idx)>
            {
                [PropInstance.PropType.Barrier]       = PropMeshBuilder.Barrier(),
                [PropInstance.PropType.Container]     = PropMeshBuilder.ShippingContainer(
                    new Vector4(0.6f, 0.1f, 0.1f, 1f)),
                [PropInstance.PropType.AbandonedCar]  = PropMeshBuilder.AbandonedCar(),
                [PropInstance.PropType.ConcreteBlock] = PropMeshBuilder.ConcreteBlock(),
                [PropInstance.PropType.StreetLight]   = PropMeshBuilder.StreetLight(true),
                [PropInstance.PropType.EscapeBeacon]  = PropMeshBuilder.EscapeBeacon(),
            };

            foreach (var (type, (verts, idx)) in types)
            {
                CreateBuffer(verts, BufferUsageFlags.VertexBufferBit, out var vb, out var vm);
                CreateBuffer(idx,   BufferUsageFlags.IndexBufferBit,  out var ib, out var im);
                _propMeshes[type] = (vb, ib, (uint)idx.Length, vm, im);
            }
        }

        private void DrawProps(CommandBuffer cmd, Matrix4x4 viewProj,
                               Vector4 fogColor, float fogDensity)
        {
            if (_environment == null) return;

            _vk.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _propPipeline);

            foreach (var prop in _environment.Props)
            {
                if (!_propMeshes.TryGetValue(prop.Type, out var mesh)) continue;

                ulong zero = 0;
                _vk.Vk.CmdBindVertexBuffers(cmd, 0, 1, &mesh.vb, &zero);
                _vk.Vk.CmdBindIndexBuffer(cmd, mesh.ib, 0, IndexType.Uint32);

                float emissive = (prop.Type == PropInstance.PropType.StreetLight && prop.LightOn)
                    || prop.Type == PropInstance.PropType.EscapeBeacon
                    ? 1f : 0f;

                var push = new BeamPush
                {
                    Model      = prop.Transform.ToMatrix(),
                    ViewProj   = viewProj,
                    FogColor   = fogColor,
                    FogDensity = fogDensity,
                    Emissive   = emissive,
                };
                _vk.Vk.CmdPushConstants(cmd, _geomLayout,
                    ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    0, (uint)sizeof(BeamPush), &push);

                _vk.Vk.CmdDrawIndexed(cmd, mesh.count, 1, 0, 0, 0);
            }
        }

        // ── Vehicles ──────────────────────────────────────────────────────────

        private void SyncVehicleBuffers()
        {
            foreach (var (id, v) in _vehicles.Vehicles)
            {
                EnsureVehicleGpu(id);
                var gpu = _vehicleGpu[id];

                // Rebuild GPU buffer if wheel heights changed (deformation / suspension)
                // Only rebuild if any wheel moved more than 5 mm — filters suspension
                // micro-jitter that would otherwise re-allocate GPU buffers every frame.
                const float RebuildThreshold = 0.005f;
                bool rebuild = (gpu.LastWheelHeights == null) != (v.WheelHeights == null);
                if (!rebuild && v.WheelHeights != null && gpu.LastWheelHeights != null)
                {
                    int n = Math.Min(v.WheelHeights.Length, gpu.LastWheelHeights.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (MathF.Abs(v.WheelHeights[i] - gpu.LastWheelHeights[i]) > RebuildThreshold)
                        { rebuild = true; break; }
                    }
                }

                if (rebuild)
                {
                    var color = VehicleMeshBuilder.ColorForPlayer(gpu.ColorSlot);
                    var (verts, idx) = VehicleMeshBuilder.BuildCar(color, v.WheelHeights);
                    RebuildVehicleBuffer(gpu, verts, idx);
                    gpu.LastWheelHeights = v.WheelHeights != null
                        ? (float[])v.WheelHeights.Clone() : null;
                }
            }
        }

        private void EnsureVehicleGpu(string id)
        {
            if (_vehicleGpu.ContainsKey(id)) return;
            int slot = _vehicleColorCounter++ % VehicleMeshBuilder.PlayerColors.Length;
            var color = VehicleMeshBuilder.ColorForPlayer(slot);
            var (verts, idx) = VehicleMeshBuilder.BuildCar(color);
            var gpu = new VehicleGpu { ColorSlot = slot };
            RebuildVehicleBuffer(gpu, verts, idx);
            _vehicleGpu[id] = gpu;
        }

        private void RebuildVehicleBuffer(VehicleGpu gpu, float[] verts, uint[] idx)
        {
            var dummyVb = gpu.Vb; var dummyVbm = gpu.VbMem;
            var dummyIb = gpu.Ib; var dummyIbm = gpu.IbMem;
            DestroyBuffer(ref dummyVb, ref dummyVbm);
            DestroyBuffer(ref dummyIb, ref dummyIbm);
            gpu.Vb = dummyVb; gpu.VbMem = dummyVbm;
            gpu.Ib = dummyIb; gpu.IbMem = dummyIbm;

            CreateBuffer(verts, BufferUsageFlags.VertexBufferBit, out gpu.Vb, out gpu.VbMem);
            CreateBuffer(idx,   BufferUsageFlags.IndexBufferBit,  out gpu.Ib, out gpu.IbMem);
            gpu.IndexCount = (uint)idx.Length;
        }

        private void DestroyVehicleGpu(string id)
        {
            if (!_vehicleGpu.TryGetValue(id, out var gpu)) return;
            _vehicleGpu.Remove(id);
            var vb = gpu.Vb; var vm = gpu.VbMem;
            var ib = gpu.Ib; var im = gpu.IbMem;
            DestroyBuffer(ref vb, ref vm);
            DestroyBuffer(ref ib, ref im);
        }

        private void DrawVehicles(CommandBuffer cmd, Matrix4x4 viewProj,
                                  Vector4 fogColor, float fogDensity)
        {
            _vk.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _vehiclePipeline);

            foreach (var (id, v) in _vehicles.Vehicles)
            {
                if (!_vehicleGpu.TryGetValue(id, out var gpu)) continue;
                if (gpu.IndexCount == 0) continue;

                ulong zero = 0;
                _vk.Vk.CmdBindVertexBuffers(cmd, 0, 1, &gpu.Vb, &zero);
                _vk.Vk.CmdBindIndexBuffer(cmd, gpu.Ib, 0, IndexType.Uint32);

                var push = new BeamPush
                {
                    Model      = MathEx.TRS(v.Position, v.Rotation, Vector3.One),
                    ViewProj   = viewProj,
                    FogColor   = fogColor,
                    FogDensity = fogDensity,
                };
                _vk.Vk.CmdPushConstants(cmd, _geomLayout,
                    ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    0, (uint)sizeof(BeamPush), &push);

                _vk.Vk.CmdDrawIndexed(cmd, gpu.IndexCount, 1, 0, 0, 0);
            }
        }

        // ── Vignette (fullscreen overlay) ─────────────────────────────────────

        private void DrawVignette(CommandBuffer cmd, ChaseHUD hud)
        {
            float overlayA = hud.ShowOverlay ? hud.OverlayAlpha : 0f;
            if (hud.VignetteStrength < 0.01f && hud.FlashAlpha < 0.01f &&
                hud.HeartbeatPulse < 0.01f && overlayA < 0.01f) return;

            _vk.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _vignettePipeline);
            var push = new VignettePush
            {
                Strength     = hud.VignetteStrength,
                FlashAlpha   = hud.FlashAlpha,
                Heartbeat    = hud.HeartbeatPulse,
                OverlayAlpha = overlayA,
                FlashColor   = hud.FlashColor,
                OverlayColor = new Vector3(0.04f, 0.04f, 0.04f),
                Stamina      = hud.StaminaFraction,
                ShowStamina  = hud.ShowStamina ? 1f : 0f,
            };
            _vk.Vk.CmdPushConstants(cmd, _vignetteLayout,
                ShaderStageFlags.FragmentBit, 0, (uint)sizeof(VignettePush), &push);
            _vk.Vk.CmdDraw(cmd, 3, 1, 0, 0);
        }

        // ── HUD text overlay ──────────────────────────────────────────────────

        private void DrawResultsOverlay(CommandBuffer cmd, ChaseHUD hud,
            float w, float h)
        {
            if (_hudMapped == null) return;
            _hudCursor = 0;

            // NDC ortho: pixel coords (0..w, 0..h) → NDC (-1..1, -1..1)
            var proj = new Matrix4x4(
                2f/w, 0,    0, 0,
                0,    2f/h, 0, 0,
                0,    0,    1, 0,
               -1f,  -1f,  0, 1);

            _vk.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _hudPipeline);
            var vp = new Viewport(0, 0, w, h, 0, 1);
            var sc = new Rect2D(default, new Extent2D((uint)w, (uint)h));
            _vk.Vk.CmdSetViewport(cmd, 0, 1, &vp);
            _vk.Vk.CmdSetScissor(cmd,  0, 1, &sc);

            // ── Beacon compass: arrow at bottom of view pointing to beacon ────
            DrawBeaconCompass(cmd, proj, hud, w, h);

            if (!hud.ShowOverlay) return;

            float alpha = hud.OverlayAlpha;
            if (alpha < 0.01f) return;

            bool caught  = hud.Phase == Chase.ChasePhase.Caught;
            var  bgColor = caught
                ? new Vector4(0.08f, 0.02f, 0.02f, alpha)
                : new Vector4(0.02f, 0.08f, 0.04f, alpha);
            var titleColor = caught
                ? new Vector4(1f, 0.3f, 0.3f, alpha)
                : new Vector4(0.3f, 1f, 0.4f, alpha);

            // Background panel — fills most of screen
            float panW = w * 0.72f, panH = h * 0.62f;
            float panX = (w - panW) * 0.5f, panY = (h - panH) * 0.5f;
            DrawSolidQuad(cmd, proj, panX, panY, panW, panH, bgColor);

            // Title
            float titleScale = 4f;
            string title = hud.OverlayTitle;
            float  titleW = title.Length * CellW * titleScale;
            DrawText(cmd, proj, title,
                panX + (panW - titleW) * 0.5f, panY + 18, titleScale,
                titleColor);

            // Separator line
            float sepY = panY + 18 + CellH * titleScale + 10;
            DrawSolidQuad(cmd, proj, panX + 20, sepY, panW - 40, 2,
                new Vector4(1, 1, 1, alpha * 0.35f));

            // Line 1 (survival time + attempt)
            float line1Scale = 2.2f;
            float lineY = sepY + 12;
            DrawText(cmd, proj, hud.OverlayLine1,
                panX + 24, lineY, line1Scale,
                new Vector4(0.92f, 0.92f, 0.92f, alpha));

            // Line 2 (best time or NEW BEST)
            if (!string.IsNullOrEmpty(hud.OverlayLine2))
            {
                float line2Y = lineY + CellH * line1Scale + 6;
                var line2Color = hud.OverlayLine2 == "NEW BEST"
                    ? new Vector4(1f, 0.92f, 0.2f, alpha)
                    : new Vector4(0.7f, 0.7f, 0.7f, alpha);
                DrawText(cmd, proj, hud.OverlayLine2,
                    panX + 24, line2Y, line1Scale, line2Color);
            }

            // Restart prompt at the bottom
            float promptScale = 1.8f;
            float promptY = panY + panH - CellH * promptScale - 18;
            float promptW = hud.OverlayPrompt.Length * CellW * promptScale;
            DrawText(cmd, proj, hud.OverlayPrompt,
                panX + (panW - promptW) * 0.5f, promptY, promptScale,
                new Vector4(0.85f, 0.85f, 0.85f, alpha * 0.8f));
        }

        private void DrawBeaconCompass(CommandBuffer cmd, Matrix4x4 proj,
            ChaseHUD hud, float w, float h)
        {
            // Skip when overlay is showing — compass is distracting during end screen
            if (hud.ShowOverlay && hud.OverlayAlpha > 0.5f) return;

            // ThreatBearing is the bearing TO the vehicle; the beacon direction is a fixed
            // angle from the player's perspective computed by ChaseHUD.  We approximate
            // the beacon compass using ThreatBearing as a placeholder.
            // The actual direction comes from the world-space bearing of BeaconPosition
            // vs Player.Position — ChaseHUD exposes ThreatBearing for the threat arrow.
            // For the beacon we use a static indicator at the top of the screen.
            float compassR = 48f;
            float cx = w * 0.5f, cy = compassR + 12f;

            // Ring background
            DrawSolidQuad(cmd, proj, cx - compassR, cy - compassR,
                compassR * 2, compassR * 2,
                new Vector4(0.05f, 0.35f, 0.3f, 0.45f));

            // "BEACON" label
            float labScale = 1.4f;
            string lab = "BEACON";
            float labW = lab.Length * CellW * labScale;
            DrawText(cmd, proj, lab,
                cx - labW * 0.5f, cy - CellH * labScale * 0.5f, labScale,
                new Vector4(0.1f, 1f, 0.85f, 0.9f));
        }

        private void DrawSolidQuad(CommandBuffer cmd, Matrix4x4 proj,
            float x, float y, float qw, float qh, Vector4 color)
        {
            var verts = new float[6 * 5];
            EmitQuad(verts, 0, x, y, qw, qh, 0, 0, 1, 1);
            UploadAndDrawHud(cmd, proj, verts, color, _whiteDescSet,
                scaleU: 1, scaleV: 1, offU: 0, offV: 0);
        }

        private void DrawText(CommandBuffer cmd, Matrix4x4 proj,
            string text, float x, float y, float scale, Vector4 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            float gw = CellW * scale, gh = CellH * scale;
            float cellUW = CellW / (float)AtlasW;
            float cellVH = CellH / (float)AtlasH;

            var verts = new float[text.Length * 6 * 5];
            int at = 0;
            float cx = x;
            foreach (char c in text.ToUpperInvariant())
            {
                if (c == ' ') { cx += gw; continue; }
                var (u0, v0) = GlyphUV(c);
                at = EmitQuad(verts, at, cx, y, gw, gh, u0, v0, u0 + cellUW, v0 + cellVH);
                cx += gw;
            }
            if (at == 0) return;

            UploadAndDrawHud(cmd, proj, verts[..at], color, _fontDescSet,
                scaleU: 1, scaleV: 1, offU: 0, offV: 0);
        }

        private static (float u, float v) GlyphUV(char c)
        {
            int idx = c >= 32 && c < 128 ? c - 32 : 0;
            int col = idx % AtlasCols, row = idx / AtlasCols;
            return (col * CellW / (float)AtlasW, row * CellH / (float)AtlasH);
        }

        private static int EmitQuad(float[] buf, int at,
            float x, float y, float qw, float qh,
            float u0, float v0, float u1, float v1)
        {
            float[] q = {
                x,    y,    0, u0, v0,
                x,    y+qh, 0, u0, v1,
                x+qw, y,    0, u1, v0,
                x+qw, y,    0, u1, v0,
                x,    y+qh, 0, u0, v1,
                x+qw, y+qh, 0, u1, v1,
            };
            Array.Copy(q, 0, buf, at, q.Length);
            return at + q.Length;
        }

        private void UploadAndDrawHud(CommandBuffer cmd, Matrix4x4 proj,
            float[] verts, Vector4 color, DescriptorSet ds,
            float scaleU, float scaleV, float offU, float offV)
        {
            if (verts.Length == 0 || _hudMapped == null) return;
            int byteOffset = _hudCursor * 5 * sizeof(float);
            int byteLen    = verts.Length * sizeof(float);
            if (byteOffset + byteLen > HudVbCapacity) return;

            new Span<float>((float*)_hudMapped + _hudCursor * 5, verts.Length)
                .TryCopyFrom(verts);

            ulong off = (ulong)byteOffset;
            _vk.Vk.CmdBindVertexBuffers(cmd, 0, 1, &_hudVb, &off);

            var dsLocal = ds;
            _vk.Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics,
                _hudLayout, 0, 1, &dsLocal, 0, null);

            var push = new UIPush
            {
                Transform = proj,
                Color     = color,
                UvScaleX  = scaleU, UvScaleY = scaleV,
                UvOffsetX = offU,   UvOffsetY = offV,
            };
            _vk.Vk.CmdPushConstants(cmd, _hudLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0, (uint)sizeof(UIPush), &push);

            _vk.Vk.CmdDraw(cmd, (uint)(verts.Length / 5), 1, 0, 0);
            _hudCursor += verts.Length / 5;
        }

        // ── HUD pipeline setup ────────────────────────────────────────────────

        private void CreateHudSampler()
        {
            var si = new SamplerCreateInfo
            {
                SType        = StructureType.SamplerCreateInfo,
                MagFilter    = Filter.Linear,
                MinFilter    = Filter.Linear,
                MipmapMode   = SamplerMipmapMode.Linear,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                MaxLod       = 16f,
            };
            Sampler s;
            _vk.Vk.CreateSampler(_vk.Device, &si, null, &s);
            _hudSampler = s;
        }

        private void CreateHudWhiteTexture()
        {
            var ici = new ImageCreateInfo
            {
                SType       = StructureType.ImageCreateInfo,
                ImageType   = ImageType.Type2D,
                Format      = Format.R8G8B8A8Srgb,
                Extent      = new Extent3D(1, 1, 1),
                MipLevels   = 1,
                ArrayLayers = 1,
                Samples     = SampleCountFlags.Count1Bit,
                Tiling      = ImageTiling.Optimal,
                Usage       = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            Silk.NET.Vulkan.Image img;
            _vk.Vk.CreateImage(_vk.Device, &ici, null, &img);

            MemoryRequirements req;
            _vk.Vk.GetImageMemoryRequirements(_vk.Device, img, &req);
            var mai = new MemoryAllocateInfo
            {
                SType           = StructureType.MemoryAllocateInfo,
                AllocationSize  = req.Size,
                MemoryTypeIndex = _vk.FindMemoryType(req.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit),
            };
            DeviceMemory mem;
            _vk.Vk.AllocateMemory(_vk.Device, &mai, null, &mem);
            _vk.Vk.BindImageMemory(_vk.Device, img, mem, 0);

            byte[] white = [255, 255, 255, 255];
            CreateBuffer(white, BufferUsageFlags.TransferSrcBit, out var staging, out var stagMem);

            var cb = _vk.BeginOneShot();
            TransitionImage(cb, img, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            var region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers
                    { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new Extent3D(1, 1, 1),
            };
            _vk.Vk.CmdCopyBufferToImage(cb, staging, img, ImageLayout.TransferDstOptimal, 1, &region);
            TransitionImage(cb, img, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
            _vk.EndOneShot(cb);

            _vk.Vk.DestroyBuffer(_vk.Device, staging, null);
            _vk.Vk.FreeMemory(_vk.Device, stagMem, null);

            var ivci = new ImageViewCreateInfo
            {
                SType    = StructureType.ImageViewCreateInfo,
                Image    = img,
                ViewType = ImageViewType.Type2D,
                Format   = Format.R8G8B8A8Srgb,
                SubresourceRange = new ImageSubresourceRange
                    { AspectMask = ImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 },
            };
            ImageView view;
            _vk.Vk.CreateImageView(_vk.Device, &ivci, null, &view);

            _whiteTex = img; _whiteTexMem = mem; _whiteTexView = view;
        }

        private void CreateHudDescriptorSetLayout()
        {
            var binding = new DescriptorSetLayoutBinding
            {
                Binding         = 0,
                DescriptorType  = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags      = ShaderStageFlags.FragmentBit,
            };
            var lci = new DescriptorSetLayoutCreateInfo
            {
                SType        = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings    = &binding,
            };
            DescriptorSetLayout dsl;
            _vk.Vk.CreateDescriptorSetLayout(_vk.Device, &lci, null, &dsl);
            _hudDescLayout = dsl;
        }

        private void CreateHudDescriptorPool()
        {
            var size = new DescriptorPoolSize
                { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 8 };
            var pci = new DescriptorPoolCreateInfo
            {
                SType         = StructureType.DescriptorPoolCreateInfo,
                MaxSets       = 8,
                PoolSizeCount = 1,
                PPoolSizes    = &size,
            };
            DescriptorPool pool;
            _vk.Vk.CreateDescriptorPool(_vk.Device, &pci, null, &pool);
            _hudDescPool = pool;
        }

        private void AllocWriteHudWhiteDesc()
        {
            var layout = _hudDescLayout;
            var ai = new DescriptorSetAllocateInfo
            {
                SType              = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool     = _hudDescPool,
                DescriptorSetCount = 1,
                PSetLayouts        = &layout,
            };
            DescriptorSet ds;
            _vk.Vk.AllocateDescriptorSets(_vk.Device, &ai, &ds);
            _whiteDescSet = ds;

            var imgInfo = new DescriptorImageInfo
            {
                Sampler     = _hudSampler,
                ImageView   = _whiteTexView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            };
            var write = new WriteDescriptorSet
            {
                SType           = StructureType.WriteDescriptorSet,
                DstSet          = ds,
                DstBinding      = 0,
                DescriptorCount = 1,
                DescriptorType  = DescriptorType.CombinedImageSampler,
                PImageInfo      = &imgInfo,
            };
            _vk.Vk.UpdateDescriptorSets(_vk.Device, 1, &write, 0, null);
        }

        private void CreateHudFontAtlas()
        {
            var pixels = new byte[AtlasW * AtlasH];
            for (int ci = 0; ci < 96; ci++)
            {
                int col = ci % AtlasCols, row = ci / AtlasCols;
                int bx  = col * CellW, by = row * CellH;
                for (int py = 0; py < GlyphH; py++)
                {
                    byte bits = _fontBits[ci * 7 + py];
                    for (int px = 0; px < GlyphW; px++)
                    {
                        bool lit = (bits >> (4 - px) & 1) != 0;
                        pixels[(by + py) * AtlasW + (bx + px)] = lit ? (byte)255 : (byte)0;
                    }
                }
            }

            var ici = new ImageCreateInfo
            {
                SType       = StructureType.ImageCreateInfo,
                ImageType   = ImageType.Type2D,
                Format      = Format.R8Unorm,
                Extent      = new Extent3D((uint)AtlasW, (uint)AtlasH, 1),
                MipLevels   = 1,
                ArrayLayers = 1,
                Samples     = SampleCountFlags.Count1Bit,
                Tiling      = ImageTiling.Optimal,
                Usage       = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            Silk.NET.Vulkan.Image img;
            _vk.Vk.CreateImage(_vk.Device, &ici, null, &img);

            MemoryRequirements req;
            _vk.Vk.GetImageMemoryRequirements(_vk.Device, img, &req);
            var mai = new MemoryAllocateInfo
            {
                SType           = StructureType.MemoryAllocateInfo,
                AllocationSize  = req.Size,
                MemoryTypeIndex = _vk.FindMemoryType(req.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit),
            };
            DeviceMemory mem;
            _vk.Vk.AllocateMemory(_vk.Device, &mai, null, &mem);
            _vk.Vk.BindImageMemory(_vk.Device, img, mem, 0);

            CreateBuffer(pixels, BufferUsageFlags.TransferSrcBit, out var staging, out var stagMem);

            var cb = _vk.BeginOneShot();
            TransitionImage(cb, img, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            var region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers
                    { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new Extent3D((uint)AtlasW, (uint)AtlasH, 1),
            };
            _vk.Vk.CmdCopyBufferToImage(cb, staging, img, ImageLayout.TransferDstOptimal, 1, &region);
            TransitionImage(cb, img, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
            _vk.EndOneShot(cb);

            _vk.Vk.DestroyBuffer(_vk.Device, staging, null);
            _vk.Vk.FreeMemory(_vk.Device, stagMem, null);

            var ivci = new ImageViewCreateInfo
            {
                SType    = StructureType.ImageViewCreateInfo,
                Image    = img,
                ViewType = ImageViewType.Type2D,
                Format   = Format.R8Unorm,
                SubresourceRange = new ImageSubresourceRange
                    { AspectMask = ImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 },
            };
            ImageView view;
            _vk.Vk.CreateImageView(_vk.Device, &ivci, null, &view);

            _fontTex = img; _fontTexMem = mem; _fontTexView = view;

            // Write font descriptor set
            var layout = _hudDescLayout;
            var dsAi = new DescriptorSetAllocateInfo
            {
                SType              = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool     = _hudDescPool,
                DescriptorSetCount = 1,
                PSetLayouts        = &layout,
            };
            DescriptorSet ds;
            _vk.Vk.AllocateDescriptorSets(_vk.Device, &dsAi, &ds);
            _fontDescSet = ds;

            var fontImgInfo = new DescriptorImageInfo
            {
                Sampler     = _hudSampler,
                ImageView   = _fontTexView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            };
            var fontWrite = new WriteDescriptorSet
            {
                SType           = StructureType.WriteDescriptorSet,
                DstSet          = ds,
                DstBinding      = 0,
                DescriptorCount = 1,
                DescriptorType  = DescriptorType.CombinedImageSampler,
                PImageInfo      = &fontImgInfo,
            };
            _vk.Vk.UpdateDescriptorSets(_vk.Device, 1, &fontWrite, 0, null);
        }

        private void LoadHudPipeline()
        {
            var vertSpv = LoadSpirV("ui.vert");
            var fragSpv = LoadSpirV("ui.frag");
            var vertMod = CreateShaderModule(vertSpv);
            var fragMod = CreateShaderModule(fragSpv);

            var pcRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                Size       = (uint)sizeof(UIPush),
            };
            var descLayout = _hudDescLayout;
            var plci = new PipelineLayoutCreateInfo
            {
                SType                  = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount         = 1,
                PSetLayouts            = &descLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges    = &pcRange,
            };
            PipelineLayout pl;
            _vk.Vk.CreatePipelineLayout(_vk.Device, &plci, null, &pl);
            _hudLayout = pl;

            // UI vertex: vec3 pos + vec2 uv = 20 bytes
            var vbDesc = new VertexInputBindingDescription
                { Binding = 0, Stride = 5 * sizeof(float), InputRate = VertexInputRate.Vertex };
            var attrs = stackalloc VertexInputAttributeDescription[]
            {
                new() { Binding=0, Location=0, Format=Format.R32G32B32Sfloat, Offset=0  },
                new() { Binding=0, Location=1, Format=Format.R32G32Sfloat,    Offset=12 },
            };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType                           = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount   = 1,
                PVertexBindingDescriptions      = &vbDesc,
                VertexAttributeDescriptionCount = 2,
                PVertexAttributeDescriptions    = attrs,
            };

            _hudPipeline = CreatePipeline(vertMod, fragMod, &vertexInput, _hudLayout,
                depthTest: false, alphaBlend: true, cullBack: false);

            _vk.Vk.DestroyShaderModule(_vk.Device, vertMod, null);
            _vk.Vk.DestroyShaderModule(_vk.Device, fragMod, null);
        }

        private void CreateHudVertexBuffer()
        {
            var bci = new BufferCreateInfo
            {
                SType       = StructureType.BufferCreateInfo,
                Size        = HudVbCapacity,
                Usage       = BufferUsageFlags.VertexBufferBit,
                SharingMode = SharingMode.Exclusive,
            };
            Buffer b;
            _vk.Vk.CreateBuffer(_vk.Device, &bci, null, &b);

            MemoryRequirements req;
            _vk.Vk.GetBufferMemoryRequirements(_vk.Device, b, &req);
            var mai = new MemoryAllocateInfo
            {
                SType           = StructureType.MemoryAllocateInfo,
                AllocationSize  = req.Size,
                MemoryTypeIndex = _vk.FindMemoryType(req.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
            };
            DeviceMemory m;
            _vk.Vk.AllocateMemory(_vk.Device, &mai, null, &m);
            _vk.Vk.BindBufferMemory(_vk.Device, b, m, 0);

            void* ptr;
            _vk.Vk.MapMemory(_vk.Device, m, 0, HudVbCapacity, 0, &ptr);
            _hudMapped = ptr;
            _hudVb = b; _hudVbMem = m;
        }

        private void TransitionImage(CommandBuffer cb, Silk.NET.Vulkan.Image img,
            ImageLayout from, ImageLayout to)
        {
            var barrier = new ImageMemoryBarrier
            {
                SType            = StructureType.ImageMemoryBarrier,
                OldLayout        = from,
                NewLayout        = to,
                Image            = img,
                SubresourceRange = new ImageSubresourceRange
                    { AspectMask = ImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 },
                SrcAccessMask    = AccessFlags.None,
                DstAccessMask    = AccessFlags.TransferWriteBit,
            };
            _vk.Vk.CmdPipelineBarrier(cb,
                PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &barrier);
        }

        // ── Pipeline creation ─────────────────────────────────────────────────

        private void CreateGeomLayout()
        {
            var pcRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                Size       = (uint)sizeof(BeamPush),
            };
            var ci = new PipelineLayoutCreateInfo
            {
                SType                  = StructureType.PipelineLayoutCreateInfo,
                PushConstantRangeCount = 1,
                PPushConstantRanges    = &pcRange,
            };
            PipelineLayout pl;
            _vk.Vk.CreatePipelineLayout(_vk.Device, &ci, null, &pl);
            _geomLayout = pl;
        }

        private void CreateVignetteLayout()
        {
            var pcRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.FragmentBit,
                Size       = (uint)sizeof(VignettePush),
            };
            var ci = new PipelineLayoutCreateInfo
            {
                SType                  = StructureType.PipelineLayoutCreateInfo,
                PushConstantRangeCount = 1,
                PPushConstantRanges    = &pcRange,
            };
            PipelineLayout pl;
            _vk.Vk.CreatePipelineLayout(_vk.Device, &ci, null, &pl);
            _vignetteLayout = pl;
        }

        private void CreateVehiclePipeline()
            => _vehiclePipeline = BuildGeomPipeline("vehicle.vert", "vehicle.frag",
               stride: 10 * sizeof(float),
               new[] { (Format.R32G32B32Sfloat, 0u), (Format.R32G32B32Sfloat, 12u), (Format.R32G32B32A32Sfloat, 24u) });

        private void CreatePropPipeline()
            => _propPipeline = BuildGeomPipeline("prop.vert", "prop.frag",
               stride: 10 * sizeof(float),
               new[] { (Format.R32G32B32Sfloat, 0u), (Format.R32G32B32Sfloat, 12u), (Format.R32G32B32A32Sfloat, 24u) });

        private void CreateTerrainPipeline()
            => _terrainPipeline = BuildGeomPipeline("terrain.vert", "terrain.frag",
               stride: 8 * sizeof(float),
               new[] { (Format.R32G32B32Sfloat, 0u), (Format.R32G32B32Sfloat, 12u), (Format.R32G32Sfloat, 24u) });

        private Pipeline BuildGeomPipeline(
            string vertName, string fragName, int stride,
            (Format fmt, uint off)[] attrs)
        {
            var vertSpv = LoadSpirV(vertName);
            var fragSpv = LoadSpirV(fragName);
            var vertMod = CreateShaderModule(vertSpv);
            var fragMod = CreateShaderModule(fragSpv);

            var vbDesc = new VertexInputBindingDescription
            {
                Binding   = 0,
                Stride    = (uint)stride,
                InputRate = VertexInputRate.Vertex,
            };

            var attrDescs = stackalloc VertexInputAttributeDescription[attrs.Length];
            for (int i = 0; i < attrs.Length; i++)
                attrDescs[i] = new() { Binding=0, Location=(uint)i,
                    Format=attrs[i].fmt, Offset=attrs[i].off };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType                           = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount   = 1,
                PVertexBindingDescriptions      = &vbDesc,
                VertexAttributeDescriptionCount = (uint)attrs.Length,
                PVertexAttributeDescriptions    = attrDescs,
            };

            return CreatePipeline(vertMod, fragMod, &vertexInput, _geomLayout,
                depthTest: true, alphaBlend: false, cullBack: true);
        }

        private void CreateVignettePipeline()
        {
            var vertSpv = LoadSpirV("vignette.vert");
            var fragSpv = LoadSpirV("vignette.frag");
            var vertMod = CreateShaderModule(vertSpv);
            var fragMod = CreateShaderModule(fragSpv);

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
            };

            _vignettePipeline = CreatePipeline(vertMod, fragMod, &vertexInput, _vignetteLayout,
                depthTest: false, alphaBlend: true, cullBack: false);
        }

        private Pipeline CreatePipeline(
            ShaderModule vertMod, ShaderModule fragMod,
            PipelineVertexInputStateCreateInfo* vertexInput,
            PipelineLayout layout,
            bool depthTest, bool alphaBlend, bool cullBack)
        {
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType    = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };
            var viewport = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1, ScissorCount = 1,
            };
            var raster = new PipelineRasterizationStateCreateInfo
            {
                SType       = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode    = cullBack ? CullModeFlags.BackBit : CullModeFlags.None,
                FrontFace   = FrontFace.CounterClockwise,
                LineWidth   = 1f,
            };
            var ms = new PipelineMultisampleStateCreateInfo
            {
                SType                = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };
            var ds = new PipelineDepthStencilStateCreateInfo
            {
                SType            = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable  = depthTest,
                DepthWriteEnable = depthTest,
                DepthCompareOp   = CompareOp.LessOrEqual,
            };
            var blendAttach = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable    = alphaBlend,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp        = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp        = BlendOp.Add,
            };
            var blend = new PipelineColorBlendStateCreateInfo
            {
                SType           = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments    = &blendAttach,
            };
            var dynamics = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            var dynState = new PipelineDynamicStateCreateInfo
            {
                SType             = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates    = dynamics,
            };

            Pipeline pipe;
            fixed (byte* mainName = _mainUtf8)
            {
                var stages = stackalloc PipelineShaderStageCreateInfo[]
                {
                    new() { SType=StructureType.PipelineShaderStageCreateInfo,
                            Stage=ShaderStageFlags.VertexBit,   Module=vertMod, PName=mainName },
                    new() { SType=StructureType.PipelineShaderStageCreateInfo,
                            Stage=ShaderStageFlags.FragmentBit, Module=fragMod, PName=mainName },
                };
                var gpci = new GraphicsPipelineCreateInfo
                {
                    SType               = StructureType.GraphicsPipelineCreateInfo,
                    StageCount          = 2,
                    PStages             = stages,
                    PVertexInputState   = vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState      = &viewport,
                    PRasterizationState = &raster,
                    PMultisampleState   = &ms,
                    PDepthStencilState  = &ds,
                    PColorBlendState    = &blend,
                    PDynamicState       = &dynState,
                    Layout              = layout,
                    RenderPass          = _swapchain.RenderPass,
                    Subpass             = 0,
                };
                _vk.Vk.CreateGraphicsPipelines(_vk.Device, default, 1, &gpci, null, &pipe);
            }

            _vk.Vk.DestroyShaderModule(_vk.Device, vertMod, null);
            _vk.Vk.DestroyShaderModule(_vk.Device, fragMod, null);
            return pipe;
        }

        // ── Vulkan helpers ────────────────────────────────────────────────────

        private static byte[] LoadSpirV(string name)
        {
            var key   = name.Replace('.', '_');
            var field = typeof(ShaderBytecode).GetField(key,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (field?.GetValue(null) is byte[] embedded && embedded.Length > 0)
                return embedded;

            foreach (var p in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "shaders", name + ".spv"),
                Path.Combine("/data/local/tmp/beamquest/shaders", name + ".spv"),
            })
                if (File.Exists(p)) return File.ReadAllBytes(p);

            throw new FileNotFoundException($"SPIR-V missing for '{name}'. Run build.sh.");
        }

        private ShaderModule CreateShaderModule(byte[] spv)
        {
            fixed (byte* p = spv)
            {
                var ci = new ShaderModuleCreateInfo
                {
                    SType    = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)spv.Length,
                    PCode    = (uint*)p,
                };
                ShaderModule m;
                _vk.Vk.CreateShaderModule(_vk.Device, &ci, null, &m);
                return m;
            }
        }

        private void CreateBuffer<T>(T[] data, BufferUsageFlags usage,
            out Buffer buf, out DeviceMemory mem) where T : struct
        {
            int byteLen = data.Length * Marshal.SizeOf<T>();
            var bci = new BufferCreateInfo
            {
                SType       = StructureType.BufferCreateInfo,
                Size        = (ulong)byteLen,
                Usage       = usage | BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
            };
            Buffer b;
            _vk.Vk.CreateBuffer(_vk.Device, &bci, null, &b);

            MemoryRequirements req;
            _vk.Vk.GetBufferMemoryRequirements(_vk.Device, b, &req);

            uint memType;
            try { memType = _vk.FindMemoryType(req.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit); }
            catch { memType = _vk.FindMemoryType(req.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit); }

            var mai = new MemoryAllocateInfo
            {
                SType           = StructureType.MemoryAllocateInfo,
                AllocationSize  = req.Size,
                MemoryTypeIndex = memType,
            };
            DeviceMemory m2;
            _vk.Vk.AllocateMemory(_vk.Device, &mai, null, &m2);
            _vk.Vk.BindBufferMemory(_vk.Device, b, m2, 0);

            void* mapped;
            _vk.Vk.MapMemory(_vk.Device, m2, 0, (ulong)byteLen, 0, &mapped);
            new Span<T>(mapped, data.Length).TryCopyFrom(data);
            _vk.Vk.UnmapMemory(_vk.Device, m2);

            buf = b; mem = m2;
        }

        private void DestroyBuffer(ref Buffer b, ref DeviceMemory m)
        {
            if (b.Handle != 0) { _vk.Vk.DestroyBuffer(_vk.Device, b, null); b = default; }
            if (m.Handle != 0) { _vk.Vk.FreeMemory(_vk.Device, m, null);    m = default; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _vk.Vk.DeviceWaitIdle(_vk.Device);

            DestroyBuffer(ref _terrainVb, ref _terrainVbMem);
            DestroyBuffer(ref _terrainIb, ref _terrainIbMem);

            foreach (var (_, m) in _propMeshes)
            {
                var vb = m.vb; var vm = m.vm; var ib = m.ib; var im = m.im;
                DestroyBuffer(ref vb, ref vm);
                DestroyBuffer(ref ib, ref im);
            }

            foreach (var id in _vehicleGpu.Keys.ToList())
                DestroyVehicleGpu(id);

            void DestroyPipeline(ref Pipeline p)
            {
                if (p.Handle != 0) { _vk.Vk.DestroyPipeline(_vk.Device, p, null); p = default; }
            }
            DestroyPipeline(ref _vehiclePipeline);
            DestroyPipeline(ref _propPipeline);
            DestroyPipeline(ref _terrainPipeline);
            DestroyPipeline(ref _vignettePipeline);

            if (_geomLayout.Handle    != 0) _vk.Vk.DestroyPipelineLayout(_vk.Device, _geomLayout, null);
            if (_vignetteLayout.Handle != 0) _vk.Vk.DestroyPipelineLayout(_vk.Device, _vignetteLayout, null);

            // HUD text pipeline
            if (_hudMapped != null && _hudVbMem.Handle != 0)
            {
                _vk.Vk.UnmapMemory(_vk.Device, _hudVbMem);
                _hudMapped = null;
            }
            DestroyBuffer(ref _hudVb, ref _hudVbMem);
            if (_hudPipeline.Handle    != 0) _vk.Vk.DestroyPipeline(_vk.Device, _hudPipeline, null);
            if (_hudLayout.Handle      != 0) _vk.Vk.DestroyPipelineLayout(_vk.Device, _hudLayout, null);
            if (_fontTexView.Handle    != 0) _vk.Vk.DestroyImageView(_vk.Device, _fontTexView, null);
            if (_fontTex.Handle        != 0) _vk.Vk.DestroyImage(_vk.Device, _fontTex, null);
            if (_fontTexMem.Handle     != 0) _vk.Vk.FreeMemory(_vk.Device, _fontTexMem, null);
            if (_whiteTexView.Handle   != 0) _vk.Vk.DestroyImageView(_vk.Device, _whiteTexView, null);
            if (_whiteTex.Handle       != 0) _vk.Vk.DestroyImage(_vk.Device, _whiteTex, null);
            if (_whiteTexMem.Handle    != 0) _vk.Vk.FreeMemory(_vk.Device, _whiteTexMem, null);
            if (_hudDescPool.Handle    != 0) _vk.Vk.DestroyDescriptorPool(_vk.Device, _hudDescPool, null);
            if (_hudDescLayout.Handle  != 0) _vk.Vk.DestroyDescriptorSetLayout(_vk.Device, _hudDescLayout, null);
            if (_hudSampler.Handle     != 0) _vk.Vk.DestroySampler(_vk.Device, _hudSampler, null);
        }
    }
}
