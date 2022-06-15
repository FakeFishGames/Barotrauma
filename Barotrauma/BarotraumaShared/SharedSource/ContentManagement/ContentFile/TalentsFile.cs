using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class TalentsFile : GenericPrefabFile<TalentPrefab>
    {
        public TalentsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "talent";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "talents";
        protected override PrefabCollection<TalentPrefab> Prefabs => TalentPrefab.TalentPrefabs;
        protected override TalentPrefab CreatePrefab(ContentXElement element)
        {
            return new TalentPrefab(element, this);
        }
    }
}