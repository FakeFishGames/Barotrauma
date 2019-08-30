using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        public override string DebugTag => "find diving gear";
        public override bool ForceRun => true;

        private readonly string gearTag;

        private AIObjectiveGetItem getDivingGear;
        private AIObjectiveContainItem getOxygen;

        public override bool IsCompleted() => HumanAIController.HasItem(character, gearTag, "oxygensource");

        public override float GetPriority() => MathHelper.Clamp(100 - character.OxygenAvailable, 0, 100);
        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFindDivingGear;

        public AIObjectiveFindDivingGear(Character character, bool needDivingSuit, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTag = needDivingSuit ? "divingsuit" : "diving";
        }

        protected override void Act(float deltaTime)
        {
            var item = character.Inventory.FindItemByTag(gearTag);
            if (item == null || !character.HasEquippedItem(item))
            {
                TryAddSubObjective(ref getDivingGear, () =>
                {
                    character.Speak(TextManager.Get("DialogGetDivingGear"), null, 0.0f, "getdivinggear", 30.0f);
                    return new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true);
                });
            }
            else
            {
                var containedItems = item.ContainedItems;
                if (containedItems == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveFindDivingGear failed - the item \"" + item + "\" has no proper inventory");
#endif
                    abandon = true;
                    return;
                }
                // Drop empty tanks
                foreach (Item containedItem in containedItems)
                {
                    if (containedItem == null) { continue; }
                    if (containedItem.Condition <= 0.0f)
                    {
                        containedItem.Drop(character);
                    }
                }
                if (containedItems.None(it => it.HasTag("oxygensource") && it.Condition > 0.0f))
                {
                    var oxygenTank = character.Inventory.FindItemByTag("oxygensource", true);
                    if (oxygenTank != null)
                    {
                        var container = item.GetComponent<ItemContainer>();
                        if (container.Item.ParentInventory == character.Inventory)
                        {
                            character.Inventory.RemoveItem(oxygenTank);
                            container.Inventory.TryPutItem(oxygenTank, null);
                        }
                        else
                        {
                            container.Combine(oxygenTank);
                        }
                    }
                    else
                    {
                        TryAddSubObjective(ref getOxygen, () =>
                        {
                            character.Speak(TextManager.Get("DialogGetOxygenTank"), null, 0, "getoxygentank", 30.0f);
                            return new AIObjectiveContainItem(character, new string[] { "oxygentank", "oxygensource" }, item.GetComponent<ItemContainer>(), objectiveManager);
                        });
                    }
                }
            }
        }
    }
}
