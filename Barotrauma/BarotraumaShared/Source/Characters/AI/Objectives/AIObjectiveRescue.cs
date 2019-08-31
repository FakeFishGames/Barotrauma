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

        const float TreatmentDelay = 0.5f;

        private readonly Character targetCharacter;

        private AIObjectiveGoTo goToObjective;
        private float treatmentTimer;
        private Hull safeHull;

        public AIObjectiveRescue(Character character, Character targetCharacter, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            if (targetCharacter == null)
            {
                string errorMsg = $"{character.Name}: Attempted to create a Rescue objective with no target!\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("AIObjectiveRescue:ctor:targetnull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                abandon = true;
                return;
            }

            if (targetCharacter == character)
            {
                // TODO: enable healing self too
                abandon = true;
                return;
            }
            this.targetCharacter = targetCharacter;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveRescue rescueObjective = otherObjective as AIObjectiveRescue;
            return rescueObjective != null && rescueObjective.targetCharacter == targetCharacter;
        }
        
        protected override void Act(float deltaTime)
        {
            if (targetCharacter == null || targetCharacter.Removed)
            {
                return;
            }

            // Unconcious target is not in a safe place -> Move to a safe place first
            if (targetCharacter.IsUnconscious && HumanAIController.GetHullSafety(targetCharacter.CurrentHull, targetCharacter) < HumanAIController.HULL_SAFETY_THRESHOLD)
            {
                if (character.SelectedCharacter != targetCharacter)
                {   
                    character.Speak(TextManager.GetWithVariables("DialogFoundUnconsciousTarget", new string[2] { "[targetname]", "[roomname]" }, 
                        new string[2] { targetCharacter.Name, targetCharacter.CurrentHull.DisplayName }, new bool[2] { false, true }),
                        null, 1.0f, "foundunconscioustarget" + targetCharacter.Name, 60.0f);

                    // Go to the target and select it
                    if (!character.CanInteractWith(targetCharacter))
                    {
                        if (goToObjective != null && goToObjective.Target != targetCharacter)
                        {
                            goToObjective = null;
                        }
                        TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(targetCharacter, character, objectiveManager));
                    }
                    else
                    {
                        character.SelectCharacter(targetCharacter);
                    }
                }
                else
                {
                    // Drag the character into safety
                    if (goToObjective != null && goToObjective.Target == targetCharacter)
                    {
                        goToObjective = null;
                    }
                    if (safeHull == null)
                    {
                        var findSafety = objectiveManager.GetObjective<AIObjectiveFindSafety>();
                        if (findSafety == null)
                        {
                            // Ensure that we have the find safety objective (should always be the case)
                            findSafety = new AIObjectiveFindSafety(character, objectiveManager);
                            objectiveManager.AddObjective(findSafety);
                        }
                        safeHull = findSafety.FindBestHull(HumanAIController.VisibleHulls);
                    }
                    if (character.CurrentHull != safeHull)
                    {
                        TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(safeHull, character, objectiveManager));
                    }
                }
            }

            if (subObjectives.Any()) { return; }

            if (!character.CanInteractWith(targetCharacter))
            {
                // Go to the target and select it
                TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(targetCharacter, character, objectiveManager));
            }
            else
            {
                // We can start applying treatment
                if (character.SelectedCharacter != targetCharacter)
                {                
                    character.Speak(TextManager.GetWithVariables("DialogFoundWoundedTarget", new string[2] { "[targetname]", "[roomname]" },
                        new string[2] { targetCharacter.Name, targetCharacter.CurrentHull.DisplayName }, new bool[2] { false, true }),
                        null, 1.0f, "foundwoundedtarget" + targetCharacter.Name, 60.0f);

                    character.SelectCharacter(targetCharacter);
                }
                GiveTreatment(deltaTime);
            }
        }

        // TODO: consider optimizing a bit
        private void GiveTreatment(float deltaTime)
        {
            if (treatmentTimer > 0.0f)
            {
                treatmentTimer -= deltaTime;
            }
            treatmentTimer = TreatmentDelay;

            var allAfflictions = targetCharacter.CharacterHealth.GetAllAfflictions()
                .Where(a => a.GetVitalityDecrease(targetCharacter.CharacterHealth) > 0)
                .ToList();

            allAfflictions.Sort((a1, a2) =>
            {
                return Math.Sign(a2.GetVitalityDecrease(targetCharacter.CharacterHealth) - a1.GetVitalityDecrease(targetCharacter.CharacterHealth));
            });
            //check if we already have a suitable treatment for any of the afflictions
            foreach (Affliction affliction in allAfflictions)
            {
                foreach (KeyValuePair<string, float> treatmentSuitability in affliction.Prefab.TreatmentSuitability)
                {
                    if (treatmentSuitability.Value > 0.0f)
                    {
                        Item matchingItem = character.Inventory.FindItemByIdentifier(treatmentSuitability.Key, true);
                        if (matchingItem == null) { continue; }
                        ApplyTreatment(affliction, matchingItem);
                        return;
                    }
                }
            }
            //didn't have any suitable treatments available, try to find some medical items
            HashSet<string> suitableItemIdentifiers = new HashSet<string>();
            foreach (Affliction affliction in allAfflictions)
            {
                foreach (KeyValuePair<string, float> treatmentSuitability in affliction.Prefab.TreatmentSuitability)
                {
                    if (treatmentSuitability.Value > 0.0f)
                    {
                        suitableItemIdentifiers.Add(treatmentSuitability.Key);
                    }
                }
            }
            if (suitableItemIdentifiers.Count > 0)
            {
                List<string> itemNameList = new List<string>();
                foreach (string itemIdentifier in suitableItemIdentifiers)
                {
                    if (MapEntityPrefab.Find(null, itemIdentifier, showErrorMessages: false) is ItemPrefab itemPrefab)
                    {
                        itemNameList.Add(itemPrefab.Name);
                    }
                    //only list the first 4 items
                    if (itemNameList.Count >= 4) { break; }
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
                    

                    character.Speak(TextManager.GetWithVariables("DialogListRequiredTreatments", new string[2] { "[targetname]", "[treatmentlist]" },
                        new string[2] { targetCharacter.Name, itemListStr }, new bool[2] { false, true }),
                        null, 2.0f, "listrequiredtreatments" + targetCharacter.Name, 60.0f);
                }
                character.DeselectCharacter();
                // TODO: use TryAdd?
                AddSubObjective(new AIObjectiveGetItem(character, suitableItemIdentifiers.ToArray(), objectiveManager, equip: true));
            }
            character.AnimController.Anim = AnimController.Animation.CPR;
        }

        private void ApplyTreatment(Affliction affliction, Item item)
        {
            var targetLimb = targetCharacter.CharacterHealth.GetAfflictionLimb(affliction);
            bool remove = false;
            foreach (ItemComponent ic in item.Components)
            {
                if (!ic.HasRequiredContainedItems(user: character, addMessage: false)) { continue; }
#if CLIENT
                ic.PlaySound(ActionType.OnUse, character.WorldPosition, character);
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
            if (targetCharacter == null || targetCharacter.Removed)
            {
                return true;
            }
            bool isCompleted = targetCharacter.Bleeding <= 0 && targetCharacter.Vitality / targetCharacter.MaxVitality > AIObjectiveRescueAll.GetVitalityThreshold(objectiveManager);
            if (isCompleted)
            {                
                character.Speak(TextManager.GetWithVariable("DialogTargetHealed", "[targetname]", targetCharacter.Name),
                    null, 1.0f, "targethealed" + targetCharacter.Name, 60.0f);
            }
            return isCompleted || targetCharacter.IsDead;
        }

        public override float GetPriority()
        {
            if (targetCharacter == null) { return 0; }
            if (targetCharacter.CurrentHull == null || targetCharacter.Removed || targetCharacter.IsDead)
            {
                abandon = true;
                return 0;
            }
            // Don't go into rooms that have enemies
            if (Character.CharacterList.Any(c => c.CurrentHull == targetCharacter.CurrentHull && !HumanAIController.IsFriendly(c)))
            {
                abandon = true;
                return 0;
            }
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
            float dist = Math.Abs(character.WorldPosition.X - targetCharacter.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - targetCharacter.WorldPosition.Y) * 2.0f;
            float distanceFactor = MathHelper.Lerp(1, 0.5f, MathUtils.InverseLerp(0, 10000, dist));
            float vitalityFactor = AIObjectiveRescueAll.GetVitalityFactor(targetCharacter);
            float devotion = Math.Min(Priority, 10) / 100;
            return MathHelper.Lerp(0, 100, MathHelper.Clamp(devotion + vitalityFactor * distanceFactor, 0, 1));
        }
    }
}
