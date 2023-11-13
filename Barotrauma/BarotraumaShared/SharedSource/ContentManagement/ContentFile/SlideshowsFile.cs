namespace Barotrauma
{
    sealed class SlideshowsFile : GenericPrefabFile<SlideshowPrefab>
    {
        protected override PrefabCollection<SlideshowPrefab> Prefabs => SlideshowPrefab.Prefabs;

        public SlideshowsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "Slideshow";

        protected override bool MatchesPlural(Identifier identifier) => identifier == "Slideshows";

        protected override SlideshowPrefab CreatePrefab(ContentXElement element) => new SlideshowPrefab(this, element);
    }
}