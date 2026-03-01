using System.Runtime.CompilerServices;

namespace System.Collections
{
    public interface IList : ICollection
    {
        object this[int index]
        {
            get;
            set;
        }

        int Add(object value);

        bool Contains(object value);

        void Clear();

        bool IsReadOnly
        { get; }


        bool IsFixedSize
        {
            get;
        }

        void Insert(int index, object value);

        void Remove(object value);
        void RemoveAt(int index);
    }
}
namespace System.Collections.Generic
{
    public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
    {
        T this[int index]
        {
            get;
        }
    }
    public class List<T>
    {
        private const int DefaultCapacity = 4;

        internal T[] _items; // Do not rename
        internal int _size; // Do not rename
        internal int _version; // Do not rename

        private static readonly T[] s_emptyArray = new T[0];

        public List()
        {
            _items = s_emptyArray;
        }
        public List(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException();

            if (capacity == 0)
                _items = s_emptyArray;
            else
                _items = new T[capacity];
        }

        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (value != _items.Length)
                {
                    if (value > 0)
                    {
                        T[] newItems = new T[value];
                        if (_size > 0)
                        {
                            Array.Copy(_items, newItems, _size);
                        }
                        _items = newItems;
                    }
                    else
                    {
                        _items = s_emptyArray;
                    }
                }
            }
        }
        public int Count => _size;
        public int Length => Count; // alias

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException();
                }
                return _items[index];
            }

            set
            {
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException();
                }
                _items[index] = value;
                _version++;
            }
        }

        //public Enumerator GetEnumerator() => new Enumerator(this);
    }
}
namespace System.Numerics
{
    public struct Vector2
    {
        internal const int Alignment = 8;
        internal const int ElementCount = 2;

        public float X;
        public float Y;

        public Vector2(float value)
        {
            X = value; Y = value;
        }

        public Vector2(float x, float y)
        {
            X = x; Y = y;
        }
        public static Vector2 One
        {
            get => new Vector2(1.0f);
        }
        public static Vector2 Zero
        {
            get => new Vector2(0.0f);
        }
        public static Vector2 UnitX
        {
            get => new Vector2(1.0f, 0.0f);
        }
        public static Vector2 UnitY
        {
            get => new Vector2(0.0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}>";
        }
    }
    public struct Vector3
    {
        internal const int Alignment = 8;
        internal const int ElementCount = 3;

        public float X = 0;
        public float Y = 0;
        public float Z = 0;

        public Vector3(float value)
        {
            X = value; Y = value; Z = value;
        }
        public Vector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
        public static Vector3 One
        {
            get => new Vector3(1.0f);
        }
        public static Vector3 Zero
        {
            get => new Vector3(0f);
        }
        public static Vector3 UnitX
        {
            get => new Vector3(1.0f, 0f, 0f);
        }
        public static Vector3 UnitY
        {
            get => new Vector3(0.0f, 1.0f, 0f);
        }
        public static Vector3 UnitZ
        {
            get => new Vector3(0f, 0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}, {Z.ToString()}>";
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X += right.X;
            result.Y += right.Y;
            result.Z += right.Z;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X /= right.X;
            result.Y /= right.Y;
            result.Z /= right.Z;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 value1, float value2)
        {
            Vector3 result = new Vector3(value1.X, value1.Y, value1.Z);
            value1.X /= value2;
            value1.Y /= value2;
            value1.Z /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3 left, Vector3 right)
            => left.X == right.X && left.Y == right.Y && left.Z == right.Z;

        public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 left, float right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X *= right;
            result.Y *= right;
            result.Z *= right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(float left, Vector3 right)
        {
            Vector3 result = new Vector3(right.X, right.Y, right.Z);
            result.X *= left;
            result.Y *= left;
            result.Z *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 value) => new Vector3(-(value.X), -(value.Y), -(value.Z));
    }
    public struct Vector4
    {
        internal const int Alignment = 16;
        internal const int ElementCount = 4;

        public float X;
        public float Y;
        public float Z;
        public float W;


        public Vector4(float value)
        {
            X = value; Y = value; Z = value; W = value;
        }

        public Vector4(Vector2 value, float z, float w)
        {
            X = value.X; Y = value.Y; Z = z; W = w;
        }

        public Vector4(Vector3 value, float w)
        {
            X = value.X; Y = value.Y; Z = value.Z; W = w;
        }

        public Vector4(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
        public static Vector4 One
        {
            get => new Vector4(1.0f);
        }
        public static Vector4 Zero
        {
            get => new Vector4(0.0f);
        }
        public static Vector4 UnitX
        {
            get => new Vector4(1.0f, 0f, 0f, 0f);
        }
        public static Vector4 UnitY
        {
            get => new Vector4(0.0f, 1.0f, 0f, 0f);
        }
        public static Vector4 UnitZ
        {
            get => new Vector4(0f, 0f, 1.0f, 0f);
        }
        public static Vector4 UnitW
        {
            get => new Vector4(0f, 0f, 0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}, {Z.ToString()}, {W.ToString()}>";
        }
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