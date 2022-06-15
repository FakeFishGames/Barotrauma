using Barotrauma.RuinGeneration;
using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class RuinConfigFile : GenericPrefabFile<RuinGenerationParams>
    {
        public RuinConfigFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "RuinConfig";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "RuinGenerationParameters";
        protected override PrefabCollection<RuinGenerationParams> Prefabs => RuinGenerationParams.RuinParams;
        protected override RuinGenerationParams CreatePrefab(ContentXElement element)
        {
            return new RuinGenerationParams(element, this);
        }
    }
}
