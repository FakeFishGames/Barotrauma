using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

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
            if (targetItem == null || !character.HasEquippedItem(targetItem) && targetItem.ContainedItems.Any(i => i.HasTag(OXYGEN_SOURCE) && i.Condition > 0))
            {
                TryAddSubObjective(ref getDivingGear, () =>
                {
                    if (targetItem == null && character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.Get("DialogGetDivingGear"), null, 0.0f, "getdivinggear", 30.0f);
                    }
                    return new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true)
                    {
                        AllowStealing = true,
                        AllowToFindDivingGear = false,
                        AllowDangerousPressure = true
                    };
                }, 
                onAbandon: () => Abandon = true,
                onCompleted: () => RemoveSubObjective(ref getDivingGear));
            }
            else
            {
                HumanAIController.UnequipContainedItems(targetItem, it => !it.HasTag("oxygensource"));
                HumanAIController.UnequipEmptyItems(targetItem);
                float min = character.Submarine == null ? 0.01f : MIN_OXYGEN;
                if (targetItem.OwnInventory != null && targetItem.OwnInventory.AllItems.None(it => it != null && it.HasTag(OXYGEN_SOURCE) && it.Condition > min))
                {
                    // No valid oxygen source loaded.
                    // Seek oxygen that has at least 10% condition left.
                    TryAddSubObjective(ref getOxygen, () =>
                    {
                        if (character.IsOnPlayerTeam)
                        {
                            if (HumanAIController.HasItem(character, "oxygensource", out _, conditionPercentage: min))
                            {
                                character.Speak(TextManager.Get("dialogswappingoxygentank"), null, 0, "swappingoxygentank", 30.0f);
                            }
                            else
                            {
                                character.Speak(TextManager.Get("DialogGetOxygenTank"), null, 0, "getoxygentank", 30.0f);
                            }
                        }
                        return new AIObjectiveContainItem(character, OXYGEN_SOURCE, targetItem.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                        {
                            AllowToFindDivingGear = false,
                            AllowDangerousPressure = true,
                            ConditionLevel = MIN_OXYGEN
                        };
                    },
                    onAbandon: () =>
                    {
                        getOxygen = null;
                        int remainingTanks = ReportOxygenTankCount();
                        // Try to seek any oxygen sources.
                        TryAddSubObjective(ref getOxygen, () =>
                        {
                            return new AIObjectiveContainItem(character, OXYGEN_SOURCE, targetItem.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                            {
                                AllowToFindDivingGear = false,
                                AllowDangerousPressure = true
                            };
                        },
                        onAbandon: () =>
                        {
                            Abandon = true;
                            if (remainingTanks > 0 && !HumanAIController.HasItem(character, "oxygensource", out _, conditionPercentage: 0.01f))
                            {
                                character.Speak(TextManager.Get("dialogcantfindtoxygen"), null, 0, "cantfindoxygen", 30.0f);
                            }
                        },
                        onCompleted: () => RemoveSubObjective(ref getOxygen));
                    },
                    onCompleted: () =>
                    {
                        RemoveSubObjective(ref getOxygen);
                        ReportOxygenTankCount();
                    });

                    int ReportOxygenTankCount()
                    {
                        int remainingOxygenTanks = Submarine.MainSub.GetItems(false).Count(i => i.HasTag("oxygensource") && i.Condition > 1);
                        if (remainingOxygenTanks == 0)
                        {
                            character.Speak(TextManager.Get("DialogOutOfOxygenTanks"), null, 0.0f, "outofoxygentanks", 30.0f);
                        }
                        else if (remainingOxygenTanks < 10)
                        {
                            character.Speak(TextManager.Get("DialogLowOnOxygenTanks"), null, 0.0f, "lowonoxygentanks", 30.0f);
                        }
                        return remainingOxygenTanks;
                    }
                }
            }
        }

        /// <summary>
        /// Returns false only when no inventory can be found from the item.
        /// </summary>
        public static bool EjectEmptyTanks(Character actor, Item target, out IEnumerable<Item> containedItems)
        {
            containedItems = target.OwnInventory?.AllItems;
            if (containedItems == null) { return false; }
            AIController.UnequipEmptyItems(actor, target);
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
