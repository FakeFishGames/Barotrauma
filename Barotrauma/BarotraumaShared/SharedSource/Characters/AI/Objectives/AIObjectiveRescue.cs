using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescue : AIObjective
    {
        public override string DebugTag => "rescue";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        public override bool AllowOutsideSubmarine => true;
        public override bool AllowInAnySub => true;

        const float TreatmentDelay = 0.5f;

        const float CloseEnoughToTreat = 100.0f;

        private readonly Character targetCharacter;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveContainItem replaceOxygenObjective;
        private AIObjectiveGetItem getItemObjective;
        private float treatmentTimer;
        private Hull safeHull;
        private float findHullTimer;
        private bool ignoreOxygen;
        private readonly float findHullInterval = 1.0f;

        public AIObjectiveRescue(Character character, Character targetCharacter, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            if (targetCharacter == null)
            {
                string errorMsg = $"{character.Name}: Attempted to create a Rescue objective with no target!\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("AIObjectiveRescue:ctor:targetnull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                Abandon = true;
                return;
            }
            this.targetCharacter = targetCharacter;
        }

        protected override void OnAbandon()
        {
            character.SelectedCharacter = null;
            base.OnAbandon();
        }

        protected override void OnCompleted()
        {
            character.SelectedCharacter = null;
            base.OnCompleted();
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands || targetCharacter == null || targetCharacter.CurrentHull == null || targetCharacter.Removed || targetCharacter.IsDead)
            {
                Abandon = true;
                return;
            }
            var otherRescuer = targetCharacter.SelectedBy;
            if (otherRescuer != null && otherRescuer != character)
            {
                // Someone else is rescuing/holding the target.
                Abandon = otherRescuer.IsPlayer || character.GetSkillLevel("medical") < otherRescuer.GetSkillLevel("medical");
                return;
            }
            if (targetCharacter != character)
            {
                if (targetCharacter.IsIncapacitated)
                {
                    // Check if the character needs more oxygen
                    if (!ignoreOxygen && character.SelectedCharacter == targetCharacter || character.CanInteractWith(targetCharacter))
                    {
                        // Replace empty oxygen tank
                        // First remove empty tanks
                        if (HumanAIController.HasItem(targetCharacter, AIObjectiveFindDivingGear.HEAVY_DIVING_GEAR, out IEnumerable<Item> suits, requireEquipped: true))
                        {
                            Item suit = suits.FirstOrDefault();
                            if (suit != null)
                            {
                                AIObjectiveFindDivingGear.DropEmptyTanks(character, suit, out _);
                            }
                        }
                        else if (HumanAIController.HasItem(targetCharacter, AIObjectiveFindDivingGear.LIGHT_DIVING_GEAR, out IEnumerable<Item> masks, requireEquipped: true))
                        {
                            Item mask = masks.FirstOrDefault();
                            if (mask != null)
                            {
                                AIObjectiveFindDivingGear.DropEmptyTanks(character, mask, out _);
                            }
                        }
                        bool ShouldRemoveDivingSuit() => targetCharacter.OxygenAvailable < CharacterHealth.InsufficientOxygenThreshold && targetCharacter.CurrentHull?.LethalPressure <= 0;
                        if (ShouldRemoveDivingSuit())
                        {
                            suits.ForEach(suit => suit.Drop(character));
                        }
                        else if (suits.Any() && suits.None(s => s.OwnInventory?.Items != null && s.OwnInventory.Items.Any(it => it != null && it.HasTag(AIObjectiveFindDivingGear.OXYGEN_SOURCE) && it.ConditionPercentage > 0)))
                        {
                            // The target has a suit equipped with an empty oxygen tank.
                            // Can't remove the suit, because the target needs it.
                            // If we happen to have an extra oxygen tank in the inventory, let's swap it.
                            Item spareOxygenTank = FindOxygenTank(targetCharacter) ?? FindOxygenTank(character);
                            if (spareOxygenTank != null)
                            {
                                Item suit = suits.FirstOrDefault();
                                if (suit != null)
                                {
                                    // Insert the new oxygen tank
                                    TryAddSubObjective(ref replaceOxygenObjective, () => new AIObjectiveContainItem(character, spareOxygenTank, suit.GetComponent<ItemContainer>(), objectiveManager),
                                        onCompleted: () => RemoveSubObjective(ref replaceOxygenObjective),
                                        onAbandon: () =>
                                        {
                                            RemoveSubObjective(ref replaceOxygenObjective);
                                            ignoreOxygen = true;
                                            if (ShouldRemoveDivingSuit())
                                            {
                                                suits.ForEach(suit => suit.Drop(character));
                                            }
                                        });
                                    return;
                                }
                            }

                            Item FindOxygenTank(Character c) =>
                                c.Inventory.FindItem(i =>
                                i.HasTag(AIObjectiveFindDivingGear.OXYGEN_SOURCE) &&
                                i.ConditionPercentage > 1 &&
                                i.FindParentInventory(inv => inv.Owner is Item otherItem && otherItem.HasTag("diving")) == null,
                                recursive: true);
                        }
                    }
                    if (HumanAIController.GetHullSafety(targetCharacter.CurrentHull, targetCharacter) < HumanAIController.HULL_SAFETY_THRESHOLD)
                    {
                        // Incapacitated target is not in a safe place -> Move to a safe place first
                        if (character.SelectedCharacter != targetCharacter)
                        {
                            if (targetCharacter.CurrentHull != null && HumanAIController.VisibleHulls.Contains(targetCharacter.CurrentHull) && targetCharacter.CurrentHull.DisplayName != null)
                            {
                                character.Speak(TextManager.GetWithVariables("DialogFoundUnconsciousTarget", new string[2] { "[targetname]", "[roomname]" },
                                    new string[2] { targetCharacter.Name, targetCharacter.CurrentHull.DisplayName }, new bool[2] { false, true }),
                                    null, 1.0f, "foundunconscioustarget" + targetCharacter.Name, 60.0f);
                            }
                            // Go to the target and select it
                            if (!character.CanInteractWith(targetCharacter))
                            {
                                RemoveSubObjective(ref replaceOxygenObjective);
                                RemoveSubObjective(ref goToObjective);
                                TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(targetCharacter, character, objectiveManager)
                                {
                                    CloseEnough = CloseEnoughToTreat,
                                    DialogueIdentifier = "dialogcannotreachpatient",
                                    TargetName = targetCharacter.DisplayName
                                },
                                onCompleted: () => RemoveSubObjective(ref goToObjective),
                                onAbandon: () =>
                                {
                                    RemoveSubObjective(ref goToObjective);
                                    Abandon = true;
                                });
                            }
                            else
                            {
                                character.SelectCharacter(targetCharacter);
                            }
                        }
                        else
                        {
                            // Drag the character into safety
                            if (safeHull == null)
                            {
                                if (findHullTimer > 0)
                                {
                                    findHullTimer -= deltaTime;
                                }
                                else
                                {
                                    safeHull = objectiveManager.GetObjective<AIObjectiveFindSafety>().FindBestHull(HumanAIController.VisibleHulls);
                                    findHullTimer = findHullInterval * Rand.Range(0.9f, 1.1f);
                                }
                            }
                            if (safeHull != null && character.CurrentHull != safeHull)
                            {
                                RemoveSubObjective(ref replaceOxygenObjective);
                                RemoveSubObjective(ref goToObjective);
                                TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(safeHull, character, objectiveManager),
                                    onCompleted: () => RemoveSubObjective(ref goToObjective),
                                    onAbandon: () =>
                                    {
                                        RemoveSubObjective(ref goToObjective);
                                        safeHull = character.CurrentHull;
                                    });
                            }
                        }
                    }
                }
            }

            if (subObjectives.Any()) { return; }

            if (targetCharacter != character && !character.CanInteractWith(targetCharacter))
            {
                RemoveSubObjective(ref replaceOxygenObjective);
                RemoveSubObjective(ref goToObjective);
                // Go to the target and select it
                TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(targetCharacter, character, objectiveManager)
                {
                    CloseEnough = CloseEnoughToTreat,
                    DialogueIdentifier = "dialogcannotreachpatient",
                    TargetName = targetCharacter.DisplayName
                },
                    onCompleted: () => RemoveSubObjective(ref goToObjective),
                    onAbandon: () =>
                    {
                        RemoveSubObjective(ref goToObjective);
                        Abandon = true;
                    });
            }
            else
            {
                // We can start applying treatment
                if (character != targetCharacter && character.SelectedCharacter != targetCharacter)
                {
                    if (targetCharacter.CurrentHull.DisplayName != null)
                    {
                        character.Speak(TextManager.GetWithVariables("DialogFoundWoundedTarget", new string[2] { "[targetname]", "[roomname]" },
                            new string[2] { targetCharacter.Name, targetCharacter.CurrentHull.DisplayName }, new bool[2] { false, true }),
                            null, 1.0f, "foundwoundedtarget" + targetCharacter.Name, 60.0f);
                    }

                    character.SelectCharacter(targetCharacter);
                }
                GiveTreatment(deltaTime);
            }
        }

        private readonly List<string> suitableItemIdentifiers = new List<string>();
        private readonly List<string> itemNameList = new List<string>();
        private readonly Dictionary<string, float> currentTreatmentSuitabilities = new Dictionary<string, float>();
        private void GiveTreatment(float deltaTime)
        {
            if (targetCharacter == null)
            {
                string errorMsg = $"{character.Name}: Attempted to update a Rescue objective with no target!";
                DebugConsole.ThrowError(errorMsg);
                Abandon = true;
                return;
            }

            SteeringManager?.Reset();

            if (!targetCharacter.IsPlayer)
            {
                // If the target is a bot, don't let it move
                targetCharacter.AIController?.SteeringManager?.Reset();
            }
            if (treatmentTimer > 0.0f)
            {
                treatmentTimer -= deltaTime;
                return;
            }
            treatmentTimer = TreatmentDelay;

            //find which treatments are the most suitable to treat the character's current condition
            targetCharacter.CharacterHealth.GetSuitableTreatments(currentTreatmentSuitabilities, normalize: false);

            //check if we already have a suitable treatment for any of the afflictions
            foreach (Affliction affliction in GetSortedAfflictions(targetCharacter))
            {
                if (affliction == null) { throw new Exception("Affliction was null"); }
                if (affliction.Prefab == null) { throw new Exception("Affliction prefab was null"); }
                foreach (KeyValuePair<string, float> treatmentSuitability in affliction.Prefab.TreatmentSuitability)
                {
                    if (currentTreatmentSuitabilities.ContainsKey(treatmentSuitability.Key) && currentTreatmentSuitabilities[treatmentSuitability.Key] > 0.0f)
                    {
                        Item matchingItem = character.Inventory.FindItemByIdentifier(treatmentSuitability.Key, true);
                        if (matchingItem == null) { continue; }
                        ApplyTreatment(affliction, matchingItem);
                        //wait a bit longer after applying a treatment to wait for potential side-effects to manifest
                        treatmentTimer = TreatmentDelay * 4;
                        return;
                    }
                }
            }
            // Find treatments outside of own inventory only if inside the own sub.
            if (character.Submarine != null && character.Submarine.TeamID == character.TeamID)
            {
                float cprSuitability = targetCharacter.Oxygen < 0.0f ? -targetCharacter.Oxygen * 100.0f : 0.0f;
                //didn't have any suitable treatments available, try to find some medical items
                if (currentTreatmentSuitabilities.Any(s => s.Value > cprSuitability))
                {
                    itemNameList.Clear();
                    suitableItemIdentifiers.Clear();
                    foreach (KeyValuePair<string, float> treatmentSuitability in currentTreatmentSuitabilities)
                    {
                        if (treatmentSuitability.Value <= cprSuitability) { continue; }
                        if (MapEntityPrefab.Find(null, treatmentSuitability.Key, showErrorMessages: false) is ItemPrefab itemPrefab)
                        {
                            if (!Item.ItemList.Any(it => it.prefab.Identifier == treatmentSuitability.Key)) { continue; }
                            suitableItemIdentifiers.Add(treatmentSuitability.Key);
                            //only list the first 4 items
                            if (itemNameList.Count < 4)
                            {
                                itemNameList.Add(itemPrefab.Name);
                            }
                        }
                    }
                    if (itemNameList.Count > 0)
                    {
                        string itemListStr = "";
                        if (itemNameList.Count == 1)
                        {
                            itemListStr = itemNameList[0];
                        }
                        else
                        {
                            itemListStr = string.Join(" or ", string.Join(", ", itemNameList.Take(itemNameList.Count - 1)), itemNameList.Last());
                        }
                        if (targetCharacter != character)
                        {
                            character.Speak(TextManager.GetWithVariables("DialogListRequiredTreatments", new string[2] { "[targetname]", "[treatmentlist]" },
                                new string[2] { targetCharacter.Name, itemListStr }, new bool[2] { false, true }),
                                null, 2.0f, "listrequiredtreatments" + targetCharacter.Name, 60.0f);
                        }
                        character.DeselectCharacter();
                        RemoveSubObjective(ref getItemObjective);
                        TryAddSubObjective(ref getItemObjective,
                            constructor: () => new AIObjectiveGetItem(character, suitableItemIdentifiers.ToArray(), objectiveManager, equip: true, spawnItemIfNotFound: character.TeamID == Character.TeamType.FriendlyNPC),
                            onCompleted: () => RemoveSubObjective(ref getItemObjective),
                            onAbandon: () => RemoveSubObjective(ref getItemObjective));
                    }
                }
            }
            if (character != targetCharacter)
            {
                character.AnimController.Anim = AnimController.Animation.CPR;
            }
        }

        private void ApplyTreatment(Affliction affliction, Item item)
        {
            var targetLimb = targetCharacter.CharacterHealth.GetAfflictionLimb(affliction);
            bool remove = false;
            foreach (ItemComponent ic in item.Components)
            {
                if (!ic.HasRequiredContainedItems(user: character, addMessage: false)) { continue; }
#if CLIENT
                ic.PlaySound(ActionType.OnUse, character);
#endif
                ic.WasUsed = true;
                ic.ApplyStatusEffects(ActionType.OnUse, 1.0f, targetCharacter, targetLimb, user: character);
                if (ic.DeleteOnUse)
                {
                    remove = true;
                }
            }
            if (remove)
            {
                Entity.Spawner?.AddToRemoveQueue(item);
            }
        }

        protected override bool Check()
        {
            if (character.LockHands || targetCharacter == null || targetCharacter.CurrentHull == null || targetCharacter.Removed || targetCharacter.IsDead)
            {
                Abandon = true;
                return false;
            }
            // Don't go into rooms that have enemies
            if (Character.CharacterList.Any(c => c.CurrentHull == targetCharacter.CurrentHull && !HumanAIController.IsFriendly(character, c) && HumanAIController.IsActive(c)))
            {
                Abandon = true;
                return false;
            }
            bool isCompleted = AIObjectiveRescueAll.GetVitalityFactor(targetCharacter) >= AIObjectiveRescueAll.GetVitalityThreshold(objectiveManager, character, targetCharacter);
            if (isCompleted && targetCharacter != character)
            {                
                character.Speak(TextManager.GetWithVariable("DialogTargetHealed", "[targetname]", targetCharacter.Name),
                    null, 1.0f, "targethealed" + targetCharacter.Name, 60.0f);
            }
            return isCompleted;
        }

        public override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
                return Priority;
            }
            if (character.LockHands || targetCharacter == null || targetCharacter.CurrentHull == null || targetCharacter.Removed || targetCharacter.IsDead)
            {
                Priority = 0;
                Abandon = true;
            }
            else
            {
                // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
                float dist = Math.Abs(character.WorldPosition.X - targetCharacter.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - targetCharacter.WorldPosition.Y) * 2.0f;
                float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, dist));
                if (targetCharacter.CurrentHull == character.CurrentHull)
                {
                    distanceFactor = 1;
                }
                float vitalityFactor = 1 - AIObjectiveRescueAll.GetVitalityFactor(targetCharacter) / 100;
                float devotion = CumulatedDevotion / 100;
                Priority = MathHelper.Lerp(0, 100, MathHelper.Clamp(devotion + (vitalityFactor * distanceFactor * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        public static IEnumerable<Affliction> GetSortedAfflictions(Character character) => CharacterHealth.SortAfflictionsBySeverity(character.CharacterHealth.GetAllAfflictions());

        public override void Reset()
        {
            base.Reset();
            goToObjective = null;
            getItemObjective = null;
            replaceOxygenObjective = null;
            safeHull = null;
            ignoreOxygen = false;
        }
    }
}
