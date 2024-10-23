using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.PerkBehaviors
{
    internal class SubItemSwapPerk : PerkBase
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetItem { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ReplacementItem { get; set; }

        public override PerkSimulation Simulation
            => PerkSimulation.ServerOnly;

        public SubItemSwapPerk(ContentXElement element, DisembarkPerkPrefab prefab) : base(element, prefab) { }

        public override bool CanApply(SubmarineInfo submarine)
        {
            XElement subElement = submarine.SubmarineElement;

            foreach (XElement element in subElement.Elements())
            {
                if (!element.Name.ToString().Equals(nameof(Item), StringComparison.OrdinalIgnoreCase)) { continue; }

                Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (identifier == TargetItem)
                {
                    return true;
                }
            }

            return false;
        }

        public override void ApplyOnRoundStart(IReadOnlyCollection<Character> teamCharacters, Submarine teamSubmarine)
        {
            if (teamSubmarine is null) { return; }

            List<Item> items = teamSubmarine.GetItems(true);

            ItemPrefab itemToInstall = ItemPrefab.Find(null, ReplacementItem);
            if (itemToInstall is null)
            {
                DebugConsole.ThrowError($"Could not find item \"{ReplacementItem}\" to swap with \"{TargetItem}\".");
                return;
            }

            foreach (Item item in items)
            {
                if (item.Prefab.Identifier == TargetItem)
                {
                    item.ReplaceWithLinkedItems(itemToInstall);
                    return;
                }
            }
        }
    }
}