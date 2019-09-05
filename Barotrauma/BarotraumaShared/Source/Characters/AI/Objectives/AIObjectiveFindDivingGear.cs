using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        public override string DebugTag => $"find diving gear ({gearTag})";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;

        private readonly string gearTag;
        private readonly string fallbackTag;

        private AIObjectiveGetItem getDivingGear;
        private AIObjectiveContainItem getOxygen;

        protected override bool Check() => HumanAIController.HasItem(character, gearTag, "oxygensource") || HumanAIController.HasItem(character, fallbackTag, "oxygensource");

        public override float GetPriority() => MathHelper.Clamp(100 - character.OxygenAvailable - 10, 0, 100);

        public AIObjectiveFindDivingGear(Character character, bool needDivingSuit, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTag = needDivingSuit ? "divingsuit" : "divingmask";
            fallbackTag = needDivingSuit ? "divingsuit" : "diving";
        }

        protected override void Act(float deltaTime)
        {
            var item = character.Inventory.FindItemByIdentifier(gearTag, true) ?? character.Inventory.FindItemByTag(gearTag, true);
            if (item == null && fallbackTag != gearTag)
            {
                item = character.Inventory.FindItemByTag(fallbackTag, true);
            }
            if (item == null || !character.HasEquippedItem(item))
            {
                TryAddSubObjective(ref getDivingGear, () =>
                {
                    if (item == null)
                    {
                        character.Speak(TextManager.Get("DialogGetDivingGear"), null, 0.0f, "getdivinggear", 30.0f);
                    }
                    return new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true);
                }, 
                onAbandon: () => RemoveSubObjective(ref getDivingGear),
                onCompleted: () => RemoveSubObjective(ref getDivingGear));
            }
            else
            {
                var containedItems = item.ContainedItems;
                if (containedItems == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveFindDivingGear failed - the item \"" + item + "\" has no proper inventory");
#endif
                    Abandon = true;
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
                        }, 
                        onAbandon: () => RemoveSubObjective(ref getOxygen),
                        onCompleted: () => RemoveSubObjective(ref getOxygen));
                    }
                }
            }
        }
    }
}
