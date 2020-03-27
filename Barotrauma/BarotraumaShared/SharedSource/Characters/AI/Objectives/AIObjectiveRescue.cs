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

        const float TreatmentDelay = 0.5f;

        const float CloseEnoughToTreat = 100.0f;

        private readonly Character targetCharacter;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveGetItem getItemObjective;
        private float treatmentTimer;
        private Hull safeHull;
        private float findHullTimer;
        private readonly float findHullInterval = 1.0f;

        public AIObjectiveRescue(Character character, Character targetCharacter, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            if (targetCharacter == null)
            {
                string errorMsg = $"{character.Name}: Attempted to create a Rescue objective with no target!\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("AIObjectiveRescue:ctor:targetnull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                Abandon = true;
                return;
            }
            this.targetCharacter = targetCharacter;
        }
        
        protected override void Act(float deltaTime)
        {
            if (character.LockHands || targetCharacter == null || targetCharacter.CurrentHull == null || targetCharacter.Removed || targetCharacter.IsDead)
            {
                Abandon = true;
                return;
            }
            if (targetCharacter.SelectedBy != null && targetCharacter.SelectedBy != character)
            {
                var otherCharacter = character.SelectedBy;
                if (otherCharacter != null)
                {
                    // Someone else is rescuing/holding the target.
                    Abandon = otherCharacter.IsPlayer || character.GetSkillLevel("medical") < otherCharacter.GetSkillLevel("medical");
                }
            }

            if (targetCharacter != character)
            {
                // Incapacitated target is not in a safe place -> Move to a safe place first
                if (targetCharacter.IsIncapacitated && HumanAIController.GetHullSafety(targetCharacter.CurrentHull, targetCharacter) < HumanAIController.HULL_SAFETY_THRESHOLD)
                {
                    if (character.SelectedCharacter != targetCharacter)
                    {
                        character.Speak(TextManager.GetWithVariables("DialogFoundUnconsciousTarget", new string[2] { "[targetname]", "[roomname]" },
                            new string[2] { targetCharacter.Name, targetCharacter.CurrentHull.DisplayName }, new bool[2] { false, true }),
                            null, 1.0f, "foundunconscioustarget" + targetCharacter.Name, 60.0f);

                        // Go to the target and select it
                        if (!character.CanInteractWith(targetCharacter))
                        {
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

            if (subObjectives.Any()) { return; }

            if (targetCharacter != character && !character.CanInteractWith(targetCharacter))
            {
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
                    character.Speak(TextManager.GetWithVariables("DialogFoundWoundedTarget", new string[2] { "[targetname]", "[roomname]" },
                        new string[2] { targetCharacter.Name, targetCharacter.CurrentHull.DisplayName }, new bool[2] { false, true }),
                        null, 1.0f, "foundwoundedtarget" + targetCharacter.Name, 60.0f);

                    character.SelectCharacter(targetCharacter);
                }
                GiveTreatment(deltaTime);
            }
        }

        private readonly List<string> suitableItemIdentifiers = new List<string>();
        private readonly List<string> itemNameList = new List<string>();
        private Dictionary<string, float> currentTreatmentSuitabilities = new Dictionary<string, float>();
        private void GiveTreatment(float deltaTime)
        {
            if (!targetCharacter.IsPlayer)
            {
                // If the target is a bot, don't let it move
                targetCharacter.AIController?.SteeringManager.Reset();
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
                        constructor: () => new AIObjectiveGetItem(character, suitableItemIdentifiers.ToArray(), objectiveManager, equip: true),
                        onCompleted: () => RemoveSubObjective(ref getItemObjective),
                        onAbandon: () => RemoveSubObjective(ref getItemObjective));
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
                ic.ApplyStatusEffects(ActionType.OnUse, 1.0f, targetCharacter, targetLimb);
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
            if (targetCharacter == null || targetCharacter.CurrentHull == null || targetCharacter.Removed || targetCharacter.IsDead)
            {
                Priority = 0;
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
    }
}
