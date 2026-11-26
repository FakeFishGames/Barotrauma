using Barotrauma;

namespace HomeAndHarbor
{
    public sealed class TechnologyTreeFile(ContentPackage contentPackage, ContentPath path) : GenericPrefabFile<TechnologyTreePrefab>(contentPackage, path)
    {
        protected override bool MatchesSingular(Identifier identifier)
            => !MatchesPlural(identifier);

        protected override bool MatchesPlural(Identifier identifier)
            => identifier == "TechnologyTrees";

        protected override PrefabCollection<TechnologyTreePrefab> Prefabs
            => TechnologyTreePrefab.Prefabs;

        protected override TechnologyTreePrefab CreatePrefab(ContentXElement element)
            => new TechnologyTreePrefab(this, element);
    }
}