using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace Barotrauma
{
    class AIObjectiveRescue : AIObjective
    {
        private readonly Character targetCharacter;

        private AIObjectiveGoTo goToObjective;

        public override bool CanBeCompleted
        {
            get
            {
                if (targetCharacter.Removed) return false;
                if (goToObjective != null && !goToObjective.CanBeCompleted) return false;

                return true;
            }
        }

        public AIObjectiveRescue(Character character, Character targetCharacter)
            : base (character, "")
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
            
            if (!character.CanInteractWith(targetCharacter))
            {
                AddSubObjective(goToObjective = new AIObjectiveGoTo(targetCharacter, character));
            }
            else
            {
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
            //TODO: reimplement AIObjectiveRescue using SuitableTreatments from the afflictions the patient has

            //target is bleeding -> try to fix it before doing anything else
            /*if (targetCharacter.Bleeding > 0.5f)
            {
                Item bandage = character.Inventory.FindItem("bandage");
                if (bandage != null)
                {
                    if (bandage.Condition <= 0.0f)
                    {
                        bandage.Drop();
                    }
                    else
                    {
                        bandage.Use(deltaTime, character);
                    }
                    return;
                }

                Item syringe = character.Inventory.FindItem("Medical Syringe");
                if (syringe == null)
                {
                    AddSubObjective(new AIObjectiveGetItem(character, "Medical Syringe", true));
                    return;
                }

                var containItem = new AIObjectiveContainItem(character, "Fibrinozine", syringe.GetComponent<ItemContainer>());
                if (!containItem.IsCompleted())
                {
                    AddSubObjective(containItem);
                    return;
                }

                syringe.Use(deltaTime, character);
            }*/

            character.AnimController.Anim = AnimController.Animation.CPR;
        }

        /*private bool CheckRequiredItems()
        {


            if (targetCharacter.Bleeding > 0.5f)
            {
                var getItem = new AIObjectiveContainItem(character, new string[] { "Fibrinozine", "Bandage", "stopbleeding" }, syringe.GetComponent<ItemContainer>());
                if (!getItem.IsCompleted())
                {
                    AddSubObjective(getItem);
                    return false;
                }
            }
            if (targetCharacter.Health < targetCharacter.MaxHealth * 0.5f)
            {
                var getItem = new AIObjectiveContainItem(character, new string[] { "Corrigodone", "heal" }, syringe.GetComponent<ItemContainer>());
                if (!getItem.IsCompleted())
                {
                    AddSubObjective(getItem);
                    return false;
                }
            }

            return true;
        }*/

        public override bool IsCompleted()
        {
            return !targetCharacter.IsUnconscious || targetCharacter.IsDead;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (targetCharacter.AnimController.CurrentHull == null) return 0.0f;
            float distance = Vector2.DistanceSquared(character.WorldPosition, targetCharacter.WorldPosition);
            return targetCharacter.IsDead ? 1000.0f / distance : 10000.0f / distance;
        }
    }
}
