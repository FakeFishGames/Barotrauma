namespace Barotrauma
{
    sealed class NPCPersonalityTraitsFile : GenericPrefabFile<NPCPersonalityTrait>
    {
        public NPCPersonalityTraitsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "personalitytrait";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "personalitytraits";
        protected override PrefabCollection<NPCPersonalityTrait> Prefabs => NPCPersonalityTrait.Traits;
        protected override NPCPersonalityTrait CreatePrefab(ContentXElement element) => new NPCPersonalityTrait(element, this);
    }
}
