using System.Xml.Linq;

namespace Barotrauma
{
    [NotSyncedInMultiplayer]
    sealed class ItemAssemblyFile : GenericPrefabFile<ItemAssemblyPrefab>
    {
        public ItemAssemblyFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "itemassembly";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "itemassemblies";
        protected override PrefabCollection<ItemAssemblyPrefab> prefabs => ItemAssemblyPrefab.Prefabs;
        protected override ItemAssemblyPrefab CreatePrefab(ContentXElement element)
        {
            return new ItemAssemblyPrefab(element, this);
        }
    }
}
