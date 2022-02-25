using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage, AlternativeContentTypeNames("MapCreature")]
    sealed class BallastFloraFile : GenericPrefabFile<BallastFloraPrefab>
    {
        public BallastFloraFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "ballastflorabehavior";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "ballastflorabehaviors";
        protected override PrefabCollection<BallastFloraPrefab> prefabs => BallastFloraPrefab.Prefabs;

        protected override BallastFloraPrefab CreatePrefab(ContentXElement element)
        {
            return new BallastFloraPrefab(element, this);
        }
    }
}