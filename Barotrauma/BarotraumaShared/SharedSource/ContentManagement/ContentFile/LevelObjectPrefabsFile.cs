using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class LevelObjectPrefabsFile : GenericPrefabFile<LevelObjectPrefab>
    {
        public LevelObjectPrefabsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "levelobjects";
        protected override PrefabCollection<LevelObjectPrefab> Prefabs => LevelObjectPrefab.Prefabs;
        protected override LevelObjectPrefab CreatePrefab(ContentXElement element)
        {
            return new LevelObjectPrefab(element, this);
        }
    }
}
