using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class NPCSetsFile : GenericPrefabFile<NPCSet>
    {
        public NPCSetsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "npcset";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "npcsets";
        protected override PrefabCollection<NPCSet> Prefabs => NPCSet.Sets;
        protected override NPCSet CreatePrefab(ContentXElement element)
        {
            return new NPCSet(element, this);
        }
    }
}
