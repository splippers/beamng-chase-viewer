// Thin aliases — pure System.Numerics, no engine dependency
global using Vector2    = System.Numerics.Vector2;
global using Vector3    = System.Numerics.Vector3;
global using Vector4    = System.Numerics.Vector4;
global using Quaternion = System.Numerics.Quaternion;
global using Matrix4x4  = System.Numerics.Matrix4x4;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace BeamQuest.Core
{
    public static class MathEx
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => Quaternion.Slerp(a, b, t);

        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var m = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot);
            m.Translation = pos;
            return m;
        }

        // BeamNG uses a right-handed Y-up coordinate system; OpenXR also Y-up.
        // No axis swap needed — just pass through.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 BeamToWorld(float x, float y, float z) => new(x, y, z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion BeamToWorld(float x, float y, float z, float w) => new(x, y, z, w);

        public static readonly Vector3 Up      = Vector3.UnitY;
        public static readonly Vector3 Forward = -Vector3.UnitZ;
        public static readonly Vector3 Right   = Vector3.UnitX;
    }

    public readonly record struct Transform3D(Vector3 Position, Quaternion Rotation, Vector3 Scale)
    {
        public static readonly Transform3D Identity = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
        public Matrix4x4 ToMatrix() => MathEx.TRS(Position, Rotation, Scale);
    }
}
