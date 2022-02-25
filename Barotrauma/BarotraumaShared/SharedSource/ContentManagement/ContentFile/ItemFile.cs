using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class ItemFile : GenericPrefabFile<ItemPrefab>
    {
        public ItemFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "items";
        protected override PrefabCollection<ItemPrefab> prefabs => ItemPrefab.Prefabs;
        protected override ItemPrefab CreatePrefab(ContentXElement element)
        {
            return new ItemPrefab(element, this);
        }
    }
}
