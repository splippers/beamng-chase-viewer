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

    // Vignette / flash / overlay — 48 bytes, matches vignette.frag push_constant block
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
        public float   _pad1;         // offset 44
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
                DrawVignette(cmd, hud);
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

                float emissive = prop.Type == PropInstance.PropType.StreetLight && prop.LightOn
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
                bool rebuild = gpu.LastWheelHeights == null ||
                    v.WheelHeights == null && gpu.LastWheelHeights != null ||
                    (v.WheelHeights != null && !v.WheelHeights.SequenceEqual(gpu.LastWheelHeights ?? []));

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
            };
            _vk.Vk.CmdPushConstants(cmd, _vignetteLayout,
                ShaderStageFlags.FragmentBit, 0, (uint)sizeof(VignettePush), &push);
            _vk.Vk.CmdDraw(cmd, 3, 1, 0, 0);
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
        }
    }
}
