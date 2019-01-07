using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveExtinguishFire : AIObjective
    {
        private Hull targetHull;

        private AIObjectiveGetItem getExtinguisherObjective;

        private AIObjectiveGoTo gotoObjective;

        private float useExtinquisherTimer;

        public AIObjectiveExtinguishFire(Character character, Hull targetHull) :
            base(character, "")
        {
            this.targetHull = targetHull;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            return targetHull.FireSources.Sum(fs => fs.Size.X * 0.1f);
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
            get { return getExtinguisherObjective == null || getExtinguisherObjective.CanBeCompleted; }
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

            foreach (FireSource fs in targetHull.FireSources.ToList())
            {
                if (fs.IsInDamageRange(character, MathHelper.Clamp(fs.DamageRange * 1.5f, extinguisher.Range * 0.5f, extinguisher.Range)) || useExtinquisherTimer > 0.0f)
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
            }

            foreach (FireSource fs in targetHull.FireSources)
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
