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

        public static float lowOxygenThreshold = 10;

        protected override bool Check() => HumanAIController.HasItem(character, gearTag, "oxygensource") || HumanAIController.HasItem(character, fallbackTag, "oxygensource");

        public AIObjectiveFindDivingGear(Character character, bool needDivingSuit, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTag = needDivingSuit ? "divingsuit" : "divingmask";
            fallbackTag = needDivingSuit ? "divingsuit" : "diving";
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
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
                    return new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true) { AllowToFindDivingGear = false };
                }, 
                onAbandon: () => Abandon = true,
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
                if (containedItems.None(it => it.HasTag("oxygensource") && it.Condition > lowOxygenThreshold))
                {
                    var oxygenTank = character.Inventory.FindItemByTag("oxygensource", true);
                    if (oxygenTank != null)
                    {
                        var container = item.GetComponent<ItemContainer>();
                        if (container.Item.ParentInventory == character.Inventory)
                        {
                            character.Inventory.RemoveItem(oxygenTank);
                            if (!container.Inventory.TryPutItem(oxygenTank, null))
                            {
                                oxygenTank.Drop(character);
                                Abandon = true;
                            }
                        }
                        else
                        {
                            container.Combine(oxygenTank);
                        }
                    }
                    else
                    {
                        // Seek oxygen that has min 10% condition left
                        TryAddSubObjective(ref getOxygen, () =>
                        {
                            character.Speak(TextManager.Get("DialogGetOxygenTank"), null, 0, "getoxygentank", 30.0f);
                            return new AIObjectiveContainItem(character, new string[] { "oxygensource" }, item.GetComponent<ItemContainer>(), objectiveManager)
                            {
                                AllowToFindDivingGear = false,
                                ConditionLevel = lowOxygenThreshold
                            };
                        }, 
                        onAbandon: () =>
                        {
                            // Try to seek any oxygen sources
                            TryAddSubObjective(ref getOxygen, () =>
                            {
                                return new AIObjectiveContainItem(character, new string[] { "oxygensource" }, item.GetComponent<ItemContainer>(), objectiveManager)
                                {
                                    AllowToFindDivingGear = false,
                                    ConditionLevel = 0
                                };
                            },
                            onAbandon: () => Abandon = true,
                            onCompleted: () => RemoveSubObjective(ref getOxygen));
                        },
                        onCompleted: () => RemoveSubObjective(ref getOxygen));
                    }
                }
            }
        }
    }
}
