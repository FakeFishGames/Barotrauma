using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class StructureFile : GenericPrefabFile<StructurePrefab>
    {
        public StructureFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "prefabs" || identifier == "structures";
        protected override PrefabCollection<StructurePrefab> prefabs => StructurePrefab.Prefabs;
        protected override StructurePrefab CreatePrefab(ContentXElement element)
        {
            return new StructurePrefab(element, this);
        }
    }
}
