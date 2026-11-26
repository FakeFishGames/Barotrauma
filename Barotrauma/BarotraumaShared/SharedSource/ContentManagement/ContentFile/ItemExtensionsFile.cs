namespace Barotrauma
{
    /// <summary>
    /// Content file that allows adding ItemComponents to existing items via XML.
    /// </summary>
    public sealed class ItemExtensionsFile(ContentPackage contentPackage, ContentPath path)
        : GenericPrefabFile<ItemExtensionPrefab>(contentPackage, path)
    {
        protected override bool MatchesSingular(Identifier identifier)
            => !MatchesPlural(identifier);

        protected override bool MatchesPlural(Identifier identifier)
            => identifier == "ItemExtensions";

        protected override ItemExtensionPrefab CreatePrefab(ContentXElement element)
            => new ItemExtensionPrefab(this, element);

        protected override PrefabCollection<ItemExtensionPrefab> Prefabs
            => ItemExtensionPrefab.Prefabs;
    }
}
