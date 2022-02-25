using System.Xml.Linq;

namespace Barotrauma
{
    sealed class EventManagerSettingsFile : GenericPrefabFile<EventManagerSettings>
    {
        public EventManagerSettingsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "EventManagerSettings";
        protected override PrefabCollection<EventManagerSettings> prefabs => EventManagerSettings.Prefabs;
        protected override EventManagerSettings CreatePrefab(ContentXElement element)
        {
            return new EventManagerSettings(element, this);
        }
    }
}