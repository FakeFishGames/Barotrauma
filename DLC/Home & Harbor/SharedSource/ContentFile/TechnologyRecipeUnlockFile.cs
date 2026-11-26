using Barotrauma;

namespace HomeAndHarbor
{
    public sealed class TechnologyRecipeUnlockFile(ContentPackage contentPackage, ContentPath path) : GenericPrefabFile<ResearchRecipeUnlockPrefab>(contentPackage, path)
    {
        protected override bool MatchesSingular(Identifier identifier)
            => !MatchesPlural(identifier);

        protected override bool MatchesPlural(Identifier identifier)
            => identifier == "TechnologyRecipeUnlocks";

        protected override PrefabCollection<ResearchRecipeUnlockPrefab> Prefabs
            => ResearchRecipeUnlockPrefab.Prefabs;

        protected override ResearchRecipeUnlockPrefab CreatePrefab(ContentXElement element)
            => new ResearchRecipeUnlockPrefab(this, element);
    }
}