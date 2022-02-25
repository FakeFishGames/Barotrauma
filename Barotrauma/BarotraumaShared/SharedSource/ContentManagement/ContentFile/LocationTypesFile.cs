using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class LocationTypesFile : GenericPrefabFile<LocationType>
    {
        public LocationTypesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "locationtypes";
        protected override PrefabCollection<LocationType> prefabs => LocationType.Prefabs;
        protected override LocationType CreatePrefab(ContentXElement element)
        {
            return new LocationType(element, this);
        }
    }
}
