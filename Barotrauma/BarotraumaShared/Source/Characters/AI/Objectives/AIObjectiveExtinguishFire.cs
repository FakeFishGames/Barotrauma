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

        public AIObjectiveExtinguishFire(Character character, Hull targetHull) : base(character, "")
        {
            this.targetHull = targetHull;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (gotoObjective != null && !gotoObjective.CanBeCompleted) { return 0; }
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
            float dist = Math.Abs(character.WorldPosition.X - targetHull.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - targetHull.WorldPosition.Y) * 2.0f;
            float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 10000, dist));
            float severityFactor = MathHelper.Lerp(0, 1, MathHelper.Clamp(targetHull.FireSources.Sum(fs => fs.Size.X) / targetHull.Size.X, 0, 1));
            return MathHelper.Clamp(Priority * severityFactor * distanceFactor, 0, 100);
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
                    getExtinguisherObjective = new AIObjectiveGetItem(character, "extinguisher", true);
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
                // TODO: check if in the same room?
                bool inRange = fs.IsInDamageRange(character, MathHelper.Clamp(fs.DamageRange * 1.5f, extinguisher.Range * 0.5f, extinguisher.Range));
                if (!character.IsClimbing && (inRange || useExtinquisherTimer > 0.0f))
                {
                    useExtinquisherTimer += deltaTime;
                    if (useExtinquisherTimer > 2.0f) useExtinquisherTimer = 0.0f;

                    character.CursorPosition = fs.Position;
                    character.SetInput(InputType.Aim, false, true);
                    character.AIController.SteeringManager.Reset();
                    extinguisher.Use(deltaTime, character);

                    if (!targetHull.FireSources.Contains(fs))
                    {
                        character.Speak(TextManager.Get("DialogPutOutFire").Replace("[roomname]", targetHull.Name), null, 0, "putoutfire", 10.0f);
                    }
                    return;
                }
                else
                {
                    //go to the first firesource
                    if (gotoObjective == null || !gotoObjective.CanBeCompleted || gotoObjective.IsCompleted())
                    {
                        gotoObjective = new AIObjectiveGoTo(ConvertUnits.ToSimUnits(fs.Position), character);
                    }
                    else
                    {
                        gotoObjective.TryComplete(deltaTime);
                    }
                    break;
                }
            }
        }
    }
}
