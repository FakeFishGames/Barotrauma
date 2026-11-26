using Barotrauma;

namespace HomeAndHarbor
{
    public class OutpostSwappableInfoFile : GenericPrefabFile<OutpostSwappableInfoPrefab>
    {
        public OutpostSwappableInfoFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "OutpostSwappableInfo";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "OutpostSwappableInfos";
        protected override OutpostSwappableInfoPrefab CreatePrefab(ContentXElement element) => new(this, element);
        protected override PrefabCollection<OutpostSwappableInfoPrefab> Prefabs => OutpostSwappableInfoPrefab.Prefabs;
    }
}