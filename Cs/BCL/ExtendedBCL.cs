namespace System.Numerics
{
    
    public struct Matrix3x2
    {
        private const int RowCount = 3;
        private const int ColumnCount = 2;

        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float M31;
        public float M32;

        public Matrix3x2(float m11, float m12,
                         float m21, float m22,
                         float m31, float m32)
        {
            M11 = m11; M12 = m12;
            M21 = m21; M22 = m22;
            M31 = m31; M32 = m32;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 Create(float m11, float m12,
                                       float m21, float m22,
                                       float m31, float m32)
            => new Matrix3x2(m11, m12, m21, m22, m31, m32);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 Create(Vector2 x, Vector2 y, Vector2 z)
            => new Matrix3x2(x.X, x.Y, y.X, y.Y, z.X, z.Y);

        public Vector2 X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M11, M12);
        }
        public Vector2 Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M21, M22);
        }
        public Vector2 Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M31, M32);
        }
    }
    public struct Matrix4x4
    {
        internal const int RowCount = 4;
        internal const int ColumnCount = 4;

        public float M11;
        public float M12;
        public float M13;
        public float M14;

        public float M21;
        public float M22;
        public float M23;
        public float M24;

        public float M31;
        public float M32;
        public float M33;
        public float M34;

        public float M41;
        public float M42;
        public float M43;
        public float M44;

        public Matrix4x4(float m11, float m12, float m13, float m14,
                         float m21, float m22, float m23, float m24,
                         float m31, float m32, float m33, float m34,
                         float m41, float m42, float m43, float m44)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
            M41 = m41; M42 = m42; M43 = m43; M44 = m44;
        }
        public static Matrix4x4 Identity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Create(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 Create(Vector4 x, Vector4 y, Vector4 z, Vector4 w)
            => new Matrix4x4(x.X, x.Y, x.Z, x.W,
                             y.X, y.Y, y.Z, y.W,
                             z.X, z.Y, z.Z, z.W,
                             w.X, w.Y, w.Z, w.W);
        public Vector4 X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M11, M12, M13, M14);
        }
        public Vector4 Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M21, M22, M23, M24);
        }
        public Vector4 Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M31, M32, M33, M34);
        }
        public Vector4 W
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M41, M42, M43, M44);
        }
    }
}