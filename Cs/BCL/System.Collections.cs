using System.Runtime.CompilerServices;

namespace System.Collections
{
    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }
    public interface IEnumerator
    {
        bool MoveNext();
        void Reset();
        object Current
        {
            get;
        }
    }

    public interface IDictionaryEnumerator : IEnumerator
    {
        object Key
        {
            get;
        }

        object? Value
        {
            get;
        }

        DictionaryEntry Entry
        {
            get;
        }
    }

    public interface ICollection : IEnumerable
    {
        int Count { get; }
        bool IsSynchronized { get; }
        object SyncRoot { get; }
        void CopyTo(Array array, int index);
    }

    public interface IComparer
    {
        // Compares two objects. An implementation of this method must return a
        // value less than zero if x is less than y, zero if x is equal to y, or a
        // value greater than zero if x is greater than y.
        //
        int Compare(object? x, object? y);
    }

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
    public interface IAlternateEqualityComparer<in TAlternate, T>
        where TAlternate : allows ref struct
        where T : allows ref struct
    {
        bool Equals(TAlternate alternate, T other);

        int GetHashCode(TAlternate alternate);

        T Create(TAlternate alternate);
    }
    public interface IEqualityComparer
    {
        bool Equals(object? x, object? y);
        int GetHashCode(object obj);
    }

    public struct DictionaryEntry
    {
        private object _key; // Do not rename
        private object? _value; // Do not rename

        public DictionaryEntry(object key, object? value)
        {
            _key = key;
            _value = value;
        }

        public object Key
        {
            get => _key;
            set => _key = value;
        }

        public object? Value
        {
            get => _value;
            set => _value = value;
        }

        public void Deconstruct(out object key, out object? value)
        {
            key = Key;
            value = Value;
        }
    }

    internal static class HashHelpers
    {
        public const uint HashCollisionThreshold = 100;

        // This is the maximum prime smaller than Array.MaxLength.
        public const int MaxPrimeArrayLength = 0x7FFFFFC3;

        public const int HashPrime = 101;

        // Table of prime numbers to use as hash table sizes.
        internal static ReadOnlySpan<int> Primes =>
        [
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        ];

        public static bool IsPrime(int candidate)
        {
            if ((candidate & 1) != 0)
            {
                int limit = (int)Math.Sqrt(candidate);
                for (int divisor = 3; divisor <= limit; divisor += 2)
                {
                    if ((candidate % divisor) == 0)
                        return false;
                }
                return true;
            }
            return candidate == 2;
        }

        public static int GetPrime(int min)
        {
            if (min < 0)
                throw new ArgumentException();

            foreach (int prime in Primes)
            {
                if (prime >= min)
                    return prime;
            }

            // Outside of our predefined table. Compute the hard way.
            for (int i = (min | 1); i < int.MaxValue; i += 2)
            {
                if (IsPrime(i) && ((i - 1) % HashPrime != 0))
                    return i;
            }
            return min;
        }
        // Returns size of hashtable to grow to.
        public static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;

            if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
            {
                return MaxPrimeArrayLength;
            }

            return GetPrime(newSize);
        }
        /// <summary>Returns approximate reciprocal of the divisor: ceil(2**64 / divisor).</summary>
        /// <remarks>This should only be used on 64-bit.</remarks>
        public static ulong GetFastModMultiplier(uint divisor) =>
            ulong.MaxValue / divisor + 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint FastMod(uint value, uint divisor, ulong multiplier)
        {
            // This is equivalent of (uint)Math.BigMul(multiplier * value, divisor, out _).
            // This version is faster than BigMul because we only need the high bits.
            uint highbits = (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);

            return highbits;
        }
    }
}
