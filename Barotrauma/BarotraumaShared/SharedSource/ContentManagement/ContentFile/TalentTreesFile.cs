using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class TalentTreesFile : GenericPrefabFile<TalentTree>
    {
        public TalentTreesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "talenttree";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "talenttrees";
        protected override PrefabCollection<TalentTree> Prefabs => TalentTree.JobTalentTrees;
        protected override TalentTree CreatePrefab(ContentXElement element)
        {
            return new TalentTree(element, this);
        }
    }
}