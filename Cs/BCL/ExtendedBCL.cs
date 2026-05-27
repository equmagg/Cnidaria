namespace System.Numerics
{
    public struct Quaternion : IEquatable<Quaternion>
    {
        /// <summary>The X value of the vector component of the quaternion.</summary>
        public float X;

        /// <summary>The Y value of the vector component of the quaternion.</summary>
        public float Y;

        /// <summary>The Z value of the vector component of the quaternion.</summary>
        public float Z;

        /// <summary>The rotation component of the quaternion.</summary>
        public float W;

        internal const int Count = 4;

        public Quaternion(float x, float y, float z, float w)
        {
            X = x; Y= y; Z = z; W = w;
        }
        public Quaternion(Vector3 vectorPart, float scalarPart)
        {
            this = Create(vectorPart, scalarPart);
        }

        public static Quaternion Zero
        {
            get => default;
        }
        public static Quaternion Identity
        {
            get => Create(0.0f, 0.0f, 0.0f, 1.0f);
        }

        public static Quaternion Create(float x, float y, float z, float w) => new Quaternion(x, y, z, w);
        public static Quaternion Create(Vector3 vectorPart, float scalarPart) => new Quaternion(vectorPart.X, vectorPart.Y, vectorPart.Z, scalarPart);

        public static Quaternion CreateFromRotationMatrix(Matrix4x4 matrix)
        {
            float trace = matrix.M11 + matrix.M22 + matrix.M33;

            Quaternion q = default;

            if (trace > 0.0f)
            {
                float s = float.Sqrt(trace + 1.0f);
                q.W = s * 0.5f;
                s = 0.5f / s;
                q.X = (matrix.M23 - matrix.M32) * s;
                q.Y = (matrix.M31 - matrix.M13) * s;
                q.Z = (matrix.M12 - matrix.M21) * s;
            }
            else
            {
                if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
                {
                    float s = float.Sqrt(1.0f + matrix.M11 - matrix.M22 - matrix.M33);
                    float invS = 0.5f / s;
                    q.X = 0.5f * s;
                    q.Y = (matrix.M12 + matrix.M21) * invS;
                    q.Z = (matrix.M13 + matrix.M31) * invS;
                    q.W = (matrix.M23 - matrix.M32) * invS;
                }
                else if (matrix.M22 > matrix.M33)
                {
                    float s = float.Sqrt(1.0f + matrix.M22 - matrix.M11 - matrix.M33);
                    float invS = 0.5f / s;
                    q.X = (matrix.M21 + matrix.M12) * invS;
                    q.Y = 0.5f * s;
                    q.Z = (matrix.M32 + matrix.M23) * invS;
                    q.W = (matrix.M31 - matrix.M13) * invS;
                }
                else
                {
                    float s = float.Sqrt(1.0f + matrix.M33 - matrix.M11 - matrix.M22);
                    float invS = 0.5f / s;
                    q.X = (matrix.M31 + matrix.M13) * invS;
                    q.Y = (matrix.M32 + matrix.M23) * invS;
                    q.Z = 0.5f * s;
                    q.W = (matrix.M12 - matrix.M21) * invS;
                }
            }

            return q;
        }
        public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        {
            (Vector3 sin, Vector3 cos) = Vector3.SinCos((new Vector3(roll, pitch, yaw)) * 0.5f);

            (float sr, float cr) = (sin.X, cos.X);
            (float sp, float cp) = (sin.Y, cos.Y);
            (float sy, float cy) = (sin.Z, cos.Z);

            Quaternion result;

            result.X = cy * sp * cr + sy * cp * sr;
            result.Y = sy * cp * cr - cy * sp * sr;
            result.Z = cy * cp * sr - sy * sp * cr;
            result.W = cy * cp * cr + sy * sp * sr;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Quaternion quaternion1, Quaternion quaternion2)
            => quaternion1.X * quaternion2.X +
               quaternion1.Y * quaternion2.Y +
               quaternion1.Z * quaternion2.Z +
               quaternion1.W * quaternion2.W;

        public static bool operator ==(Quaternion value1, Quaternion value2) 
            => value1.X == value2.X && value1.Y == value2.Y && value1.Z == value2.Z && value1.W == value2.W;
        public static bool operator !=(Quaternion value1, Quaternion value2) => !(value1 == value2);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public override readonly bool Equals([NotNullWhen(true)] object? obj) => (obj is Quaternion other) && Equals(other);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Quaternion other) => this == other;
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override readonly string ToString() => $"{{X:{X} Y:{Y} Z:{Z} W:{W}}}";
    }
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
namespace System.Linq
{
    public static class Enumerable
    {
        private abstract class Iterator<TSource> : IEnumerable<TSource>, IEnumerable, IEnumerator<TSource>, IEnumerator, IDisposable
        {
            private protected int _state;

            private protected TSource _current;

            public TSource Current => _current;

            object IEnumerator.Current => Current;

            private protected abstract Iterator<TSource> Clone();

            public virtual void Dispose()
            {
                _current = default(TSource);
                _state = -1;
            }

            public Iterator<TSource> GetEnumerator()
            {
                Iterator<TSource> obj = ((_state == 0) ? this : Clone());
                obj._state = 1;
                return obj;
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }

            public abstract bool MoveNext();

            IEnumerator<TSource> IEnumerable<TSource>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public abstract TSource[] ToArray();

            public abstract List<TSource> ToList();

            public abstract int GetCount(bool onlyIfCheap);
        }
    }
}