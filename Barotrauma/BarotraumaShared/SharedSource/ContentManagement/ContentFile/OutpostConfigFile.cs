using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage(alternativeTypes: typeof(OutpostFile))]
    sealed class OutpostConfigFile : GenericPrefabFile<OutpostGenerationParams>
    {
        public OutpostConfigFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "OutpostConfig";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "OutpostGenerationParameters";
        protected override PrefabCollection<OutpostGenerationParams> Prefabs => OutpostGenerationParams.OutpostParams;
        protected override OutpostGenerationParams CreatePrefab(ContentXElement element)
        {
            return new OutpostGenerationParams(element, this);
        }
    }
}
