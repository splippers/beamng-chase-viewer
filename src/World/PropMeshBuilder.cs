namespace BeamQuest.World
{
    /// <summary>
    /// Builds static prop meshes for the chase arena.
    /// All props use the same vertex format as vehicles: [px,py,pz, nx,ny,nz, r,g,b,a].
    /// </summary>
    public static class PropMeshBuilder
    {
        // ── Concrete barrier (jersey barrier) ────────────────────────────────
        // Trapezoidal cross-section, 1.5m tall, 0.6m base, 1.8m long
        public static (float[] vertices, uint[] indices) Barrier()
            => Box(new Vector3(0f, 0.75f, 0f), new Vector3(0.55f, 1.5f, 1.8f),
                   new Vector4(0.72f, 0.72f, 0.72f, 1f));

        // ── Shipping container ────────────────────────────────────────────────
        // 2.4m×2.6m×6.1m in standard ISO dimensions
        public static (float[] vertices, uint[] indices) ShippingContainer(Vector4 color)
            => Box(new Vector3(0f, 1.3f, 0f), new Vector3(2.4f, 2.6f, 6.1f), color);

        // ── Abandoned car shell (simple box + windscreen slope) ───────────────
        public static (float[] vertices, uint[] indices) AbandonedCar()
        {
            var (bv, bi) = Box(new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 1.1f, 4.2f),
                               new Vector4(0.25f, 0.22f, 0.2f, 1f));
            var (rv, ri) = Box(new Vector3(0f, 1.45f, 0.1f), new Vector3(1.75f, 0.8f, 2.0f),
                               new Vector4(0.18f, 0.16f, 0.15f, 1f));
            return Merge(bv, bi, rv, ri);
        }

        // ── Street light post ─────────────────────────────────────────────────
        // Pole + arm + glowing head
        public static (float[] vertices, uint[] indices) StreetLight(bool on)
        {
            var headColor = on
                ? new Vector4(1f, 0.95f, 0.7f, 1f)   // warm sodium orange
                : new Vector4(0.3f, 0.3f, 0.3f, 1f);

            var (pv, pi) = Box(new Vector3(0f, 3f, 0f),    new Vector3(0.12f, 6f, 0.12f),
                               new Vector4(0.35f, 0.35f, 0.4f, 1f));
            var (av, ai) = Box(new Vector3(0.8f, 6.1f, 0f),new Vector3(1.6f, 0.1f, 0.1f),
                               new Vector4(0.35f, 0.35f, 0.4f, 1f));
            var (hv, hi) = Box(new Vector3(1.6f, 5.85f, 0f),new Vector3(0.45f, 0.3f, 0.5f),
                               headColor);

            var (t, ti) = Merge(pv, pi, av, ai);
            return Merge(t, ti, hv, hi);
        }

        // ── Low wall / concrete block ─────────────────────────────────────────
        public static (float[] vertices, uint[] indices) ConcreteBlock()
            => Box(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 0.5f),
                   new Vector4(0.65f, 0.63f, 0.6f, 1f));

        // ── Shared box builder ────────────────────────────────────────────────
        private static (float[] vertices, uint[] indices) Box(
            Vector3 center, Vector3 size, Vector4 color)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            Vector3[] c =
            {
                center + new Vector3(-hx,-hy,-hz), center + new Vector3( hx,-hy,-hz),
                center + new Vector3( hx, hy,-hz), center + new Vector3(-hx, hy,-hz),
                center + new Vector3(-hx,-hy, hz), center + new Vector3( hx,-hy, hz),
                center + new Vector3( hx, hy, hz), center + new Vector3(-hx, hy, hz),
            };
            Vector3[] normals = { -Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitX,
                                   Vector3.UnitX, -Vector3.UnitY, Vector3.UnitY };
            int[][] faces = { new[]{0,1,2,3}, new[]{5,4,7,6},
                              new[]{4,0,3,7}, new[]{1,5,6,2},
                              new[]{4,5,1,0}, new[]{3,2,6,7} };

            var verts   = new float[6 * 4 * 10];
            var indices = new List<uint>();

            for (int f = 0; f < 6; f++)
            {
                var n = normals[f];
                for (int i = 0; i < 4; i++)
                {
                    int vi = (f * 4 + i) * 10;
                    var p = c[faces[f][i]];
                    verts[vi+0]=p.X; verts[vi+1]=p.Y; verts[vi+2]=p.Z;
                    verts[vi+3]=n.X; verts[vi+4]=n.Y; verts[vi+5]=n.Z;
                    verts[vi+6]=color.X; verts[vi+7]=color.Y; verts[vi+8]=color.Z; verts[vi+9]=color.W;
                }
                uint b = (uint)f * 4;
                indices.AddRange(new[] { b,b+1,b+2, b,b+2,b+3 });
            }

            return (verts, indices.ToArray());
        }

        private static (float[] vertices, uint[] indices) Merge(
            float[] av, uint[] ai, float[] bv, uint[] bi)
        {
            uint offset = (uint)(av.Length / 10);
            var verts   = av.Concat(bv).ToArray();
            var indices = ai.Concat(bi.Select(i => i + offset)).ToArray();
            return (verts, indices);
        }
    }
}
