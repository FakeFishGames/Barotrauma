using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveExtinguishFire : AIObjective
    {
        public override string DebugTag => "extinguish fire";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        private Hull targetHull;

        private AIObjectiveGetItem getExtinguisherObjective;

        private AIObjectiveGoTo gotoObjective;

        private float useExtinquisherTimer;

        public AIObjectiveExtinguishFire(Character character, Hull targetHull, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetHull = targetHull;
        }

        public override float GetPriority()
        {
            if (gotoObjective != null && !gotoObjective.CanBeCompleted) { return 0; }
            if (Character.CharacterList.Any(c => c.CurrentHull == targetHull && !HumanAIController.IsFriendly(c))) { return 0; }
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
            float dist = Math.Abs(character.WorldPosition.X - targetHull.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - targetHull.WorldPosition.Y) * 2.0f;
            float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 10000, dist));
            float severity = AIObjectiveExtinguishFires.GetFireSeverity(targetHull);
            float severityFactor = MathHelper.Lerp(0, 1, severity / 100);
            float devotion = Math.Max(Priority, 10) / 100;
            return MathHelper.Lerp(0, 100, MathHelper.Clamp(devotion + severityFactor * distanceFactor, 0, 1));
        }

        public override bool IsCompleted()
        {
            return targetHull.FireSources.Count == 0;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            var otherExtinguishFire = otherObjective as AIObjectiveExtinguishFire;
            return otherExtinguishFire != null && otherExtinguishFire.targetHull == targetHull;
        }

        public override bool CanBeCompleted
        {
            get { return getExtinguisherObjective == null || getExtinguisherObjective.IsCompleted() || getExtinguisherObjective.CanBeCompleted; }
        }

        protected override void Act(float deltaTime)
        {
            var extinguisherItem = character.Inventory.FindItemByIdentifier("extinguisher") ?? character.Inventory.FindItemByTag("extinguisher");
            if (extinguisherItem == null || extinguisherItem.Condition <= 0.0f || !character.HasEquippedItem(extinguisherItem))
            {
                if (getExtinguisherObjective == null)
                {
                    character.Speak(TextManager.Get("DialogFindExtinguisher"), null, 2.0f, "findextinguisher", 30.0f);
                    getExtinguisherObjective = new AIObjectiveGetItem(character, "extinguisher", objectiveManager, equip: true);
                }
                else
                {
                    getExtinguisherObjective.TryComplete(deltaTime);
                }

                return;
            }

            var extinguisher = extinguisherItem.GetComponent<RepairTool>();
            if (extinguisher == null)
            {
                DebugConsole.ThrowError("AIObjectiveExtinguishFire failed - the item \"" + extinguisherItem + "\" has no RepairTool component but is tagged as an extinguisher");
                return;
            }

            foreach (FireSource fs in targetHull.FireSources)
            {
                bool inRange = fs.IsInDamageRange(character, MathHelper.Clamp(fs.DamageRange * 1.5f, extinguisher.Range * 0.5f, extinguisher.Range));
                bool move = !inRange;
                if (inRange || useExtinquisherTimer > 0.0f)
                {
                    useExtinquisherTimer += deltaTime;
                    if (useExtinquisherTimer > 2.0f)
                    {
                        useExtinquisherTimer = 0.0f;
                    }
                    character.AIController.SteeringManager.Reset();
                    character.CursorPosition = fs.Position;
                    if (extinguisher.Item.RequireAimToUse)
                    {
                        character.SetInput(InputType.Aim, false, true);
                    }
                    Limb sightLimb = null;
                    if (character.Inventory.IsInLimbSlot(extinguisherItem, InvSlotType.RightHand))
                    {
                        sightLimb = character.AnimController.GetLimb(LimbType.RightHand);
                    }
                    else if (character.Inventory.IsInLimbSlot(extinguisherItem, InvSlotType.LeftHand))
                    {
                        sightLimb = character.AnimController.GetLimb(LimbType.LeftHand);
                    }
                    if (!character.CanSeeTarget(fs, sightLimb))
                    {
                        move = true;
                    }
                    else
                    {
                        move = false;
                        extinguisher.Use(deltaTime, character);
                        if (!targetHull.FireSources.Contains(fs))
                        {
                            character.Speak(TextManager.Get("DialogPutOutFire").Replace("[roomname]", targetHull.Name), null, 0, "putoutfire", 10.0f);
                        }
                    }
                }
                if (move)
                {
                    //go to the first firesource
                    if (gotoObjective == null || !gotoObjective.CanBeCompleted || gotoObjective.IsCompleted())
                    {
                        gotoObjective = new AIObjectiveGoTo(ConvertUnits.ToSimUnits(fs.Position), character, objectiveManager);
                    }
                    else
                    {
                        gotoObjective.TryComplete(deltaTime);
                    }
                }
                break;
            }
        }
    }
}
