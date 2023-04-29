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

        private readonly PrefabSelector<PrefabActivator<T>>? activator =
            (typeof(T).GetInterfaces().Any(i => i.Name.Contains(nameof(IImplementsInherit)))
            && !typeof(T).GetInterfaces().Any(i => i.Name.Contains(nameof(IImplementsActivator)))
            && !typeof(T).GetInterfaces().Any(i => i.Name.Contains(nameof(IImplementsVariants<T>))))
            ? new PrefabSelector<PrefabActivator<T>>() : null;

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

        public bool have_activator => activator != null;

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

        public PrefabActivator<T>? GetCurrentActivator() => activator?.ActivePrefab;

        public PrefabActivator<T>? GetPackageActivator(string package_id) => activator?.GetPrefabByPackage(package_id);

        public T? GetPrefabByPackage(string package_id) {
            if (have_activator) {
                throw new InvalidOperationException("Use GetPackageActivator for types with activators and specific package");
            }
            foreach (T it in this)
            {
                if (it.ContentPackage!.StringMatches(package_id))
                {
                    return it;
                }
            }
            return null;
        }

        public PrefabActivator<T>? GetPreviousActivator(string package_name)
        {
            if (activator is null) { 
                throw new InvalidOperationException($"GetPreviousActivator does not apply to {typeof(T).FullName}!");
            }
            return activator.GetPrevious(package_name);
        }



        public void Add(T prefab, bool isOverride)
        {
            if (activator != null) {
                throw new InvalidOperationException("Use AddDefered for IImplementsInherit types!");
            }
            using (new WriteLock(rwl)) { AddInternal(prefab, isOverride); }
        }


        public void AddDefered(ContentFile file, ContentXElement element, 
            Func<ContentXElement, T> constructorLambda,  Func<PrefabActivator<T>, PrefabActivator<T>?> locator,
            VariantExtensions.VariantXMLChecker? inherit_callback, Action<T>? OnAdd, bool isOverride)
        {
            activator!.Add(new PrefabActivator<T>(file, element, constructorLambda, locator,  inherit_callback, OnAdd), isOverride);
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
            if (activator != null && callback != null)
            {
                throw new InvalidOperationException("RemoveByFile shouldn't use callback of type Action<T> when defered.");
            }
            else if (activator != null) {
                using (new WriteLock(rwl))
                {
                    activator.RemoveByFile(file);
                    // already disposed. Active prefab already resolves to something else.
                    {
                        for (int i = overrides.Count - 1; i >= 0; i--)
                        {
                            var prefab = overrides[i];
                            if (prefab.ContentFile == file)
                            {
                                overrides.Remove(prefab);
                            }
                        }

                        if (basePrefabInternal is { ContentFile: var baseFile } p && baseFile == file)
                        {
                            basePrefabInternal = null;
                        }
                    }
                }
            }
            else
            {
                var removed = new List<T>();
                using (new WriteLock(rwl))
                {
                    for (int i = overrides.Count - 1; i >= 0; i--)
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
        }


        public void Sort(bool force_resolve = false)
        {
            using (new WriteLock(rwl)) { SortInternal(force_resolve); }
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

        // will recursive lock when onAdd is not null and have callback in activate...
        public T? activePrefabInternal { 
            get {
                if (activator is null)
                {
                    return overrides.Count > 0 ? overrides.First() : basePrefabInternal;
                }
                else {
                    activator.Sort(true);
                    T? current = activator.ActivePrefab?.Activate();
                    overrides.Clear();
                    if (current != null) {
                        overrides.Add(current);
                    }
                    return current;
                }
            }
        }

		public T? activePrefabInternal_NoCreate
		{
			get
			{
				if (activator is null)
				{
					return overrides.Count > 0 ? overrides.First() : basePrefabInternal;
				}
				else
				{
					return activator.activePrefabInternal_NoCreate?.Current;
				}
			}
		}

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
            if (activator != null)
            {
				if (activePrefabInternal_NoCreate != prefab)
				{
					throw new InvalidOperationException($"Can't remove concrete prefab that is defered and not current.");
				}
				else
				{
					activator.activePrefabInternal_NoCreate?.InvalidateCache();
					overrides.Remove(prefab);
				}
			}
            else
            {
                if (basePrefabInternal == prefab) { basePrefabInternal = null; }
                else if (overrides.Contains(prefab)) { overrides.Remove(prefab); }
                else { throw new InvalidOperationException($"Can't remove prefab from PrefabSelector ({typeof(T)}, {prefab.Identifier}, {prefab.ContentFile.ContentPackage.Name})"); }
                prefab.Dispose();
            }
            SortInternal();
        }

        private void SortInternal(bool force_resolve = false)
        {
			overrides.Sort((p1, p2) => (p1.ContentPackage?.Index ?? int.MaxValue) - (p2.ContentPackage?.Index ?? int.MaxValue));
			activator?.SortInternal(force_resolve);
			if (force_resolve && activator != null)
			{
				T? current = activator.ActivePrefab?.Activate();
				overrides.Clear();
				if (current != null)
				{
					overrides.Add(current);
				}
			}
		}

		// will recursive lock when onAdd is not null and have callback in activate...
		public bool isEmptyInternal
		{
			get
			{
				if (activator == null)
				{
					return basePrefabInternal is null && overrides.Count == 0;
				}
				else
				{
					return activator.isEmptyInternal;
				}
			}
		}

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