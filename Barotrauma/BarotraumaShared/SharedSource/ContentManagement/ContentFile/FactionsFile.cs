using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class FactionsFile : GenericPrefabFile<FactionPrefab>
    {
        public FactionsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "faction";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "factions";
        protected override PrefabCollection<FactionPrefab> Prefabs => FactionPrefab.Prefabs;
        protected override FactionPrefab CreatePrefab(ContentXElement element)
        {
            return new FactionPrefab(element, this);
        }
    }
}
