#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class PrefabSelector<T> : IEnumerable<T> where T : notnull, Prefab
    {
        public T? BasePrefab
        {
            get
            {
                lock (overrides) { return basePrefabInternal; }
            }
        }

        public T? ActivePrefab
        {
            get
            {
                lock (overrides) { return activePrefabInternal; }
            }
        }

        public T? GetPrevious(string package_name)
		{
            bool found = false;
			foreach (T prefab in this)
			{
                if(found) {
                    return prefab;
				}
				if (prefab.ContentPackage?.StringMatches(package_name)??false)
				{
                    found = true;
                }
            }
            return null;
		}

		public void Add(T prefab, bool isOverride)
        {
            lock (overrides) { AddInternal(prefab, isOverride); }
        }

        public void RemoveIfContains(T prefab)
        {
            lock (overrides) { RemoveIfContainsInternal(prefab); }
        }

        public void Remove(T prefab)
        {
            lock (overrides) { RemoveInternal(prefab); }
        }

        public void RemoveByFile(ContentFile file, Action<T>? callback = null)
        {
            lock (overrides) { RemoveByFileInternal(file, callback); }
        }

        public void Sort()
        {
            lock (overrides) { SortInternal(); }
        }

        public bool IsEmpty
        {
            get
            {
                lock (overrides) { return isEmptyInternal; }
            }
        }

        public bool Contains(T prefab)
        {
            lock (overrides) { return ContainsInternal(prefab); }
        }
        
        public bool IsOverride(T prefab)
        {
            lock (overrides) { return IsOverrideInternal(prefab); }
        }


        #region Underlying implementations of the public methods, done separately to avoid nested locking
        private T? basePrefabInternal;
        private readonly List<T> overrides = new List<T>();

        private T? activePrefabInternal => overrides.Any() ? overrides.First() : basePrefabInternal;

        private void AddInternal(T prefab, bool isOverride)
        {
            if (isOverride)
            {
                if (overrides.Contains(prefab)) { throw new InvalidOperationException($"Duplicate prefab in PrefabSelector ({typeof(T)}, {prefab.Identifier}, {prefab.ContentFile.ContentPackage.Name})"); }
                overrides.Add(prefab);
            }
            else
            {
                if (BasePrefab != null)
                {
                    string prefabName
                    = prefab is MapEntityPrefab mapEntityPrefab
                        ? $"\"{mapEntityPrefab.OriginalName}\", \"{prefab.Identifier}\""
                        : $"\"{prefab.Identifier}\"";
                    throw new InvalidOperationException(
                        $"Failed to add the prefab {prefabName} ({prefab.GetType()}) from \"{prefab.ContentPackage?.Name ?? "[NULL]"}\" ({prefab.ContentPackage?.Dir ?? ""}): "
                        + $"a prefab with the same identifier from \"{ActivePrefab!.ContentPackage?.Name ?? "[NULL]"}\" ({ActivePrefab!.ContentPackage?.Dir ?? ""}) already exists; try overriding");
                }
                basePrefabInternal = prefab;
            }
            SortInternal();
        }

        private void RemoveIfContainsInternal(T prefab)
        {
            if (!ContainsInternal(prefab)) { return; }
            RemoveInternal(prefab);
        }

        private void RemoveInternal(T prefab)
        {
            if (basePrefabInternal == prefab) { basePrefabInternal = null; }
            else if (overrides.Contains(prefab)) { overrides.Remove(prefab); }
            else { throw new InvalidOperationException($"Can't remove prefab from PrefabSelector ({typeof(T)}, {prefab.Identifier}, {prefab.ContentFile.ContentPackage.Name})"); }
            prefab.Dispose();
            SortInternal();
        }

        private void RemoveByFileInternal(ContentFile file, Action<T>? callback)
        {
            for (int i = overrides.Count-1; i >= 0; i--)
            {
                var prefab = overrides[i];
                if (prefab.ContentFile == file)
                {
                    RemoveInternal(prefab);
                    callback?.Invoke(prefab);
                }
            }

            if (basePrefabInternal is { ContentFile: var baseFile } p && baseFile == file)
            {
                RemoveInternal(basePrefabInternal);
                callback?.Invoke(p);
            }
        }

        private void SortInternal()
        {
            overrides.Sort((p1, p2) => (p1.ContentPackage?.Index ?? int.MaxValue) - (p2.ContentPackage?.Index ?? int.MaxValue));
        }

        private bool isEmptyInternal => basePrefabInternal is null && !overrides.Any();

        private bool ContainsInternal(T prefab) => basePrefabInternal == prefab || overrides.Contains(prefab);

        private int IndexOfInternal(T prefab) => basePrefabInternal == prefab
            ? overrides.Count
            : overrides.IndexOf(prefab);

        private bool IsOverrideInternal(T prefab) => IndexOfInternal(prefab) > 0;
        #endregion
        
        public IEnumerator<T> GetEnumerator()
        {
            T? basePrefab;
            ImmutableArray<T> overrideClone;
            lock (overrides)
            {
                basePrefab = basePrefabInternal;
                overrideClone = overrides.ToImmutableArray();
            }
            // should be in reverse load order...
            foreach (T prefab in overrideClone)
            {
                yield return prefab;
            }
            if (basePrefab != null) { yield return basePrefab; }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}