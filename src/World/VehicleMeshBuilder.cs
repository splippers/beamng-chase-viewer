using System.Numerics;

namespace BeamQuest.World
{
    /// <summary>
    /// Builds procedural vehicle meshes.
    ///
    /// Tier 1 (now): coloured box (body) + 4 cylinder wheels.
    /// The mesh is expressed as a flat float array:
    ///   [px, py, pz, nx, ny, nz, r, g, b, a]  per vertex
    /// with a uint[] index buffer.
    ///
    /// Tier 2 (later): load the vehicle's actual OBJ/JBeam mesh from a bundle.
    /// </summary>
    public static class VehicleMeshBuilder
    {
        // Standard car proportions in metres
        private const float BodyL = 4.2f;
        private const float BodyW = 1.8f;
        private const float BodyH = 1.4f;
        private const float WheelR = 0.33f;
        private const float WheelW = 0.22f;

        private static readonly Vector2[] WheelPositions =
        {
            new(-BodyW * 0.5f - WheelW * 0.5f,  BodyL * 0.35f),   // FL
            new( BodyW * 0.5f + WheelW * 0.5f,  BodyL * 0.35f),   // FR
            new(-BodyW * 0.5f - WheelW * 0.5f, -BodyL * 0.35f),   // RL
            new( BodyW * 0.5f + WheelW * 0.5f, -BodyL * 0.35f),   // RR
        };

        public static (float[] vertices, uint[] indices) BuildCar(Vector4 color, float[] ? wheelHeights = null)
        {
            var verts   = new List<float>();
            var indices = new List<uint>();

            // Body box
            AppendBox(verts, indices,
                new Vector3(0f, BodyH * 0.5f, 0f),
                new Vector3(BodyW, BodyH, BodyL),
                color);

            // Wheels
            for (int i = 0; i < 4; i++)
            {
                float wH  = wheelHeights != null && i < wheelHeights.Length ? wheelHeights[i] : WheelR;
                float wheelY = wH - WheelR;
                var wPos = new Vector3(WheelPositions[i].X, wheelY, WheelPositions[i].Y);
                AppendCylinder(verts, indices, wPos,
                    WheelR, WheelW, 16, new Vector4(0.2f, 0.2f, 0.2f, 1f));
            }

            return (verts.ToArray(), indices.ToArray());
        }

        private static void AppendBox(
            List<float> verts, List<uint> indices,
            Vector3 center, Vector3 size, Vector4 color)
        {
            uint base_ = (uint)(verts.Count / 10);
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;

            Vector3[] corners =
            {
                center + new Vector3(-hx,-hy,-hz), center + new Vector3( hx,-hy,-hz),
                center + new Vector3( hx, hy,-hz), center + new Vector3(-hx, hy,-hz),
                center + new Vector3(-hx,-hy, hz), center + new Vector3( hx,-hy, hz),
                center + new Vector3( hx, hy, hz), center + new Vector3(-hx, hy, hz),
            };
            Vector3[] normals =
            {
                -Vector3.UnitZ, Vector3.UnitZ,
                -Vector3.UnitX, Vector3.UnitX,
                -Vector3.UnitY, Vector3.UnitY,
            };
            int[][] faces =
            {
                new[]{0,1,2,3}, new[]{5,4,7,6},
                new[]{4,0,3,7}, new[]{1,5,6,2},
                new[]{4,5,1,0}, new[]{3,2,6,7},
            };

            for (int f = 0; f < 6; f++)
            {
                var n = normals[f];
                foreach (var ci in faces[f])
                {
                    var p = corners[ci];
                    verts.AddRange(new[] { p.X, p.Y, p.Z, n.X, n.Y, n.Z, color.X, color.Y, color.Z, color.W });
                }
                uint b = base_ + (uint)f * 4;
                indices.AddRange(new[] { b,b+1,b+2, b,b+2,b+3 });
            }
        }

        private static void AppendCylinder(
            List<float> verts, List<uint> indices,
            Vector3 center, float radius, float width, int segments, Vector4 color)
        {
            uint base_ = (uint)(verts.Count / 10);
            float hw = width * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)(i     * 2.0 * Math.PI / segments);
                float a1 = (float)((i+1) * 2.0 * Math.PI / segments);

                float y0 = MathF.Cos(a0) * radius, z0 = MathF.Sin(a0) * radius;
                float y1 = MathF.Cos(a1) * radius, z1 = MathF.Sin(a1) * radius;
                var n0 = new Vector3(0f, MathF.Cos(a0), MathF.Sin(a0));
                var n1 = new Vector3(0f, MathF.Cos(a1), MathF.Sin(a1));

                // Two triangle-strip quads (one per side)
                uint b = base_ + (uint)i * 4;
                void Vert(Vector3 p, Vector3 n) =>
                    verts.AddRange(new[] { p.X+center.X, p.Y+center.Y, p.Z+center.Z,
                                          n.X, n.Y, n.Z, color.X, color.Y, color.Z, color.W });

                Vert(new(-hw, y0, z0), n0);
                Vert(new( hw, y0, z0), n0);
                Vert(new( hw, y1, z1), n1);
                Vert(new(-hw, y1, z1), n1);
                indices.AddRange(new[] { b, b+1, b+2, b, b+2, b+3 });
            }
        }

        // Player colour palette (index by player slot)
        public static readonly Vector4[] PlayerColors =
        {
            new(0.9f, 0.2f, 0.2f, 1f), // red
            new(0.2f, 0.6f, 0.9f, 1f), // blue
            new(0.2f, 0.8f, 0.3f, 1f), // green
            new(0.9f, 0.8f, 0.1f, 1f), // yellow
            new(0.8f, 0.3f, 0.9f, 1f), // purple
            new(0.9f, 0.5f, 0.1f, 1f), // orange
            new(0.1f, 0.8f, 0.8f, 1f), // cyan
            new(0.9f, 0.4f, 0.7f, 1f), // pink
        };

        public static Vector4 ColorForPlayer(int slot) =>
            PlayerColors[slot % PlayerColors.Length];
    }
}
