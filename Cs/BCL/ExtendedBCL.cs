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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            _version++;
            T[] array = _items;
            int size = _size;
            if ((uint)size < (uint)array.Length)
            {
                _size = size + 1;
                array[size] = item;
            }
            else
            {
                AddWithResize(item);
            }
        }

        // Non-inline from List.Add to improve its code quality as uncommon path
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            int size = _size;
            Grow(size + 1);
            _size = size + 1;
            _items[size] = item;
        }

        internal void Grow(int capacity)
        {
            Capacity = GetNewCapacity(capacity);
        }
        internal void GrowForInsertion(int indexToInsert, int insertionCount = 1)
        {
            int requiredCapacity = checked(_size + insertionCount);
            int newCapacity = GetNewCapacity(requiredCapacity);

            // Inline and adapt logic from set_Capacity

            T[] newItems = new T[newCapacity];
            if (indexToInsert != 0)
            {
                Array.Copy(_items, newItems, indexToInsert);
            }

            if (_size != indexToInsert)
            {
                Array.Copy(_items, indexToInsert, newItems, indexToInsert + insertionCount, _size - indexToInsert);
            }

            _items = newItems;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetNewCapacity(int capacity)
        {
            int newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;

            if ((uint)newCapacity > Array.MaxLength) newCapacity = Array.MaxLength;

            if (newCapacity < capacity) newCapacity = capacity;

            return newCapacity;
        }
        public void Insert(int index, T item)
        {
            // Note that insertions at the end are legal.
            if ((uint)index > (uint)_size)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (_size == _items.Length)
            {
                GrowForInsertion(index, 1);
            }
            else if (index < _size)
            {
                Array.Copy(_items, index, _items, index + 1, _size - index);
            }
            _items[index] = item;
            _size++;
            _version++;
        }

        public int IndexOf(T item)
            => Array.IndexOf<T>(_items, item, 0, _size);

        public bool Contains(T item)
        {
            return _size != 0 && IndexOf(item) >= 0;
        }

        public void CopyTo(T[] array)
            => CopyTo(array, 0);

        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            if (_size - index < count)
            {
                throw new ArgumentException();
            }

            // Delegate rest of error checking to Array.Copy.
            Array.Copy(_items, index, array, arrayIndex, count);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            // Delegate rest of error checking to Array.Copy.
            Array.Copy(_items, 0, array, arrayIndex, _size);
        }

        // Clears the contents of List.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _version++;
            if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                int size = _size;
                _size = 0;
                if (size > 0)
                {
                    Array.Clear(_items, 0, size); // Clear the elements so that the gc can reclaim the references.
                }
            }
            else
            {
                _size = 0;
            }
        }

        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size)
            {
                throw new ArgumentOutOfRangeException();
            }
            _size--;
            if (index < _size)
            {
                Array.Copy(_items, index + 1, _items, index, _size - index);
            }
            if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _items[_size] = default;
            }
            _version++;
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (_size - index < count)
                throw new ArgumentOutOfRangeException();

            if (count > 0)
            {
                _size -= count;
                if (index < _size)
                {
                    Array.Copy(_items, index + count, _items, index, _size - index);
                }

                _version++;
                if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    Array.Clear(_items, _size, count);
                }
            }
        }

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator +(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X += right.X;
            result.Y += right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X /= right.X;
            result.Y /= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 value1, float value2)
        {
            var result = new Vector2(value1.X, value1.Y);
            result.X /= value2;
            result.Y /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2 left, Vector2 right) => left.X == right.X && left.Y == right.Y;

        public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X *= right.X;
            result.Y *= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 left, float right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X *= right;
            result.Y *= right;
            return result;
        }

        public static Vector2 operator *(float left, Vector2 right)
        {
            var result = new Vector2(right.X, right.Y);
            result.X *= left;
            result.Y *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X -= right.X;
            result.Y -= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 value) => new Vector2(-(value.X), -(value.Y));
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
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
            this.Z *= value;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X += right.X;
            result.Y += right.Y;
            result.Z += right.Z;
            result.W += right.W;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X /= right.X;
            result.Y /= right.Y;
            result.Z /= right.Z;
            result.W /= right.W;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 value1, float value2)
        {
            Vector4 result = new Vector4(value1.X, value1.Y, value1.Z, value1.W);
            value1.X /= value2;
            value1.Y /= value2;
            value1.Z /= value2;
            value1.W /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector4 left, Vector4 right)
            => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

        public static bool operator !=(Vector4 left, Vector4 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            result.W *= right.W;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, float right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X *= right;
            result.Y *= right;
            result.Z *= right;
            result.W *= right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(float left, Vector4 right)
        {
            Vector4 result = new Vector4(right.X, right.Y, right.Z, right.W);
            result.X *= left;
            result.Y *= left;
            result.Z *= left;
            result.W *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
            this.Z *= value;
            this.W *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            result.W -= right.W;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 value) => new Vector4(-(value.X), -(value.Y), -(value.Z), -(value.W));
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