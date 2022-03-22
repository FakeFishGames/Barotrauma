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
        /// Dictionary containing all prefabs of the same type.
        /// Key is the identifier.
        /// Value is a list of prefabs that share the same identifier,
        /// where the first element is the "base" prefab,
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
        /// Returns the active prefab with the identifier.
        /// </summary>
        /// <param name="identifier">Prefab identifier</param>
        /// <returns>Active prefab with the identifier</returns>
        public T this[string identifier]
        {
            get { return prefabs[identifier].Last(); }
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
        /// Returns true if a prefab with the identifier exists, false otherwise.
        /// </summary>
        /// <param name="identifier">Prefab identifier</param>
        /// <returns>Whether a prefab with the identifier exists or not</returns>
        public bool ContainsKey(string identifier)
        {
            return prefabs.ContainsKey(identifier);
        }

        /// <summary>
        /// Returns true if a prefab with the identifier exists, false otherwise.
        /// </summary>
        /// <param name="identifier">Prefab identifier</param>
        /// <param name="prefab">The matching prefab (if one is found)</param>
        /// <returns>Whether a prefab with the identifier exists or not</returns>
        public bool TryGetValue(string identifier, out T prefab)
        {
            if (!ContainsKey(identifier))
            {
                prefab = default;
                return false;
            }
            prefab = this[identifier];
            return true;
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

            bool basePrefabExists = prefabs.TryGetValue(prefab.Identifier, out List<T> list);

            //Handle bad overrides and duplicates
            if (basePrefabExists && !isOverride)
            {
                DebugConsole.ThrowError($"Failed to add the prefab \"{prefab.OriginalName}\", \"{prefab.Identifier}\" ({typeof(T)}): a prefab with the same identifier already exists; try overriding\n{Environment.StackTrace}");
                return;
            }

            //Add to list
            if (!basePrefabExists)
            {
                list = new List<T>();
            }

            list.Add(prefab);

            Sort(list);

            if (!basePrefabExists)
            {
                prefabs.Add(prefab.Identifier, list);
            }
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

            var newList = list.Skip(1)
                .OrderByDescending(p => GameMain.Config.EnabledRegularPackages.IndexOf(p.ContentPackage)).ToList();

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
