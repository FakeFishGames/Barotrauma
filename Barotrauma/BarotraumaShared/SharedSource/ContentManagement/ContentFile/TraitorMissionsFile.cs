using System.Xml.Linq;

#warning TODO: This file is just about the only thing that's actually somewhat okay about the current traitor system. Gut the whole thing.

#if CLIENT
using PrefabType = Barotrauma.TraitorMissionPrefab;
#elif SERVER
using PrefabType = Barotrauma.TraitorMissionPrefab.TraitorMissionEntry;
#endif

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class TraitorMissionsFile : GenericPrefabFile<PrefabType>
    {
        public TraitorMissionsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "TraitorMission";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "TraitorMissions";
        protected override PrefabCollection<PrefabType> Prefabs => PrefabType.Prefabs;
        protected override PrefabType CreatePrefab(ContentXElement element)
        {
            return new PrefabType(element, this);
        }
    }
}
