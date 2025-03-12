using System.Collections.Generic;

namespace Barotrauma.PerkBehaviors
{
    internal class UpgradeSubmarinePerk : PerkBase
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier UpgradeIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CategoryIdentifier { get; set; }

        [Serialize(0, IsPropertySaveable.Yes)]
        public int Level { get; set; }

        public override PerkSimulation Simulation
            => PerkSimulation.ServerAndClients;

        public UpgradeSubmarinePerk(ContentXElement element, DisembarkPerkPrefab prefab) : base(element, prefab) { }

        public override void ApplyOnRoundStart(IReadOnlyCollection<Character> teamCharacters, Submarine teamSubmarine)
        {
            if (teamSubmarine is null) { return; }

            bool prefabFound = UpgradePrefab.Prefabs.TryGet(UpgradeIdentifier, out UpgradePrefab upgradePrefab);
            bool categoryFound = UpgradeCategory.Categories.TryGet(CategoryIdentifier, out UpgradeCategory upgradeCategory);

            if (!prefabFound)
            {
                DebugConsole.ThrowError($"{nameof(UpgradeSubmarinePerk)}: Upgrade prefab not found");
                return;
            }

            if (upgradePrefab.IsWallUpgrade)
            {
                foreach (Structure structure in teamSubmarine.GetWalls(UpgradeManager.UpgradeAlsoConnectedSubs))
                {
                    structure.AddUpgrade(new Upgrade(structure, upgradePrefab, Level), createNetworkEvent: true);
                }
            }
            else if (categoryFound)
            {
                foreach (Item item in teamSubmarine.GetItems(UpgradeManager.UpgradeAlsoConnectedSubs))
                {
                    if (upgradeCategory.CanBeApplied(item, upgradePrefab))
                    {
                        item.AddUpgrade(new Upgrade(item, upgradePrefab, Level), createNetworkEvent: true);
                    }
                }
            }
            else
            {
                DebugConsole.ThrowError($"{nameof(UpgradeSubmarinePerk)}: Upgrade category not found");
            }
        }
    }
}