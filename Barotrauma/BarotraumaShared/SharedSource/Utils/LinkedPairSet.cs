using System;
using System.Collections;
using System.Collections.Generic;

namespace Barotrauma
{
    public class LinkedPairSet<T1, T2> : IEnumerable<(T1, T2)>
    {
        private readonly Dictionary<T1, T2> t1ToT2 = new Dictionary<T1, T2>();
        private readonly Dictionary<T2, T1> t2ToT1 = new Dictionary<T2, T1>();

        public bool Contains(T1 t1)
        {
            return t1ToT2.ContainsKey(t1);
        }

        public bool Contains(T2 t2)
        {
            return t2ToT1.ContainsKey(t2);
        }

        public T2 this[T1 t1]
        {
            get { return t1ToT2[t1]; }
            set
            {
                T2 prevT2 = t1ToT2[t1];
                t2ToT1.Remove(prevT2); t2ToT1.Add(value, t1);
                t1ToT2[t1] = value;
            }
        }

        public T1 this[T2 t2]
        {
            get { return t2ToT1[t2]; }
            set
            {
                T1 prevT1 = t2ToT1[t2];
                t1ToT2.Remove(prevT1); t1ToT2.Add(value, t2);
                t2ToT1[t2] = value;
            }
        }

        public void Add(T1 t1, T2 t2)
        {
            if (Contains(t1)) { throw new ArgumentException($"{GetType().Name} already contains {t1}"); }
            if (Contains(t2)) { throw new ArgumentException($"{GetType().Name} already contains {t2}"); }
            t1ToT2.Add(t1, t2);
            t2ToT1.Add(t2, t1);
        }

        public void Remove(T1 t1)
        {
            T2 t2 = t1ToT2[t1];
            t1ToT2.Remove(t1);
            t2ToT1.Remove(t2);
        }

        public void Remove(T2 t2)
        {
            T1 t1 = t2ToT1[t2];
            t1ToT2.Remove(t1);
            t2ToT1.Remove(t2);
        }

        public IEnumerator<(T1, T2)> GetEnumerator()
        {
            foreach (var t1 in t1ToT2.Keys)
            {
                yield return (t1, t1ToT2[t1]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}