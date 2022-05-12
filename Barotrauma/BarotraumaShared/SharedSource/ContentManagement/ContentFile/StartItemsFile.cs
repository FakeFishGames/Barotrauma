namespace Barotrauma
{
    sealed class StartItemsFile : GenericPrefabFile<StartItemSet>
    {
        public StartItemsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "itemset";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "startitems";
        protected override PrefabCollection<StartItemSet> Prefabs => StartItemSet.Sets;
        protected override StartItemSet CreatePrefab(ContentXElement element) => new StartItemSet(element, this);
    }
}
