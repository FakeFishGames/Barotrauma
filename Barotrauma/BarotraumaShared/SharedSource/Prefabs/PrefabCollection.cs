#nullable enable
using Barotrauma.Extensions;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;

namespace Barotrauma
{
    public class PrefabCollection<T> : IEnumerable<T> where T : notnull, Prefab
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public PrefabCollection()
        {
            var interfaces = typeof(T).GetInterfaces();
            implementsVariants = interfaces.Any(i => i.Name.Contains(nameof(IImplementsVariants<T>)));
        }

        // an instance of prefab that the collection keeps track of.
        // used in ienumerable<T>


        /// <summary>
        /// Constructor with OnAdd and OnRemove callbacks provided.
        /// </summary>
        public PrefabCollection(
            Action<T, bool>? onAdd,
            Action<T>? onRemove,
            Action? onSort,
            Action<ContentFile>? onAddOverrideFile,
            Action<ContentFile>? onRemoveOverrideFile) : this()
        {
            OnAdd = onAdd;
            OnRemove = onRemove;
            OnSort = onSort;
            OnAddOverrideFile = onAddOverrideFile;
            OnRemoveOverrideFile = onRemoveOverrideFile;
        }

        /// <summary>
        /// Constructor with only the OnSort callback provided.
        /// </summary>
        public PrefabCollection(Action? onSort) : this()
        {
            OnSort = onSort;
        }

        /// <summary>
        /// Method to be called when calling Add(T prefab, bool override).
        /// If provided, the method is called only if Add succeeds.
        /// </summary>
        private readonly Action<T, bool>? OnAdd = null;

        /// <summary>
        /// Method to be called when calling Remove(T prefab).
        /// If provided, the method is called before success
        /// or failure can be determined within the body of Remove.
        /// </summary>
        private readonly Action<T>? OnRemove = null;

        /// <summary>
        /// Method to be called when calling SortAll().
        /// </summary>
        private readonly Action? OnSort = null;

        /// <summary>
        /// Method to be called when calling AddOverrideFile(ContentFile file).
        /// </summary>
        private readonly Action<ContentFile>? OnAddOverrideFile = null;

        /// <summary>
        /// Method to be called when calling RemoveOverrideFile(ContentFile file).
        /// </summary>
        private readonly Action<ContentFile>? OnRemoveOverrideFile = null;

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
#if DEBUG && MODBREAKER
        private readonly CursedDictionary<Identifier, PrefabSelector<T>> prefabs = new CursedDictionary<Identifier, PrefabSelector<T>>();
#else
        private readonly ConcurrentDictionary<Identifier, PrefabSelector<T>> prefabs = new ConcurrentDictionary<Identifier, PrefabSelector<T>>();
#endif

        /// <summary>
        /// Collection of content files that override all previous prefabs
        /// i.e. anything set to load before these effectively doesn't exist
        /// </summary>
        private readonly HashSet<ContentFile> overrideFiles = new HashSet<ContentFile>();
        private ContentFile? topMostOverrideFile = null;

        private readonly bool implementsVariants;

        private bool IsPrefabOverriddenByFile(T prefab)
        {
            return topMostOverrideFile != null &&
                    topMostOverrideFile.ContentPackage.Index > prefab.ContentFile.ContentPackage.Index;
        }

        private class InheritanceTreeCollection
        {
            public class Node
            {
                public Node(Identifier identifier, string package /*= ""*/) { Inst = new PrefabInstance(identifier, package); }

                public readonly PrefabInstance Inst;
                public Node? Parent = null;
                public readonly HashSet<Node> Inheritors = new HashSet<Node>();
            }

            private readonly PrefabCollection<T> prefabCollection;

            public InheritanceTreeCollection(PrefabCollection<T> collection) { prefabCollection = collection; }

            public readonly Dictionary<PrefabInstance, Node> IdToNode = new Dictionary<PrefabInstance, Node>();
            public readonly HashSet<Node> RootNodes = new HashSet<Node>();

            public Node? AddNodeAndInheritors(PrefabInstance inst)
            {
                if (!prefabCollection.TryGet(inst, out T? prefab)) { return null; }

                if (prefab is null) return null;

                if (!IdToNode.TryGetValue(inst, out var node))
                {
                    node = new Node(inst.id, inst.package);
                    RootNodes.Add(node);
                    IdToNode.Add(inst, node);
                }
                else
                {
                    //if the node already exists, it already contains
                    //all inheritors so let's just return this immediately
                    return node;
                }
            
                // for overridden stuff to be found by inheritance, one must use all prefabs asscociated with id,
                // not just the active prefab, as implemented by PrefabCollection's enummerator
                prefabCollection.prefabs
                    .SelectMany(p => p.Value)
                    .Cast<IImplementsVariants<T>>()
                    .Where(p => p.InheritParent.id == inst.id)
                     // only the current package is null
                    .Where(p => (p.InheritParent.package == inst.package
                       // variant inheritance across package boundary need reload prefab, so the evaluated one doesn't count here.
                        || (p.originalElement.InheritParent().package.IsNullOrEmpty() && p.GetPrevious(inst.id)?.ContentPackage?.Name == inst.package))
                        && p.CheckInheritHistory(prefab))
                    .Cast<T>()
                    .ForEach(p =>
                    {
                        var c_inst = new PrefabInstance(p.Identifier, p.ContentPackage?.Name ?? "");
                        var inheritorNode = AddNodeAndInheritors(c_inst);
                        if (inheritorNode is null) { return; }
                        RootNodes.Remove(inheritorNode);
                        inheritorNode.Parent = node;
                        node.Inheritors.Add(inheritorNode);
                    });
                return node;
            }

            public void AddNodesAndInheritors(IEnumerable<PrefabInstance> ids)
                => ids.ForEach(id => AddNodeAndInheritors(id));

            public void InvokeCallbacks(bool force_resolve = false)
            {
                HashSet<Node> uncheckedNodes = IdToNode.Values.ToHashSet();

                void invokeCallbacksForNode(Node node)
                {
                    if (!prefabCollection.TryGet(node.Inst, out var p) ||
                        !(p is IImplementsVariants<T> prefab)) { return; }
					if (!prefab.InheritParent.id.IsEmpty)
					{
						T parent;
						if (prefab.InheritParent.package.IsNullOrEmpty())
						{
							parent = prefab.GetPrevious(prefab.InheritParent.id);
						}
						else
						{
							parent = prefab.FindByPrefabInstance(prefab.InheritParent);
						}
						if (!(parent is null))
						{
							prefab.CheckInheritHistory(parent);
							prefab.InheritFrom(parent!);
						}
						else if (force_resolve)
						{
							DebugConsole.LogError($"Cannot resolve inheritance of {(prefab as T)!.Identifier} inheriting {prefab.InheritParent.id}!");
						}
					}
					node.Inheritors.ForEach(invokeCallbacksForNode);
                }
                RootNodes.ForEach(invokeCallbacksForNode);
            }
        }

        private void HandleInheritance(T prefab, bool force_resolve = false)
		{
            var inst = new PrefabInstance(prefab.Identifier, prefab.ContentPackage?.Name ?? "");
            HandleInheritance(inst, force_resolve);
        }

        private void HandleInheritance(PrefabInstance prefabInstance, bool force_resolve = false)
            => HandleInheritance(prefabInstance.ToEnumerable(), force_resolve);

        private void HandleInheritance(IEnumerable<PrefabInstance> prefabInstance, bool force_resolve = false)
        {
            if (!implementsVariants) { return; }
            InheritanceTreeCollection inheritanceTreeCollection = new InheritanceTreeCollection(this);
            inheritanceTreeCollection.AddNodesAndInheritors(prefabInstance);
            inheritanceTreeCollection.InvokeCallbacks(force_resolve);
        }

        /// <summary>
        /// AllPrefabs exposes all prefabs instead of just the active ones.
        /// </summary>
        public IEnumerable<KeyValuePair<Identifier, PrefabSelector<T>>> AllPrefabs
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
        /// Returns the active prefab with the given identifier.
        /// </summary>
        /// <param name="identifier">Prefab identifier</param>
        /// <returns>Active prefab with the given identifier</returns>
        public T this[Identifier identifier]
        {
            get
            {
                Prefab.DisallowCallFromConstructor();
                var prefab = prefabs[identifier].ActivePrefab;
                if (prefab != null && !IsPrefabOverriddenByFile(prefab))
                {
                    return prefab;
                }
                throw new IndexOutOfRangeException($"Prefab of identifier \"{identifier}\" cannot be returned because it was overridden by \"{topMostOverrideFile!.Path}\"");
            }
        }

        public T this[string identifier]
        {
            get
            {
                //this exists because I don't want implicit
                //string to Identifier conversion for the most
                //part, but it's useful and fairly safe to do
                //in this particular instance
                return this[identifier.ToIdentifier()];
            }
        }

        /// <summary>
        /// Returns true if a prefab with the identifier exists, false otherwise.
        /// </summary>
        /// <param name="identifier">Prefab identifier</param>
        /// <param name="result">The matching prefab (if one is found)</param>
        /// <returns>Whether a prefab with the identifier exists or not</returns>
        public bool TryGet(PrefabInstance identifier, [NotNullWhen(true)] out T? result)
        {
            Prefab.DisallowCallFromConstructor();
            if (prefabs.TryGetValue(identifier.id, out PrefabSelector<T>? selector))
            {
                if(identifier.package.IsNullOrEmpty()){
                    result = selector!.ActivePrefab;
                    return true;
                }
                else{
                    foreach (T it in selector)
                    {
                        if (it.ContentPackage!.StringMatches(identifier.package))
                        {
                            result = it;
                            return true;
                        }
                    }
                    result = null;
                    return false;
                }

                return false;
            }
            else
            {
                result = null;
                return false;
            }
        }

        public bool TryGet(string identifier, [NotNullWhen(true)] out T? result)
        {
            return TryGet(new PrefabInstance(identifier.ToIdentifier(), ""), out result);
        }

        public bool TryGet(Identifier identifier, [NotNullWhen(true)] out T? result)
        {
            return TryGet(new PrefabInstance(identifier, ""), out result);
        }


        public IEnumerable<Identifier> Keys => prefabs.Keys;

        /// <summary>
        /// Finds the first active prefab that returns true given the predicate,
        /// or null if no such prefab is found.
        /// </summary>
        /// <param name="predicate">Predicate to perform the search with.</param>
        /// <returns></returns>
        public T? Find(Predicate<T> predicate)
        {
            Prefab.DisallowCallFromConstructor();
            foreach (var kpv in prefabs)
            {
                if (kpv.Value.ActivePrefab is T p && predicate(p))
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns true if a prefab with the given identifier exists, false otherwise.
        /// </summary>
        /// <param name="identifier">Prefab identifier</param>
        /// <returns>Whether a prefab with the given identifier exists or not</returns>
        public bool ContainsKey(Identifier identifier)
        {
            Prefab.DisallowCallFromConstructor();
            return prefabs.ContainsKey(identifier);
        }

        public bool ContainsKey(string k) =>  prefabs.ContainsKey(k.ToIdentifier());

        /// <summary>
        /// Determines whether a prefab is implemented as an override or not.
        /// </summary>
        /// <param name="prefab">Prefab in this collection</param>
        /// <returns>Whether a prefab is implemented as an override or not</returns>
        public bool IsOverride(T prefab)
        {
            Prefab.DisallowCallFromConstructor();
            if (ContainsKey(prefab.Identifier))
            {
                return prefabs[prefab.Identifier].IsOverride(prefab);
            }
            return false;
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
            Prefab.DisallowCallFromConstructor();
            if (prefab.Identifier.IsEmpty)
            {
                throw new ArgumentException($"Prefab has no identifier!");
            }

            bool selectorExists = prefabs.TryGetValue(prefab.Identifier, out PrefabSelector<T>? selector);

            //Add to list
            selector ??= new PrefabSelector<T>();

            if (prefab is PrefabWithUintIdentifier prefabWithUintIdentifier)
            {
                if (!selector.IsEmpty)
                {
                    prefabWithUintIdentifier.UintIdentifier = (selector.ActivePrefab as PrefabWithUintIdentifier)!.UintIdentifier;
                }
                else
                {
                    using (MD5 md5 = MD5.Create())
                    {
                        prefabWithUintIdentifier.UintIdentifier = ToolBox.IdentifierToUint32Hash(prefab.Identifier, md5);

                        //it's theoretically possible for two different values to generate the same hash, but the probability is astronomically small
                        T? findCollision()
                            => Find(p =>
                                p.Identifier != prefab.Identifier
                                && p is PrefabWithUintIdentifier otherPrefab
                                && otherPrefab.UintIdentifier == prefabWithUintIdentifier.UintIdentifier);
                        for (T? collision = findCollision(); collision != null; collision = findCollision())
                        {
                            DebugConsole.ThrowError($"Hashing collision when generating uint identifiers for {typeof(T).Name}: {prefab.Identifier} has the same UintIdentifier as {collision.Identifier} ({prefabWithUintIdentifier.UintIdentifier})");
                            prefabWithUintIdentifier.UintIdentifier++;
                        }
                    }
                }
            }
            selector.Add(prefab, isOverride);

            if (!selectorExists)
            {
                if (!prefabs.TryAdd(prefab.Identifier, selector)) { throw new Exception($"Failed to add selector for \"{prefab.Identifier}\""); }
            }
            OnAdd?.Invoke(prefab, isOverride);
            HandleInheritance(prefab);
        }

        /// <summary>
        /// Removes a prefab from the collection.
        /// </summary>
        /// <param name="prefab">Prefab</param>
        public void Remove(T prefab)
        {
            Prefab.DisallowCallFromConstructor();
            OnRemove?.Invoke(prefab);
            if (!ContainsKey(prefab.Identifier)) { return; }
            if (!prefabs[prefab.Identifier].Contains(prefab)) { return; }
            prefabs[prefab.Identifier].Remove(prefab);

            if (prefabs[prefab.Identifier].IsEmpty)
            {
                prefabs.TryRemove(prefab.Identifier, out _);
            }
            HandleInheritance(prefab);
        }

        /// <summary>
        /// Removes all prefabs that were loaded from a certain file.
        /// </summary>
        public void RemoveByFile(ContentFile file)
        {
            Prefab.DisallowCallFromConstructor();
            HashSet<Identifier> clearedIdentifiers = new HashSet<Identifier>();
            foreach (var kpv in prefabs)
            {
                kpv.Value.RemoveByFile(file, OnRemove);
                if (kpv.Value.IsEmpty) { clearedIdentifiers.Add(kpv.Key); }
            }

            foreach (var identifier in clearedIdentifiers)
            {
                prefabs.TryRemove(identifier, out _);
            }
            RemoveOverrideFile(file);
        }

        /// <summary>
        /// Adds an override file to the collection.
        /// </summary>
        public void AddOverrideFile(ContentFile file)
        {
            Prefab.DisallowCallFromConstructor();
            if (!overrideFiles.Contains(file))
            {
                overrideFiles.Add(file);
            }
            OnAddOverrideFile?.Invoke(file);
        }

        /// <summary>
        /// Removes an override file from the collection.
        /// </summary>
        public void RemoveOverrideFile(ContentFile file)
        {
            Prefab.DisallowCallFromConstructor();
            if (overrideFiles.Contains(file))
            {
                overrideFiles.Remove(file);
            }
            OnRemoveOverrideFile?.Invoke(file);
        }

        /// <summary>
        /// Sorts all prefabs in the collection based on the content package load order.
        /// </summary>
        public void SortAll()
        {
            Prefab.DisallowCallFromConstructor();
            foreach (var kvp in prefabs)
            {
                kvp.Value.Sort();
            }
            topMostOverrideFile = overrideFiles.Any() ? overrideFiles.First(f1 => overrideFiles.All(f2 => f1.ContentPackage.Index >= f2.ContentPackage.Index)) : null;
            OnSort?.Invoke();
			// inheritance cannot just work topmost prefab
			// Mod A have item a, Mod B partially overrides Mod A's a.
			// If you have Mod B, this doesn't necessarily mean Mod B works correctly
			AllPrefabs.SelectMany(p => p.Value).ForEach(p => HandleInheritance(p, true));
            /*
            foreach(T p in this){
                HandleInheritance(p);
			}*/
        }

        /// <summary>
        /// GetEnumerator implementation to enable foreach
        /// </summary>
        /// <returns>IEnumerator</returns>
        public IEnumerator<T> GetEnumerator()
        {
            Prefab.DisallowCallFromConstructor();
            foreach (var kpv in prefabs)
            {
                var prefab = kpv.Value.ActivePrefab;
                if (prefab != null && !IsPrefabOverriddenByFile(prefab))
                {
                    yield return prefab;
                }
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
