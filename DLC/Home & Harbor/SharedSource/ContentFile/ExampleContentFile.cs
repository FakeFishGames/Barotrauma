using Barotrauma;

namespace HomeAndHarbor
{
    public class ExampleContentFile : GenericPrefabFile<ExamplePrefab>
    {
        public ExampleContentFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "ExamplePrefab";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "ExamplePrefabs";
        protected override ExamplePrefab CreatePrefab(ContentXElement element) => new(this, element);
        protected override PrefabCollection<ExamplePrefab> Prefabs => ExamplePrefab.Prefabs;
    }
}