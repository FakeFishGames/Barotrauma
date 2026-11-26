using Barotrauma;

namespace HomeAndHarbor
{
    /// <summary>
    /// Extra info for outpost swappable items
    /// </summary>
    public class OutpostSwappableInfoPrefab : Prefab
    {
        public static readonly PrefabCollection<OutpostSwappableInfoPrefab> Prefabs = new();

        public Identifier SwappableItem => Identifier;
        public readonly Identifier RequiredTechnology;

        public OutpostSwappableInfoPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier(nameof(SwappableItem), Identifier.Empty))
        {
            RequiredTechnology = element.GetAttributeIdentifier(nameof(RequiredTechnology), Identifier.Empty);
        }

        public override void Dispose() { }
    }
}