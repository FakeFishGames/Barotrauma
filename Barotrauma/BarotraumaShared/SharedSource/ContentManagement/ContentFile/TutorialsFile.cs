namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class TutorialsFile : GenericPrefabFile<TutorialPrefab>
    {
        protected override PrefabCollection<TutorialPrefab> Prefabs => TutorialPrefab.Prefabs;

        public TutorialsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "Tutorial";

        protected override bool MatchesPlural(Identifier identifier) => identifier == "Tutorials";

        protected override TutorialPrefab CreatePrefab(ContentXElement element) => new TutorialPrefab(this, element);
    }
}