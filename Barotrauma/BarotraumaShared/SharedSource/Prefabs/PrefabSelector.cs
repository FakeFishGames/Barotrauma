#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Barotrauma.Threading;

namespace Barotrauma
{
    public class PrefabSelector<T> : IEnumerable<T> where T : notnull, Prefab
    {
        private readonly ReaderWriterLockSlim rwl = new ReaderWriterLockSlim();
        
        public T? BasePrefab
        {
            get
            {
                using (new ReadLock(rwl)) { return basePrefabInternal; }
            }
        }

        public T? ActivePrefab
        {
            get
            {
                using (new ReadLock(rwl)) { return activePrefabInternal; }
            }
        }

        public T? GetPrevious(string package_name)
		{
            bool found = false;
			var it = GetEnumerator();
			while (it.MoveNext())
			{
				if (found)
				{
					return it.Current;
				}
				if (it.Current.ContentPackage?.StringMatches(package_name) ?? false)
				{
					found = true;
				}
			}
			return null;
		}

		public void Add(T prefab, bool isOverride)
        {
            using (new WriteLock(rwl)) { AddInternal(prefab, isOverride); }
        }

        public void RemoveIfContains(T prefab)
        {
            using (new WriteLock(rwl)) { RemoveIfContainsInternal(prefab); }
        }

        public void Remove(T prefab)
        {
            using (new WriteLock(rwl)) { RemoveInternal(prefab); }
        }

        public void RemoveByFile(ContentFile file, Action<T>? callback = null)
        {
            var removed = new List<T>();
            using (new WriteLock(rwl))
            {
                for (int i = overrides.Count-1; i >= 0; i--)
                {
                    var prefab = overrides[i];
                    if (prefab.ContentFile == file)
                    {
                        RemoveInternal(prefab);
                        removed.Add(prefab);
                    }
                }

                if (basePrefabInternal is { ContentFile: var baseFile } p && baseFile == file)
                {
                    RemoveInternal(basePrefabInternal);
                    removed.Add(p);
                }
            }
            if (callback != null) { removed.ForEach(callback); }
        }

        public void Sort()
        {
            using (new WriteLock(rwl)) { SortInternal(); }
        }

        public bool IsEmpty
        {
            get
            {
                using (new ReadLock(rwl)) { return isEmptyInternal; }
            }
        }

        public bool Contains(T prefab)
        {
            using (new ReadLock(rwl)) { return ContainsInternal(prefab); }
        }
        
        public bool IsOverride(T prefab)
        {
            using (new ReadLock(rwl)) { return IsOverrideInternal(prefab); }
        }


        #region Underlying implementations of the public methods, done separately to avoid nested locking
        private T? basePrefabInternal;
        private readonly List<T> overrides = new List<T>();

        private T? activePrefabInternal => overrides.Count > 0 ? overrides.First() : basePrefabInternal;

        private void AddInternal(T prefab, bool isOverride)
        {
            if (isOverride)
            {
                if (overrides.Contains(prefab)) { throw new InvalidOperationException($"Duplicate prefab in PrefabSelector ({typeof(T)}, {prefab.Identifier}, {prefab.ContentFile.ContentPackage.Name})"); }
                overrides.Add(prefab);
            }
            else
            {
                if (basePrefabInternal != null)
                {
                    string prefabName
                    = prefab is MapEntityPrefab mapEntityPrefab
                        ? $"\"{mapEntityPrefab.OriginalName}\", \"{prefab.Identifier}\""
                        : $"\"{prefab.Identifier}\"";
                    throw new InvalidOperationException(
                        $"Failed to add the prefab {prefabName} ({prefab.GetType()}) from \"{prefab.ContentPackage?.Name ?? "[NULL]"}\" ({prefab.ContentPackage?.Dir ?? ""}): "
                        + $"a prefab with the same identifier from \"{activePrefabInternal!.ContentPackage?.Name ?? "[NULL]"}\" ({activePrefabInternal!.ContentPackage?.Dir ?? ""}) already exists; try overriding");
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

        private void SortInternal()
        {
            overrides.Sort((p1, p2) => (p1.ContentPackage?.Index ?? int.MaxValue) - (p2.ContentPackage?.Index ?? int.MaxValue));
        }

        private bool isEmptyInternal => basePrefabInternal is null && overrides.Count == 0;

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
            using (new ReadLock(rwl))
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