#if WINDOWS_PHONE || XBOX

using System.Collections;
using System.Collections.Generic;

namespace FarseerPhysics.Common
{
    public class HashSet<T> : ICollection<T>
    {
        private Dictionary<T, byte> _dict;

        public HashSet(int capacity)
        {
            _dict = new Dictionary<T, byte>(capacity);
        }

        public HashSet()
        {
            _dict = new Dictionary<T, byte>();
        }

        #region ICollection<T> Members

        public void Add(T item)
        {
            // We don't care for the value in dictionary, only keys matter.
            if (!_dict.ContainsKey(item))
                _dict.Add(item, 0);
        }

        public void Clear()
        {
            _dict.Clear();
        }

        public bool Contains(T item)
        {
            return _dict.ContainsKey(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            foreach (var item in _dict.Keys)
            {
                array[arrayIndex++] = item;
            }
        }

        public bool Remove(T item)
        {
            return _dict.Remove(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _dict.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _dict.Keys.GetEnumerator();
        }

        // Properties
        public int Count
        {
            get { return _dict.Keys.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        #endregion
    }
}
#endif