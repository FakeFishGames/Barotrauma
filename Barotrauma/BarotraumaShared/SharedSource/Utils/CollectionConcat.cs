using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class CollectionConcat<T> : ICollection<T>
    {
        protected readonly IEnumerable<T> enumerableA;
        protected readonly IEnumerable<T> enumerableB;

        public CollectionConcat(IEnumerable<T> a, IEnumerable<T> b)
        {
            enumerableA = a; enumerableB = b;
        }

        public int Count => enumerableA.Count()+enumerableB.Count();

        public bool IsReadOnly => true;

        public void Add(T item) => throw new InvalidOperationException();

        public void Clear() => throw new InvalidOperationException();

        public bool Remove(T item) => throw new InvalidOperationException();

        public bool Contains(T item) => enumerableA.Contains(item) || enumerableB.Contains(item);

        public void CopyTo(T[] array, int arrayIndex)
        {
            void performCopy(IEnumerable<T> enumerable)
            {
                if (enumerable is ICollection<T> collection)
                {
                    collection.CopyTo(array, arrayIndex);
                    arrayIndex += collection.Count;
                }
                else
                {
                    foreach (var item in enumerable)
                    {
                        array[arrayIndex] = item;
                        arrayIndex++;
                    }
                }
            }
            
            performCopy(enumerableA);
            performCopy(enumerableB);
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (T item in enumerableA) { yield return item; }
            foreach (T item in enumerableB) { yield return item; }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class ListConcat<T> : CollectionConcat<T>, IList<T>, IReadOnlyList<T>
    {
        public ListConcat(IEnumerable<T> a, IEnumerable<T> b) : base(a, b) { }

        public int IndexOf(T item)
        {
            int aCount = 0;
            if (enumerableA is IList<T> listA)
            {
                int index = listA.IndexOf(item);
                if (index >= 0) { return index; }
                aCount = listA.Count;
            }
            else
            {
                foreach (var a in enumerableA)
                {
                    if (object.Equals(item, a)) { return aCount; }
                    aCount++;
                }
            }
            
            if (enumerableB is IList<T> listB)
            {
                int index = listB.IndexOf(item);
                if (index >= 0) { return index + aCount; }
            }
            else
            {
                foreach (var b in enumerableB)
                {
                    if (object.Equals(item, b)) { return aCount; }
                    aCount++;
                }
            }

            return -1;
        }

        public void Insert(int index, T item)
        {
            throw new InvalidOperationException();
        }

        public void RemoveAt(int index)
        {
            throw new InvalidOperationException();
        }

        public T this[int index]
        {
            get
            {
                int aCount = enumerableA.Count();
                return index < aCount ? enumerableA.ElementAt(index) : enumerableB.ElementAt(index - aCount);
            }
            set
            {
                throw new InvalidOperationException();
            }
        }
    }
}