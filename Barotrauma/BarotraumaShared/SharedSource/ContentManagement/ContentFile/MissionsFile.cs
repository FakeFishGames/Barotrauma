using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class MissionsFile : GenericPrefabFile<MissionPrefab>
    {
        /*private readonly static ImmutableHashSet<Type> missionTypes;
        static MissionsFile()
        {
            missionTypes = ReflectionUtils.GetDerivedNonAbstract<Mission>()
                .ToImmutableHashSet();
        }*/

        public MissionsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier)
            => !MatchesPlural(identifier);
        /*missionTypes.Any(t => identifier == t.Name)
            || identifier == "OutpostDestroyMission" || identifier == "OutpostRescueMission";*/
        protected override bool MatchesPlural(Identifier identifier) => identifier == "missions";
        protected override PrefabCollection<MissionPrefab> prefabs => MissionPrefab.Prefabs;
        protected override MissionPrefab CreatePrefab(ContentXElement element)
        {
            return new MissionPrefab(element, this);
        }
    }
}
