using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        public override Identifier Identifier { get; set; } = "find diving gear".ToIdentifier();
        public override string DebugTag => $"{Identifier} ({gearTag})";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;

        private readonly Identifier gearTag;

        private AIObjectiveGetItem getDivingGear;
        private AIObjectiveContainItem getOxygen;
        private Item targetItem;

        public const float MIN_OXYGEN = 10;

        public static readonly Identifier HEAVY_DIVING_GEAR = "deepdiving".ToIdentifier();
        public static readonly Identifier LIGHT_DIVING_GEAR = "lightdiving".ToIdentifier();
        /// <summary>
        /// Diving gear that's suitable for wearing indoors (-> the bots don't try to unequip it when they don't need diving gear)
        /// </summary>
        public static readonly Identifier DIVING_GEAR_WEARABLE_INDOORS = "divinggear_wearableindoors".ToIdentifier();
        public static readonly Identifier OXYGEN_SOURCE = "oxygensource".ToIdentifier();

        protected override bool CheckObjectiveSpecific() => targetItem != null && character.HasEquippedItem(targetItem, slotType: InvSlotType.OuterClothes | InvSlotType.Head);

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
            if (targetItem == null && gearTag == LIGHT_DIVING_GEAR)
            {
                targetItem = character.Inventory.FindItemByTag(HEAVY_DIVING_GEAR, true);
            }
            if (targetItem == null || !character.HasEquippedItem(targetItem, slotType: InvSlotType.OuterClothes | InvSlotType.Head | InvSlotType.InnerClothes) && targetItem.ContainedItems.Any(i => i.HasTag(OXYGEN_SOURCE) && i.Condition > 0))
            {
                TryAddSubObjective(ref getDivingGear, () =>
                {
                    if (targetItem == null && character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.Get("DialogGetDivingGear").Value, null, 0.0f, "getdivinggear".ToIdentifier(), 30.0f);
                    }
                    return new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true)
                    {
                        AllowStealing = HumanAIController.NeedsDivingGear(character.CurrentHull, out _),
                        AllowToFindDivingGear = false,
                        AllowDangerousPressure = true,
                        EquipSlotType = InvSlotType.OuterClothes | InvSlotType.Head | InvSlotType.InnerClothes,
                        Wear = true
                    };
                }, 
                onAbandon: () => Abandon = true,
                onCompleted: () =>
                {
                    RemoveSubObjective(ref getDivingGear);
                    if (gearTag == HEAVY_DIVING_GEAR && HumanAIController.HasItem(character, LIGHT_DIVING_GEAR, out IEnumerable<Item> masks, requireEquipped: true))
                    {
                        foreach (Item mask in masks)
                        {
                            if (mask != targetItem)
                            {
                                character.Inventory.TryPutItem(mask, character, CharacterInventory.anySlot);
                            }
                        }
                    }
                });
            }
            else
            {
                float min = GetMinOxygen(character);
                if (targetItem.OwnInventory != null && targetItem.OwnInventory.AllItems.None(it => it != null && it.HasTag(OXYGEN_SOURCE) && it.Condition > min))
                {
                    TryAddSubObjective(ref getOxygen, () =>
                    {
                        if (character.IsOnPlayerTeam)
                        {
                            if (HumanAIController.HasItem(character, OXYGEN_SOURCE, out _, conditionPercentage: min))
                            {
                                character.Speak(TextManager.Get("dialogswappingoxygentank").Value, null, 0, "swappingoxygentank".ToIdentifier(), 30.0f);
                                if (character.Inventory.FindAllItems(i => i.HasTag(OXYGEN_SOURCE) && i.Condition > min).Count == 1)
                                {
                                    character.Speak(TextManager.Get("dialoglastoxygentank").Value, null, 0.0f, "dialoglastoxygentank".ToIdentifier(), 30.0f);
                                }
                            }
                            else
                            {
                                character.Speak(TextManager.Get("DialogGetOxygenTank").Value, null, 0, "getoxygentank".ToIdentifier(), 30.0f);
                            }
                        }
                        var container = targetItem.GetComponent<ItemContainer>();                        
                        var objective = new AIObjectiveContainItem(character, OXYGEN_SOURCE, container, objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                        {
                            AllowToFindDivingGear = false,
                            AllowDangerousPressure = true,
                            ConditionLevel = MIN_OXYGEN,
                            RemoveExistingWhenNecessary = true
                        };
                        if (container.HasSubContainers)
                        {
                            objective.TargetSlot = container.FindSuitableSubContainerIndex(OXYGEN_SOURCE);
                        }
                        return objective;
                    },
                    onAbandon: () =>
                    {
                        getOxygen = null;
                        int remainingTanks = ReportOxygenTankCount();
                        // Try to seek any oxygen sources, even if they have minimal amount of oxygen.
                        TryAddSubObjective(ref getOxygen, () =>
                        {
                            return new AIObjectiveContainItem(character, OXYGEN_SOURCE, targetItem.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                            {
                                AllowToFindDivingGear = false,
                                AllowDangerousPressure = true,
                                RemoveExisting = true
                            };
                        },
                        onAbandon: () =>
                        {
                            Abandon = true;
                            if (remainingTanks > 0 && !HumanAIController.HasItem(character, OXYGEN_SOURCE, out _, conditionPercentage: 0.01f))
                            {
                                character.Speak(TextManager.Get("dialogcantfindtoxygen").Value, null, 0, "cantfindoxygen".ToIdentifier(), 30.0f);
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
                        if (character.Submarine != Submarine.MainSub) { return 1; }
                        int remainingOxygenTanks = Submarine.MainSub.GetItems(false).Count(i => i.HasTag(OXYGEN_SOURCE) && i.Condition > 1);
                        if (remainingOxygenTanks == 0)
                        {
                            character.Speak(TextManager.Get("DialogOutOfOxygenTanks").Value, null, 0.0f, "outofoxygentanks".ToIdentifier(), 30.0f);
                        }
                        else if (remainingOxygenTanks < 10)
                        {
                            character.Speak(TextManager.Get("DialogLowOnOxygenTanks").Value, null, 0.0f, "lowonoxygentanks".ToIdentifier(), 30.0f);
                        }
                        return remainingOxygenTanks;
                    }
                }
            }
        }

        public override void Reset()
        {
            base.Reset();
            getDivingGear = null;
            getOxygen = null;
            targetItem = null;
        }

        public static float GetMinOxygen(Character character)
        {
            // Seek oxygen that has at least 10% condition left, if we are inside a friendly sub.
            // The margin helps us to survive, because we might need some oxygen before we can find more oxygen.
            // When we are venturing outside of our sub, let's just suppose that we have enough oxygen with us and optimize it so that we don't keep switching off half used tanks.
            float min = 0.01f;
            float minOxygen = character.IsInFriendlySub ? MIN_OXYGEN : min;
            if (minOxygen > min && character.Inventory.AllItems.Any(i => i.HasTag("oxygensource") && i.ConditionPercentage >= minOxygen))
            {
                // There's a valid oxygen tank in the inventory -> no need to swap the tank too early.
                minOxygen = min;
            }
            return minOxygen;
        }
    }
}
