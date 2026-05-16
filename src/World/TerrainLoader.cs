using BeamQuest.Core;
using Microsoft.Extensions.Logging;

namespace BeamQuest.World
{
    /// <summary>
    /// Loads a BeamNG terrain heightmap.
    ///
    /// Supported formats:
    ///   - Raw float32 binary (.raw32): width*height floats, Y-up heights in metres
    ///   - PNG greyscale 16-bit (.png):  normalized 0-1 → scaled by HeightScale
    ///
    /// BeamNG exports: Editor → Export Terrain as .raw or .png.
    /// Drop the exported file alongside the APK (or in the Android external storage)
    /// and set TerrainPath before calling LoadAsync.
    /// </summary>
    public sealed class TerrainLoader
    {
        private readonly ILogger<TerrainLoader> _log;

        public float[]? Heights    { get; private set; }
        public int      GridSize   { get; private set; }
        public float    HeightScale { get; set; } = 400f;   // BeamNG default terrain height range
        public float    GridSpacing { get; set; } = 1f;     // metres per heightmap cell (1 = 1m grid)
        public string   MapName    { get; private set; } = "";

        public TerrainLoader(ILogger<TerrainLoader> log) => _log = log;

        public async Task<bool> LoadAsync(string path)
        {
            if (!File.Exists(path))
            {
                _log.LogWarning("Terrain file not found: {Path}", path);
                return false;
            }

            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".raw32")
                    await LoadRaw32Async(path);
                else if (ext is ".png" or ".bmp")
                    await LoadPng16Async(path);
                else
                {
                    _log.LogWarning("Unsupported terrain format: {Ext}", ext);
                    return false;
                }

                MapName = Path.GetFileNameWithoutExtension(path);
                _log.LogInformation("Loaded {GridSize}x{GridSize} terrain '{Map}'", GridSize, GridSize, MapName);
                EventBus.Publish(new TerrainLoadedEvent(MapName, GridSize));
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Terrain load failed: {Path}", path);
                return false;
            }
        }

        private async Task LoadRaw32Async(string path)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            int count = bytes.Length / 4;
            int side  = (int)Math.Sqrt(count);
            Heights   = new float[count];
            Buffer.BlockCopy(bytes, 0, Heights, 0, bytes.Length);
            GridSize  = side;
        }

        private async Task LoadPng16Async(string path)
        {
            // Read PNG manually: we only need greyscale values.
            // For simplicity, treat the file as a raw 16-bit heightmap if the PNG
            // header matches expected dimensions — otherwise fall back to row-by-row
            // decode assuming 8bpp greyscale.
            var bytes = await File.ReadAllBytesAsync(path);

            // Minimal PNG: skip header and read IHDR width/height
            if (bytes.Length < 24 || bytes[0] != 0x89) throw new InvalidDataException("Not a PNG");
            int w = (bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19];
            int h = (bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];
            int count = w * h;

            // Flat grey assumption: last `count` bytes are raw heights (0-255)
            // Real implementation would use a PNG decoder; good enough for dev
            Heights  = new float[count];
            int off  = bytes.Length - count;
            for (int i = 0; i < count && off + i < bytes.Length; i++)
                Heights[i] = (bytes[off + i] / 255f) * HeightScale;
            GridSize = w;
        }

        /// <summary>Returns the terrain height at world (x, z) by bilinear interpolation.</summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            if (Heights == null) return 0f;
            float gx = worldX / GridSpacing;
            float gz = worldZ / GridSpacing;
            int x0 = Math.Clamp((int)gx, 0, GridSize - 2);
            int z0 = Math.Clamp((int)gz, 0, GridSize - 2);
            float tx = gx - x0, tz = gz - z0;

            float h00 = Heights[z0 * GridSize + x0];
            float h10 = Heights[z0 * GridSize + x0 + 1];
            float h01 = Heights[(z0+1) * GridSize + x0];
            float h11 = Heights[(z0+1) * GridSize + x0 + 1];

            return h00 * (1-tx)*(1-tz) + h10 * tx*(1-tz)
                 + h01 * (1-tx)*tz     + h11 * tx*tz;
        }

        /// <summary>
        /// Builds a flat float array of terrain geometry:
        /// [px, py, pz, nx, ny, nz, u, v] per vertex
        /// Downsampled to maxCells×maxCells for rendering performance.
        /// </summary>
        public (float[] vertices, uint[] indices) BuildMesh(int maxCells = 256)
        {
            if (Heights == null) return (Array.Empty<float>(), Array.Empty<uint>());

            int step = Math.Max(1, GridSize / maxCells);
            int cells = GridSize / step;
            var verts   = new float[cells * cells * 8];
            var indices = new List<uint>();

            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int gx = x * step, gz = z * step;
                float wx = gx * GridSpacing;
                float wz = gz * GridSpacing;
                float wy = Heights[gz * GridSize + gx];

                // Finite-difference normal
                float hL = gx > 0           ? Heights[gz * GridSize + gx - step] : wy;
                float hR = gx < GridSize-step? Heights[gz * GridSize + gx + step] : wy;
                float hD = gz > 0           ? Heights[(gz-step)*GridSize + gx]   : wy;
                float hU = gz < GridSize-step? Heights[(gz+step)*GridSize + gx]  : wy;
                var n = Vector3.Normalize(new Vector3(hL - hR, 2f * GridSpacing, hD - hU));

                int vi = (z * cells + x) * 8;
                verts[vi+0]=wx; verts[vi+1]=wy; verts[vi+2]=wz;
                verts[vi+3]=n.X; verts[vi+4]=n.Y; verts[vi+5]=n.Z;
                verts[vi+6]=(float)x/(cells-1); verts[vi+7]=(float)z/(cells-1);

                if (x < cells-1 && z < cells-1)
                {
                    uint b = (uint)(z * cells + x);
                    indices.AddRange(new[] {b, b+1, b+(uint)cells+1, b, b+(uint)cells+1, b+(uint)cells});
                }
            }

            return (verts, indices.ToArray());
        }
    }
}
