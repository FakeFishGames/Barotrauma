using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class UpgradeModulesFile : GenericPrefabFile<UpgradeContentPrefab>
    {
        public UpgradeModulesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) =>
            identifier == "upgrademodule" ||
            identifier == "upgradecategory";

        protected override bool MatchesPlural(Identifier identifier) =>
            identifier == "upgrademodules";

        protected override PrefabCollection<UpgradeContentPrefab> Prefabs => UpgradeContentPrefab.PrefabsAndCategories;
        protected override UpgradeContentPrefab CreatePrefab(ContentXElement element)
        {
            Identifier elemName = element.NameAsIdentifier();
            if (elemName == "upgradecategory")
            {
                return new UpgradeCategory(element, this);
            }
            else
            {
                return new UpgradePrefab(element, this);
            }
        }
    }
}
