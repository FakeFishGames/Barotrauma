using System;

namespace Barotrauma
{
    /// <summary>
    /// Prefab that has a property serves as a deterministic hash of
    /// a prefab's identifier. This member is filled automatically
    /// by PrefabCollection.Add. Required for GetRandom to work on
    /// arbitrary Prefab enumerables, recommended for network synchronization.
    /// </summary>
    public abstract class PrefabWithUintIdentifier : Prefab
    {
        public UInt32 UintIdentifier { get; set; }

        protected PrefabWithUintIdentifier(ContentFile file, Identifier identifier) : base(file, identifier) { }

        protected PrefabWithUintIdentifier(ContentFile file, ContentXElement element) : base(file, element) { }
    }
}