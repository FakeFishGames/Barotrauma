namespace Barotrauma
{
#if CLIENT
    [NotSyncedInMultiplayer]
    sealed class BackgroundCreaturePrefabsFile : GenericPrefabFile<BackgroundCreaturePrefab>
    {
        public BackgroundCreaturePrefabsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "backgroundcreatures";
        protected override PrefabCollection<BackgroundCreaturePrefab> Prefabs => BackgroundCreaturePrefab.Prefabs;
        protected override BackgroundCreaturePrefab CreatePrefab(ContentXElement element)
        {
            return new BackgroundCreaturePrefab(element, this);
        }

        public sealed override Md5Hash CalculateHash() => Md5Hash.Blank;
    }
#else
    [NotSyncedInMultiplayer]
    sealed class BackgroundCreaturePrefabsFile : OtherFile
    {
        public BackgroundCreaturePrefabsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path)
        {
        }
    }
#endif
}