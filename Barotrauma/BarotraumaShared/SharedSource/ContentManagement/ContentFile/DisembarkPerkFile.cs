using System.Xml.Linq;

namespace Barotrauma
{
    internal sealed class DisembarkPerkFile : GenericPrefabFile<DisembarkPerkPrefab>
    {
        public DisembarkPerkFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "disembarkperk";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "disembarkperks";
        protected override PrefabCollection<DisembarkPerkPrefab> Prefabs => DisembarkPerkPrefab.Prefabs;
        protected override DisembarkPerkPrefab CreatePrefab(ContentXElement element)
        {
            return new DisembarkPerkPrefab(element, this);
        }
    }
}