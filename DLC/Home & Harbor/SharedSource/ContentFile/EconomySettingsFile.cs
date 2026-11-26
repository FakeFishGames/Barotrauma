using Barotrauma;

namespace HomeAndHarbor
{
    public sealed class EconomySettingsFile(ContentPackage contentPackage, ContentPath path)
        : GenericPrefabFile<EconomySettingsPrefab>(contentPackage, path)
    {
        protected override bool MatchesSingular(Identifier identifier)
            => !MatchesPlural(identifier);

        protected override bool MatchesPlural(Identifier identifier)
            => identifier == "EconomySettings";

        protected override EconomySettingsPrefab CreatePrefab(ContentXElement element)
            => new EconomySettingsPrefab(this, element);

        protected override PrefabCollection<EconomySettingsPrefab> Prefabs
            => EconomySettingsPrefab.Prefabs;
    }
}