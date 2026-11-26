using Barotrauma;

namespace HomeAndHarbor
{
    public sealed class TechnologyFile(ContentPackage contentPackage, ContentPath path) : GenericPrefabFile<TechnologyPrefab>(contentPackage, path)
    {
        protected override bool MatchesSingular(Identifier identifier)
            => identifier == "Technology";

        protected override bool MatchesPlural(Identifier identifier)
            => identifier == "Technologies";

        protected override PrefabCollection<TechnologyPrefab> Prefabs
            => TechnologyPrefab.Prefabs;

        protected override TechnologyPrefab CreatePrefab(ContentXElement element)
            => new TechnologyPrefab(this, element);
    }
}