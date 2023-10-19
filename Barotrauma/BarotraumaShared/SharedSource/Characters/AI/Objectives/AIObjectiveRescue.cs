﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using static Barotrauma.AIObjectiveFindSafety;

namespace Barotrauma
{
    class AIObjectiveRescue : AIObjective
    {
        public override Identifier Identifier { get; set; } = "rescue".ToIdentifier();
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        public override bool AllowOutsideSubmarine => true;
        public override bool AllowInAnySub => true;
        public override bool AllowWhileHandcuffed => false;

        const float TreatmentDelay = 0.5f;

        const float CloseEnoughToTreat = 100.0f;

        public readonly Character Target;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveContainItem replaceOxygenObjective;
        private AIObjectiveGetItem getItemObjective;
        private float treatmentTimer;
        private Hull safeHull;
        private float findHullTimer;
        private bool ignoreOxygen;
        private readonly float findHullInterval = 1.0f;
        private bool performedCpr;

        public AIObjectiveRescue(Character character, Character targetCharacter, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            if (targetCharacter == null)
            {
                string errorMsg = $"Attempted to create a Rescue objective with no target!\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(character.Name + ": " + errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("AIObjectiveRescue:ctor:targetnull", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                Abandon = true;
                return;
            }
            Target = targetCharacter;
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
            if (Target == null || Target.Removed || Target.IsDead)
            {
                Abandon = true;
                return;
            }
            var otherRescuer = Target.SelectedBy;
            if (otherRescuer != null && otherRescuer != character)
            {
                // Someone else is rescuing/holding the target.
                Abandon = otherRescuer.IsPlayer || character.GetSkillLevel("medical") < otherRescuer.GetSkillLevel("medical");
                return;
            }
            if (Target != character)
            {
                if (Target.IsIncapacitated)
                {
                    // Check if the character needs more oxygen
                    if (!ignoreOxygen && character.SelectedCharacter == Target || character.CanInteractWith(Target))
                    {
                        // Replace empty oxygen and welding fuel.
                        if (HumanAIController.HasItem(Target, Tags.HeavyDivingGear, out IEnumerable<Item> suits, requireEquipped: true))
                        {
                            Item suit = suits.FirstOrDefault();
                            if (suit != null)
                            {
                                AIController.UnequipEmptyItems(character, suit);
                                AIController.UnequipContainedItems(character, suit, it => it.HasTag(Tags.WeldingFuel));
                            }
                        }
                        else if (HumanAIController.HasItem(Target, Tags.LightDivingGear, out IEnumerable<Item> masks, requireEquipped: true))
                        {
                            Item mask = masks.FirstOrDefault();
                            if (mask != null)
                            {
                                AIController.UnequipEmptyItems(character, mask);
                                AIController.UnequipContainedItems(character, mask, it => it.HasTag(Tags.WeldingFuel));
                            }
                        }
                        bool ShouldRemoveDivingSuit() => Target.OxygenAvailable < CharacterHealth.InsufficientOxygenThreshold && Target.CurrentHull?.LethalPressure <= 0;
                        if (ShouldRemoveDivingSuit())
                        {
                            suits.ForEach(suit => suit.Drop(character));
                        }
                        else if (suits.Any() && suits.None(s => s.OwnInventory?.AllItems != null && s.OwnInventory.AllItems.Any(it => it.HasTag(Tags.OxygenSource) && it.ConditionPercentage > 0)))
                        {
                            // The target has a suit equipped with an empty oxygen tank.
                            // Can't remove the suit, because the target needs it.
                            // If we happen to have an extra oxygen tank in the inventory, let's swap it.
                            Item spareOxygenTank = FindOxygenTank(Target) ?? FindOxygenTank(character);
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
                                i.HasTag(Tags.OxygenSource) &&
                                i.ConditionPercentage > 1 &&
                                i.FindParentInventory(inv => inv.Owner is Item otherItem && otherItem.HasTag(Tags.DivingGear)) == null,
                                recursive: true);
                        }
                    }
                    if (character.Submarine != null && Target.CurrentHull != null)
                    {
                        if (HumanAIController.GetHullSafety(Target.CurrentHull, Target) < HumanAIController.HULL_SAFETY_THRESHOLD)
                        {
                            // Incapacitated target is not in a safe place -> Move to a safe place first
                            if (character.SelectedCharacter != Target)
                            {
                                if (HumanAIController.VisibleHulls.Contains(Target.CurrentHull) && Target.CurrentHull.DisplayName != null)
                                {
                                    character.Speak(TextManager.GetWithVariables("DialogFoundUnconsciousTarget",
                                        ("[targetname]", Target.Name, FormatCapitals.No),
                                        ("[roomname]", Target.CurrentHull.DisplayName, FormatCapitals.Yes)).Value,
                                        null, 1.0f, $"foundunconscioustarget{Target.Name}".ToIdentifier(), 60.0f);
                                }
                                // Go to the target and select it
                                if (!character.CanInteractWith(Target))
                                {
                                    RemoveSubObjective(ref replaceOxygenObjective);
                                    RemoveSubObjective(ref goToObjective);
                                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(Target, character, objectiveManager)
                                    {
                                        CloseEnough = CloseEnoughToTreat,
                                        DialogueIdentifier = "dialogcannotreachpatient".ToIdentifier(),
                                        TargetName = Target.DisplayName
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
                                    character.SelectCharacter(Target);
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
                                        HullSearchStatus hullSearchStatus = objectiveManager.GetObjective<AIObjectiveFindSafety>().FindBestHull(out Hull potentialSafeHull, HumanAIController.VisibleHulls);
                                        if (hullSearchStatus != HullSearchStatus.Finished) { return; }
                                        safeHull = potentialSafeHull;
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
            }

            if (subObjectives.Any()) { return; }

            if (Target != character && !character.CanInteractWith(Target))
            {
                RemoveSubObjective(ref replaceOxygenObjective);
                RemoveSubObjective(ref goToObjective);
                // Go to the target and select it
                TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(Target, character, objectiveManager)
                {
                    CloseEnough = CloseEnoughToTreat,
                    DialogueIdentifier = "dialogcannotreachpatient".ToIdentifier(),
                    TargetName = Target.DisplayName
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
                if (character != Target && character.SelectedCharacter != Target)
                {
                    if (Target.CurrentHull?.DisplayName != null)
                    {
                        character.Speak(TextManager.GetWithVariables("DialogFoundWoundedTarget",
                            ("[targetname]", Target.Name, FormatCapitals.No),
                            ("[roomname]", Target.CurrentHull.DisplayName, FormatCapitals.Yes)).Value,
                            null, 1.0f, $"foundwoundedtarget{Target.Name}".ToIdentifier(), 60.0f);
                    }
                }
                GiveTreatment(deltaTime);
            }
        }

        private readonly List<Identifier> suitableItemIdentifiers = new List<Identifier>();
        private readonly List<LocalizedString> itemNameList = new List<LocalizedString>();
        private readonly Dictionary<Identifier, float> currentTreatmentSuitabilities = new Dictionary<Identifier, float>();
        private void GiveTreatment(float deltaTime)
        {
            if (Target == null)
            {
                string errorMsg = $"{character.Name}: Attempted to update a Rescue objective with no target!";
                DebugConsole.ThrowError(errorMsg);
                Abandon = true;
                return;
            }

            SteeringManager.Reset();

            if (!Target.IsPlayer)
            {
                // If the target is a bot, don't let it move
                Target.AIController?.SteeringManager?.Reset();
            }
            if (treatmentTimer > 0.0f)
            {
                treatmentTimer -= deltaTime;
                return;
            }
            treatmentTimer = TreatmentDelay;

            float cprSuitability = Target.Oxygen < 0.0f ? -Target.Oxygen * 100.0f : 0.0f;

            //find which treatments are the most suitable to treat the character's current condition
            Target.CharacterHealth.GetSuitableTreatments(currentTreatmentSuitabilities, user: character, normalize: false, predictFutureDuration: 10.0f);

            //check if we already have a suitable treatment for any of the afflictions
            foreach (Affliction affliction in GetSortedAfflictions(Target))
            {
                if (affliction == null) { throw new Exception("Affliction was null"); }
                if (affliction.Prefab == null) { throw new Exception("Affliction prefab was null"); }
                float bestSuitability = 0.0f;
                Item bestItem = null;
                foreach (KeyValuePair<Identifier, float> treatmentSuitability in affliction.Prefab.TreatmentSuitabilities)
                {
                    if (currentTreatmentSuitabilities.ContainsKey(treatmentSuitability.Key) && 
                        currentTreatmentSuitabilities[treatmentSuitability.Key] > bestSuitability)
                    {
                        Item matchingItem = character.Inventory.FindItemByIdentifier(treatmentSuitability.Key, true);
                        //allow taking items from the target's inventory too if the target is unconscious
                        if (matchingItem == null && Target.IsIncapacitated)
                        {
                            matchingItem ??= Target.Inventory?.FindItemByIdentifier(treatmentSuitability.Key, true);
                        }
                        if (matchingItem != null) 
                        {
                            bestItem = matchingItem;
                            bestSuitability = currentTreatmentSuitabilities[treatmentSuitability.Key];
                        }
                    }
                }
                if (bestItem != null)
                {
                    if (Target != character) { character.SelectCharacter(Target); }
                    ApplyTreatment(affliction, bestItem);
                    //wait a bit longer after applying a treatment to wait for potential side-effects to manifest
                    treatmentTimer = TreatmentDelay * 4;
                    return;
                }
            }
            // Find treatments outside of own inventory only if inside the own sub.
            if (character.Submarine != null && character.Submarine.TeamID == character.TeamID)
            {
                //didn't have any suitable treatments available, try to find some medical items
                if (currentTreatmentSuitabilities.Any(s => s.Value > cprSuitability))
                {
                    itemNameList.Clear();
                    suitableItemIdentifiers.Clear();
                    foreach (KeyValuePair<Identifier, float> treatmentSuitability in currentTreatmentSuitabilities)
                    {
                        if (treatmentSuitability.Value <= cprSuitability) { continue; }
                        if (ItemPrefab.Prefabs.TryGet(treatmentSuitability.Key, out ItemPrefab itemPrefab))
                        {
                            if (Item.ItemList.None(it => it.Prefab.Identifier == treatmentSuitability.Key)) { continue; }
                            suitableItemIdentifiers.Add(itemPrefab.Identifier);
                            //only list the first 4 items
                            if (itemNameList.Count < 4)
                            {
                                itemNameList.Add(itemPrefab.Name);
                            }
                        }
                    }
                    if (itemNameList.Any())
                    {
                        LocalizedString itemListStr = "";
                        if (itemNameList.Count == 1)
                        {
                            itemListStr = itemNameList[0];
                        }
                        else if (itemNameList.Count == 2)
                        {
                            //[treatment1] or [treatment2]
                            itemListStr = TextManager.GetWithVariables(
                                "DialogRequiredTreatmentOptionsLast",
                                ("[treatment1]", itemNameList[0]),
                                ("[treatment2]", itemNameList[1]));
                        }
                        else
                        {
                            //[treatment1], [treatment2], [treatment3] ... or [treatmentx]
                            itemListStr = TextManager.GetWithVariables(
                                "DialogRequiredTreatmentOptionsFirst",
                                ("[treatment1]", itemNameList[0]),
                                ("[treatment2]", itemNameList[1]));
                            for (int i = 2; i < itemNameList.Count - 1; i++)
                            {
                                itemListStr = TextManager.GetWithVariables(
                                  "DialogRequiredTreatmentOptionsFirst",
                                  ("[treatment1]", itemListStr),
                                  ("[treatment2]", itemNameList[i]));
                            }
                            itemListStr = TextManager.GetWithVariables(
                                "DialogRequiredTreatmentOptionsLast",
                                ("[treatment1]", itemListStr),
                                ("[treatment2]", itemNameList.Last()));
                        }
                        if (Target != character && character.IsOnPlayerTeam)
                        {
                            character.Speak(TextManager.GetWithVariables("DialogListRequiredTreatments",
                                ("[targetname]", Target.Name, FormatCapitals.No),
                                ("[treatmentlist]", itemListStr, FormatCapitals.Yes)).Value,
                                null, 2.0f, $"listrequiredtreatments{Target.Name}".ToIdentifier(), 60.0f);
                        }
                        RemoveSubObjective(ref getItemObjective);
                        TryAddSubObjective(ref getItemObjective,
                            constructor: () => new AIObjectiveGetItem(character, suitableItemIdentifiers.ToArray(), objectiveManager, equip: true, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC),
                            onCompleted: () => RemoveSubObjective(ref getItemObjective),
                            onAbandon: () =>
                            {
                                Abandon = true;
                                if (character.IsOnPlayerTeam)
                                {
                                    SpeakCannotTreat();
                                }
                            });
                    }
                    else if (cprSuitability <= 0)
                    {
                        Abandon = true;
                        SpeakCannotTreat();
                    }
                }
            }
            else if (!Target.IsUnconscious)
            {
                Abandon = true;
                //no suitable treatments found, not inside our own sub (= can't search for more treatments), the target isn't unconscious (= can't give CPR)
                SpeakCannotTreat();
                return;
            }
            if (character != Target)
            {
                if (cprSuitability > 0.0f)
                {
                    character.SelectCharacter(Target);
                    character.AnimController.Anim = AnimController.Animation.CPR;
                    performedCpr = true;
                }
                else
                {
                    character.DeselectCharacter();
                }
            }
        }

        private void SpeakCannotTreat()
        {
            LocalizedString msg = character == Target ?
                TextManager.Get("dialogcannottreatself") :
                TextManager.GetWithVariable("dialogcannottreatpatient", "[name]", Target.DisplayName, FormatCapitals.No);
            character.Speak(msg.Value, identifier: "cannottreatpatient".ToIdentifier(), minDurationBetweenSimilar: 20.0f);
        }

        private void ApplyTreatment(Affliction affliction, Item item)
        {
            item.ApplyTreatment(character, Target, Target.CharacterHealth.GetAfflictionLimb(affliction));
        }

        protected override bool CheckObjectiveSpecific()
        {
            bool isCompleted = AIObjectiveRescueAll.GetVitalityFactor(Target) >= AIObjectiveRescueAll.GetVitalityThreshold(objectiveManager, character, Target);
            if (isCompleted && Target != character && character.IsOnPlayerTeam)
            {
                string textTag = performedCpr ? "DialogTargetResuscitated" : "DialogTargetHealed";
                string message = TextManager.GetWithVariable(textTag, "[targetname]", Target.Name)?.Value;
                character.Speak(message, delay: 1.0f, identifier: $"targethealed{Target.Name}".ToIdentifier(), minDurationBetweenSimilar: 60.0f);
            }
            return isCompleted;
        }

        protected override float GetPriority()
        {
            if (Target == null) { Abandon = true; }
            if (!IsAllowed) { HandleNonAllowed(); }
            if (Abandon)
            {
                return Priority;
            }
            if (character.CurrentHull != null)
            {
                if (Character.CharacterList.Any(c => c.CurrentHull == Target.CurrentHull && !HumanAIController.IsFriendly(character, c) && HumanAIController.IsActive(c)))
                {
                    // Don't go into rooms that have enemies
                    Priority = 0;
                    Abandon = true;
                    return Priority;
                }
            }
            float horizontalDistance = Math.Abs(character.WorldPosition.X - Target.WorldPosition.X);
            float verticalDistance = Math.Abs(character.WorldPosition.Y - Target.WorldPosition.Y);
            if (character.Submarine?.Info is { IsRuin: false })
            {
                verticalDistance *= 2;
            }
            float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, horizontalDistance + verticalDistance));
            if (character.CurrentHull != null && Target.CurrentHull == character.CurrentHull)
            {
                distanceFactor = 1;
            }
            float vitalityFactor = 1 - AIObjectiveRescueAll.GetVitalityFactor(Target) / 100;
            float devotion = CumulatedDevotion / 100;
            Priority = MathHelper.Lerp(0, AIObjectiveManager.EmergencyObjectivePriority, MathHelper.Clamp(devotion + (vitalityFactor * distanceFactor * PriorityModifier), 0, 1));
            return Priority;
        }

        public static IEnumerable<Affliction> GetSortedAfflictions(Character character, bool excludeBuffs = true) => CharacterHealth.SortAfflictionsBySeverity(character.CharacterHealth.GetAllAfflictions(), excludeBuffs);

        public override void Reset()
        {
            base.Reset();
            goToObjective = null;
            getItemObjective = null;
            replaceOxygenObjective = null;
            safeHull = null;
            ignoreOxygen = false;
            character.SelectedCharacter = null;
        }

        public override void OnDeselected()
        {
            character.SelectedCharacter = null;
            base.OnDeselected();
        }
    }
}
