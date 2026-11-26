using Barotrauma;

namespace HomeAndHarbor;

class PurchasableOutpostModulesFile : GenericPrefabFile<PurchasableOutpostModulePrefab>
{
    public PurchasableOutpostModulesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

    protected override bool MatchesSingular(Identifier identifier) => identifier == "PurchasableOutpostModule";

    protected override bool MatchesPlural(Identifier identifier) => identifier == "PurchasableOutpostModules";

    protected override PurchasableOutpostModulePrefab CreatePrefab(ContentXElement element) => new(this, element);

    protected override PrefabCollection<PurchasableOutpostModulePrefab> Prefabs => PurchasableOutpostModulePrefab.Prefabs;
}
