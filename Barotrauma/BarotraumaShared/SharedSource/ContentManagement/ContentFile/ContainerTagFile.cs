#nullable enable

namespace Barotrauma
{
    internal sealed class ContainerTagFile : GenericPrefabFile<ContainerTagPrefab>
    {
        public ContainerTagFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "containertag";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "containertags";
        protected override PrefabCollection<ContainerTagPrefab> Prefabs => ContainerTagPrefab.Prefabs;

        protected override ContainerTagPrefab CreatePrefab(ContentXElement element)
            => new(element, this);
    }
}