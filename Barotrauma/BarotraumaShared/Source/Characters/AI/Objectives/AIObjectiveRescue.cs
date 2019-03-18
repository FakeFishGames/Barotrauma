using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescue : AIObjective
    {
        const float TreatmentDelay = 0.5f;

        private readonly Character targetCharacter;

        private AIObjectiveGoTo goToObjective;

        private float treatmentTimer;

        public override bool CanBeCompleted
        {
            get
            {
                if (targetCharacter.Removed || 
                    targetCharacter.IsDead || 
                    targetCharacter.Vitality / targetCharacter.MaxVitality > AIObjectiveRescueAll.VitalityThreshold)
                {
                    return false;
                }
                if (goToObjective != null && !goToObjective.CanBeCompleted) { return false; }

                return true;
            }
        }

        public AIObjectiveRescue(Character character, Character targetCharacter)
            : base(character, "")
        {
            Debug.Assert(character != targetCharacter);
            this.targetCharacter = targetCharacter;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveRescue rescueObjective = otherObjective as AIObjectiveRescue;
            return rescueObjective != null && rescueObjective.targetCharacter == targetCharacter;
        }

        protected override void Act(float deltaTime)
        {
            //target in water -> move to a dry place first
            if (targetCharacter.AnimController.InWater)
            {
                if (character.SelectedCharacter != targetCharacter)
                {
                    if (!character.CanInteractWith(targetCharacter))
                    {
                        AddSubObjective(goToObjective = new AIObjectiveGoTo(targetCharacter, character));
                    }
                    else
                    {
                        character.SelectCharacter(targetCharacter);
                    }
                }
                else
                {
                    AddSubObjective(new AIObjectiveFindSafety(character));
                }
                return;
            }

            //target not in water -> we can start applying treatment
            if (!character.CanInteractWith(targetCharacter))
            {
                AddSubObjective(goToObjective = new AIObjectiveGoTo(targetCharacter, character));
            }
            else
            {
                if (character.SelectedCharacter == null)
                {
                    character?.Speak(TextManager.Get("DialogFoundUnconsciousTarget")
                        .Replace("[targetname]", targetCharacter.Name).Replace("[roomname]", character.CurrentHull.RoomName),
                        null, 1.0f,
                        "foundunconscioustarget" + targetCharacter.Name, 60.0f);
                }

                character.SelectCharacter(targetCharacter);
                GiveTreatment(deltaTime);
            }
        }

        protected override bool ShouldInterruptSubObjective(AIObjective subObjective)
        {
            if (subObjective is AIObjectiveFindSafety)
            {
                if (character.SelectedCharacter != targetCharacter) return true;
                if (character.AnimController.InWater || targetCharacter.AnimController.InWater) return false;

                foreach (Limb limb in targetCharacter.AnimController.Limbs)
                {
                    if (!Submarine.RectContains(targetCharacter.CurrentHull.WorldRect, limb.WorldPosition)) return false;
                }

                return !character.AnimController.InWater && !targetCharacter.AnimController.InWater &&
                    AIObjectiveFindSafety.GetHullSafety(character.CurrentHull, character) > 50.0f;
            }

            return false;
        }

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
                        Item matchingItem = character.Inventory.FindItemByIdentifier(treatmentSuitability.Key);
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
                    if (itemNameList.Count >= 4) break;
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
                    character?.Speak(TextManager.Get("DialogListRequiredTreatments")
                        .Replace("[targetname]", targetCharacter.Name)
                        .Replace("[treatmentlist]", itemListStr),
                        null, 2.0f, "listrequiredtreatments" + targetCharacter.Name, 60.0f);
                }

                character.DeselectCharacter();
                AddSubObjective(new AIObjectiveGetItem(character, suitableItemIdentifiers.ToArray(), true));
            }

            character.AnimController.Anim = AnimController.Animation.CPR;
        }

        private void ApplyTreatment(Affliction affliction, Item item)
        {
            var targetLimb = targetCharacter.CharacterHealth.GetAfflictionLimb(affliction);

            bool remove = false;
            foreach (ItemComponent ic in item.components)
            {
                if (!ic.HasRequiredContainedItems(addMessage: false)) continue;
#if CLIENT
                ic.PlaySound(ActionType.OnUse, character.WorldPosition, character);
#endif
                ic.WasUsed = true;
                ic.ApplyStatusEffects(ActionType.OnUse, 1.0f, targetCharacter, targetLimb);
                if (ic.DeleteOnUse) remove = true;
            }

            if (remove)
            {
                Entity.Spawner?.AddToRemoveQueue(item);
            }
        }

        public override bool IsCompleted()
        {
            bool isCompleted = 
                targetCharacter.Vitality / targetCharacter.MaxVitality > AIObjectiveRescueAll.VitalityThreshold;

            if (isCompleted)
            {
                character?.Speak(TextManager.Get("DialogTargetHealed").Replace("[targetname]", targetCharacter.Name),
                    null, 1.0f, "targethealed" + targetCharacter.Name, 60.0f);
            }

            return isCompleted || targetCharacter.IsDead;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (targetCharacter.AnimController.CurrentHull == null || targetCharacter.IsDead) { return 0.0f; }

            Vector2 diff = targetCharacter.WorldPosition - character.WorldPosition;
            float distance = Math.Abs(diff.X) + Math.Abs(diff.Y);

            float vitalityFactor = (targetCharacter.MaxVitality - targetCharacter.Vitality) / targetCharacter.MaxVitality;

            return 1000.0f * vitalityFactor / distance;
        }
    }
}
