using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class WreckAIConfigFile : GenericPrefabFile<WreckAIConfig>
    {
        public WreckAIConfigFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "wreckaiconfig";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "wreckaiconfigs";
        protected override PrefabCollection<WreckAIConfig> prefabs => WreckAIConfig.Prefabs;
        protected override WreckAIConfig CreatePrefab(ContentXElement element)
        {
            return new WreckAIConfig(element, this);
        }
    }
}
