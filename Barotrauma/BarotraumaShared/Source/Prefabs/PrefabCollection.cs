using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    public class PrefabCollection<T> : IEnumerable<T> where T : class, IPrefab, IDisposable
    {
        private readonly Dictionary<string, List<T>> prefabs = new Dictionary<string, List<T>>();

        public IEnumerable<KeyValuePair<string, List<T>>> AllPrefabs
        {
            get
            {
                foreach (var prefab in prefabs)
                {
                    yield return prefab;
                }
            }
        }

        public T this[string k]
        {
            get { return prefabs[k].Last(); }
        }

        public T Find(Predicate<T> predicate)
        {
            foreach (var kpv in prefabs)
            {
                if (predicate(kpv.Value.Last()))
                {
                    return kpv.Value.Last();
                }
            }
            return null;
        }

        public bool ContainsKey(string k)
        {
            return prefabs.ContainsKey(k);
        }

        public void Add(T prefab, bool isOverride)
        {
            if (string.IsNullOrWhiteSpace(prefab.Identifier))
            {
                DebugConsole.ThrowError($"Prefab \"{prefab.OriginalName}\" has no identifier!");
            }

            List<T> list = null;
            List<T> newList = null;
            if (!prefabs.TryGetValue(prefab.Identifier, out list))
            {
                newList = new List<T>(); newList.Add(null);
                list = newList;
            }

            if (isOverride)
            {
                /*if (list[0] == null)
                {
                    DebugConsole.ThrowError($"Error registering \"{prefab.OriginalName}\", \"{prefab.Identifier}\" ({typeof(T).ToString()}): overriding when base doesn't exist");
                    return;
                }*/
                list.Add(prefab);
            }
            else
            {
                if (list[0] != null)
                {
                    DebugConsole.ThrowError($"Error registering \"{prefab.OriginalName}\", \"{prefab.Identifier}\" ({typeof(T).ToString()}): base already exists; try overriding");
                    return;
                }
                list[0] = prefab;
            }

            Sort(list);

            if (newList != null) { prefabs.Add(prefab.Identifier, newList); }
        }

        public void Remove(T prefab)
        {
            if (!ContainsKey(prefab.Identifier)) { return; }
            if (!prefabs[prefab.Identifier].Contains(prefab)) { return; }
            if (prefabs[prefab.Identifier].IndexOf(prefab)==0)
            {
                prefabs[prefab.Identifier][0] = null;
            }
            else
            {
                prefabs[prefab.Identifier].Remove(prefab);
            }
            prefab.Dispose();

            if (prefabs[prefab.Identifier].Count <= 0 ||
                (prefabs[prefab.Identifier].Count == 1 && prefabs[prefab.Identifier][0] == null))
            {
                prefabs.Remove(prefab.Identifier);
            }
        }

        public void RemoveByFile(string filePath)
        {
            List<T> prefabsToRemove = new List<T>();
            foreach (var kpv in prefabs)
            {
                foreach (var prefab in kpv.Value)
                {
                    if (prefab != null && prefab.FilePath == filePath)
                    {
                        prefabsToRemove.Add(prefab);
                    }
                }
            }

            foreach (var prefab in prefabsToRemove)
            {
                Remove(prefab);
            }
        }

        private void Sort(List<T> prefabs)
        {
            var basePrefab = prefabs[0];
            prefabs.RemoveAt(0);

            prefabs = prefabs.OrderByDescending(p => GameMain.Config.SelectedContentPackages.IndexOf(p.ContentPackage)).ToList();
            prefabs.Insert(0, basePrefab);
        }

        public void SortAll()
        {
            foreach (var kvp in prefabs)
            {
                Sort(kvp.Value);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var kpv in prefabs)
            {
                yield return kpv.Value.Last();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
