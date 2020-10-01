using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        public override string DebugTag => $"find diving gear ({gearTag})";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;

        private readonly string gearTag;

        private AIObjectiveGetItem getDivingGear;
        private AIObjectiveContainItem getOxygen;
        private Item targetItem;

        public static float MIN_OXYGEN = 10;
        public static string HEAVY_DIVING_GEAR = "deepdiving";
        public static string LIGHT_DIVING_GEAR = "lightdiving";
        public static string OXYGEN_SOURCE = "oxygensource";

        protected override bool Check() => targetItem != null && character.HasEquippedItem(targetItem);

        public AIObjectiveFindDivingGear(Character character, bool needsDivingSuit, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTag = needsDivingSuit ? HEAVY_DIVING_GEAR : LIGHT_DIVING_GEAR;
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
            targetItem = character.Inventory.FindItemByTag(gearTag, true);
            if (targetItem == null || !character.HasEquippedItem(targetItem))
            {
                TryAddSubObjective(ref getDivingGear, () =>
                {
                    if (targetItem == null)
                    {
                        character.Speak(TextManager.Get("DialogGetDivingGear"), null, 0.0f, "getdivinggear", 30.0f);
                    }
                    return new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true)
                    {
                        AllowToFindDivingGear = false,
                        AllowDangerousPressure = true
                    };
                }, 
                onAbandon: () => Abandon = true,
                onCompleted: () => RemoveSubObjective(ref getDivingGear));
            }
            else
            {
                if (!DropEmptyTanks(character, targetItem, out Item[] containedItems))
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveFindDivingGear failed - the item \"" + targetItem + "\" has no proper inventory");
#endif
                    Abandon = true;
                    return;
                }
                if (containedItems.None(it => it != null && it.HasTag(OXYGEN_SOURCE) && it.Condition > MIN_OXYGEN))
                {
                    // No valid oxygen source loaded.
                    // Seek oxygen that has min 10% condition left.
                    TryAddSubObjective(ref getOxygen, () =>
                    {
                        character.Speak(TextManager.Get("DialogGetOxygenTank"), null, 0, "getoxygentank", 30.0f);
                        return new AIObjectiveContainItem(character, OXYGEN_SOURCE, targetItem.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == Character.TeamType.FriendlyNPC)
                        {
                            AllowToFindDivingGear = false,
                            AllowDangerousPressure = true,
                            ConditionLevel = MIN_OXYGEN
                        };
                    },
                    onAbandon: () =>
                    {
                        // Try to seek any oxygen sources.
                        TryAddSubObjective(ref getOxygen, () =>
                        {
                            return new AIObjectiveContainItem(character, OXYGEN_SOURCE, targetItem.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == Character.TeamType.FriendlyNPC)
                            {
                                AllowToFindDivingGear = false,
                                AllowDangerousPressure = true,
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

        /// <summary>
        /// Returns false only when no inventory can be found from the item.
        /// </summary>
        public static bool DropEmptyTanks(Character actor, Item target, out Item[] containedItems)
        {
            containedItems = target.OwnInventory?.Items;
            if (containedItems == null)
            {
                return false;
            }
            foreach (Item containedItem in containedItems)
            {
                if (containedItem == null) { continue; }
                if (containedItem.Condition <= 0.0f)
                {
                    containedItem.Drop(actor);
                }
            }
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            getDivingGear = null;
            getOxygen = null;
            targetItem = null;
        }
    }
}
