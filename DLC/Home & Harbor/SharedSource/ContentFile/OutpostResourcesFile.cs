using Barotrauma;

namespace HomeAndHarbor
{
    public sealed class OutpostResourcesFile(ContentPackage contentPackage, ContentPath path) : GenericPrefabFile<ResourcePrefab>(contentPackage, path)
    {
        protected override bool MatchesSingular(Identifier identifier)
            => !MatchesPlural(identifier);

        protected override bool MatchesPlural(Identifier identifier)
            => identifier == "Resources";

        protected override PrefabCollection<ResourcePrefab> Prefabs
            => ResourcePrefab.Prefabs;

        protected override ResourcePrefab CreatePrefab(ContentXElement element)
            => new ResourcePrefab(this, element);
    }
}