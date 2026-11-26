#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    public class ItemExtensionPrefab : Prefab
    {
        public static readonly PrefabCollection<ItemExtensionPrefab> Prefabs = [];

        public readonly ImmutableArray<ContentXElement> AdditionalComponents = [];

        /// <summary>
        /// Identifiers of the items this extension is applied to.
        /// </summary>
        public readonly ImmutableHashSet<Identifier> TargetItems;

        /// <summary>
        /// The extension is applied to all items with any of these tags.
        /// </summary>
        public readonly ImmutableHashSet<Identifier> Tags;

        public ItemExtensionPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            AdditionalComponents = element.Elements().ToImmutableArray();

            TargetItems = element.GetAttribute("items") != null
                ? element.GetAttributeIdentifierImmutableHashSet("items", ImmutableHashSet<Identifier>.Empty)
                // Fallback to using the identifier of the item extension
                : ImmutableHashSet.Create(Identifier);

            Tags = element.GetAttributeIdentifierImmutableHashSet("tags", ImmutableHashSet<Identifier>.Empty);
        }

        public static IEnumerable<ContentXElement> GetComponentElements(Identifier itemIdentifier, IEnumerable<Identifier> orderedItemTags)
        {
            HashSet<ItemExtensionPrefab>? appliedExtensions = null;

            foreach (ItemExtensionPrefab prefab in Prefabs)
            {
                if (!prefab.TargetItems.Contains(itemIdentifier)) 
                { 
                    continue; 
                }

                appliedExtensions ??= new HashSet<ItemExtensionPrefab>();
                appliedExtensions.Add(prefab);

                foreach (ContentXElement element in prefab.AdditionalComponents) 
                { 
                    yield return element; 
                }
            }

            foreach (Identifier tag in orderedItemTags)
            {
                foreach (ItemExtensionPrefab prefab in Prefabs)
                {
                    if (!prefab.Tags.Contains(tag)) 
                    { 
                        continue; 
                    }

                    appliedExtensions ??= new HashSet<ItemExtensionPrefab>();

                    // Don't apply the same extension twice if the item matches it in multiple ways (identifier and tag, or multiple tags)
                    if (!appliedExtensions.Add(prefab)) 
                    { 
                        continue; 
                    }

                    foreach (ContentXElement element in prefab.AdditionalComponents) 
                    { 
                        yield return element; 
                    }
                }
            }
        }

        public override void Dispose() { }
    }
}
