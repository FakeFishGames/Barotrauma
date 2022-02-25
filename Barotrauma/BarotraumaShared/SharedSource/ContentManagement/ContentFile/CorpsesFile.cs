using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class CorpsesFile : GenericPrefabFile<CorpsePrefab>
    {
        public CorpsesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "corpse";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "corpses";
        protected override PrefabCollection<CorpsePrefab> prefabs => CorpsePrefab.Prefabs;
        protected override CorpsePrefab CreatePrefab(ContentXElement element)
        {
            return new CorpsePrefab(element, this);
        }
    }
}
