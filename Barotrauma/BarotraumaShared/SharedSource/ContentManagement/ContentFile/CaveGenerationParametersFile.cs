using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class CaveGenerationParametersFile : GenericPrefabFile<CaveGenerationParams>
    {
        public CaveGenerationParametersFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => identifier == "cave";
        protected override bool MatchesPlural(Identifier identifier) => identifier == "cavegenerationparameters";
        protected override PrefabCollection<CaveGenerationParams> prefabs => CaveGenerationParams.CaveParams;
        protected override CaveGenerationParams CreatePrefab(ContentXElement element)
        {
            return new CaveGenerationParams(element, this);
        }
    }
}
