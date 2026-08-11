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