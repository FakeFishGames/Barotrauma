using Barotrauma;

namespace HomeAndHarbor
{
    public class OutpostCatalogFile : GenericPrefabFile<OutpostCatalogPrefab>
    {
        public OutpostCatalogFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "OutpostCatalog";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "OutpostCatalogs";
        protected override OutpostCatalogPrefab CreatePrefab(ContentXElement element) => new(this, element);
        protected override PrefabCollection<OutpostCatalogPrefab> Prefabs => OutpostCatalogPrefab.Prefabs;
    }
}