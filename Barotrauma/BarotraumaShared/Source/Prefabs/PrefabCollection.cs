using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    public class PrefabCollection<T> : IEnumerable<T> where T : class, IPrefab, IDisposable
    {
        /// <summary>
        /// Dictionary containing all prefabs of the same type that share the same identifier.
        /// Key is the identifier.
        /// Value is a list where the first element is the "base" prefab,
        /// i.e. the only prefab that's loaded when override tags are not defined.
        /// This first element can be null, if only overrides are defined.
        /// The last element of the list is the prefab that is effectively used
        /// (hereby called "active prefab")
        /// </summary>
        private readonly Dictionary<string, List<T>> prefabs = new Dictionary<string, List<T>>();

        /// <summary>
        /// AllPrefabs exposes all prefabs instead of just the active ones.
        /// </summary>
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

        /// <summary>
        /// Returns the active prefab with identifier k.
        /// </summary>
        /// <param name="k">Prefab identifier</param>
        /// <returns>Active prefab with identifier k</returns>
        public T this[string k]
        {
            get { return prefabs[k].Last(); }
        }

        /// <summary>
        /// Finds the first active prefab that returns true given the predicate,
        /// or null if no such prefab is found.
        /// </summary>
        /// <param name="predicate">Predicate to perform the search with.</param>
        /// <returns></returns>
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

        /// <summary>
        /// Returns true if a prefab with identifier k exists, false otherwise.
        /// </summary>
        /// <param name="k">Prefab identifier</param>
        /// <returns>Whether a prefab with identifier k exists or not</returns>
        public bool ContainsKey(string k)
        {
            return prefabs.ContainsKey(k);
        }

        /// <summary>
        /// Add a prefab to the collection.
        /// If not marked as an override, fail if a prefab with the same
        /// identifier already exists.
        /// Otherwise, add to the corresponding list,
        /// without making any changes to the base prefab.
        /// </summary>
        /// <param name="prefab">Prefab</param>
        /// <param name="isOverride">Is marked as override</param>
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

        /// <summary>
        /// Removes a prefab from the collection.
        /// </summary>
        /// <param name="prefab">Prefab</param>
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

        /// <summary>
        /// Removes all prefabs that were loaded from a certain file.
        /// </summary>
        /// <param name="filePath">File path</param>
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

        /// <summary>
        /// Sorts a list of prefabs based on the content package load order.
        /// </summary>
        /// <param name="list">List of prefabs</param>
        private void Sort(List<T> list)
        {
            if (list.Count <= 1) { return; }

            var newList = list.Skip(1).OrderByDescending(p => GameMain.Config.SelectedContentPackages.IndexOf(p.ContentPackage)).ToList();

            list.RemoveRange(1, list.Count - 1);
            list.AddRange(newList);
        }

        /// <summary>
        /// Sorts all prefabs in the collection based on the content package load order.
        /// </summary>
        public void SortAll()
        {
            foreach (var kvp in prefabs)
            {
                Sort(kvp.Value);
            }
        }

        /// <summary>
        /// GetEnumerator implementation to enable foreach
        /// </summary>
        /// <returns>IEnumerator</returns>
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var kpv in prefabs)
            {
                yield return kpv.Value.Last();
            }
        }

        /// <summary>
        /// GetEnumerator implementation to enable foreach
        /// </summary>
        /// <returns>IEnumerator</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
