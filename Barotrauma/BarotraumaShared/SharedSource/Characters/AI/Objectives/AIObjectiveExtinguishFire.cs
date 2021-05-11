using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveExtinguishFire : AIObjective
    {
        public override string Identifier { get; set; } = "extinguish fire";
        public override bool ForceRun => true;
        public override bool ConcurrentObjectives => true;
        public override bool KeepDivingGearOn => true;

        public override bool AllowInAnySub => true;

        private readonly Hull targetHull;

        private AIObjectiveGetItem getExtinguisherObjective;
        private AIObjectiveGoTo gotoObjective;
        private float useExtinquisherTimer;

        public AIObjectiveExtinguishFire(Character character, Hull targetHull, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetHull = targetHull;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
                return Priority;
            }
            bool isOrder = objectiveManager.HasOrder<AIObjectiveExtinguishFires>();
            if (!isOrder && Character.CharacterList.Any(c => c.CurrentHull == targetHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c)))
            {
                // Don't go into rooms with any enemies, unless it's an order
                Priority = 0;
                Abandon = true;
            }
            else
            {
                float yDist = Math.Abs(character.WorldPosition.Y - targetHull.WorldPosition.Y);
                yDist = yDist > 100 ? yDist * 3 : 0;
                float dist = Math.Abs(character.WorldPosition.X - targetHull.WorldPosition.X) + yDist;
                float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, dist));
                if (targetHull == character.CurrentHull || HumanAIController.VisibleHulls.Contains(targetHull))
                {
                    distanceFactor = 1;
                }
                float severity = AIObjectiveExtinguishFires.GetFireSeverity(targetHull);
                if (severity > 0.5f && !isOrder)
                {
                    // Ignore severe fires unless ordered. (Let the fire drain all the oxygen instead).
                    Priority = 0;
                    Abandon = true;
                }
                else
                {
                    float devotion = CumulatedDevotion / 100;
                    Priority = MathHelper.Lerp(0, 100, MathHelper.Clamp(devotion + (severity * distanceFactor * PriorityModifier), 0, 1));
                }
            }
            return Priority;
        }

        protected override bool CheckObjectiveSpecific() => targetHull.FireSources.None();

        private float sinTime;
        protected override void Act(float deltaTime)
        {
            var extinguisherItem = character.Inventory.FindItemByTag("fireextinguisher");
            if (extinguisherItem == null || extinguisherItem.Condition <= 0.0f || !character.HasEquippedItem(extinguisherItem))
            {
                TryAddSubObjective(ref getExtinguisherObjective, () =>
                {
                    if (character.IsOnPlayerTeam && !character.HasEquippedItem("fireextinguisher", allowBroken: false))
                    {
                        character.Speak(TextManager.Get("DialogFindExtinguisher"), null, 2.0f, "findextinguisher", 30.0f);
                    }
                    var getItemObjective = new AIObjectiveGetItem(character, "fireextinguisher", objectiveManager, equip: true)
                    {
                        AllowStealing = true,
                        // If the item is inside an unsafe hull, decrease the priority
                        GetItemPriority = i => HumanAIController.UnsafeHulls.Contains(i.CurrentHull) ? 0.1f : 1
                    };
                    if (objectiveManager.HasOrder<AIObjectiveExtinguishFires>())
                    {
                        getItemObjective.Abandoned += () => character.Speak(TextManager.Get("dialogcannotfindfireextinguisher"), null, 0.0f, "dialogcannotfindfireextinguisher", 10.0f);
                    };
                    return getItemObjective;
                });
            }
            else
            {
                var extinguisher = extinguisherItem.GetComponent<RepairTool>();
                if (extinguisher == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveExtinguishFire failed - the item \"" + extinguisherItem + "\" has no RepairTool component but is tagged as an extinguisher");
#endif
                    Abandon = true;
                    return;
                }
                foreach (FireSource fs in targetHull.FireSources)
                {
                    float xDist = Math.Abs(character.WorldPosition.X - fs.WorldPosition.X) - fs.DamageRange;
                    float yDist = Math.Abs(character.WorldPosition.Y - fs.WorldPosition.Y);
                    bool inRange = xDist + yDist < extinguisher.Range;
                    // Use the hull position, because the fire x pos is sometimes inside a wall -> the bot can't ever see it and continues running towards the wall.
                    ISpatialEntity lookTarget = character.CurrentHull == targetHull || character.CurrentHull.linkedTo.Contains(targetHull) ? targetHull : fs as ISpatialEntity;
                    bool move = !inRange || !character.CanSeeTarget(lookTarget);
                    if ((inRange && character.CanSeeTarget(lookTarget)) || useExtinquisherTimer > 0)
                    {
                        useExtinquisherTimer += deltaTime;
                        if (useExtinquisherTimer > 2.0f)
                        {
                            useExtinquisherTimer = 0.0f;
                        }
                        // Aim
                        character.CursorPosition = fs.Position;
                        Vector2 fromCharacterToFireSource = fs.WorldPosition - character.WorldPosition;
                        float dist = fromCharacterToFireSource.Length();
                        character.CursorPosition += VectorExtensions.Forward(extinguisherItem.body.TransformedRotation + (float)Math.Sin(sinTime) / 2, dist / 2);
                        if (extinguisherItem.RequireAimToUse)
                        {
                            character.SetInput(InputType.Aim, false, true);
                            sinTime += deltaTime * 10;
                        }
                        character.SetInput(extinguisherItem.IsShootable ? InputType.Shoot : InputType.Use, false, true);
                        extinguisher.Use(deltaTime, character);
                        if (!targetHull.FireSources.Contains(fs))
                        {
                            character.Speak(TextManager.GetWithVariable("DialogPutOutFire", "[roomname]", targetHull.DisplayName, true), null, 0, "putoutfire", 10.0f);
                        }
                    }
                    if (move)
                    {
                        //go to the first firesource
                        if (TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(fs, character, objectiveManager, closeEnough: Math.Max(fs.DamageRange, extinguisher.Range * 0.7f))
                            {
                                DialogueIdentifier = "dialogcannotreachfire",
                                TargetName = fs.Hull.DisplayName
                            }, 
                                onAbandon: () =>  Abandon = true, 
                                onCompleted: () => RemoveSubObjective(ref gotoObjective)))
                        {
                            gotoObjective.requiredCondition = () => targetHull == null || character.CanSeeTarget(targetHull);
                        }
                    }
                    else
                    {
                        character.AIController.SteeringManager.Reset();
                    }
                    break;
                }
            }
        }

        public override void Reset()
        {
            base.Reset();
            getExtinguisherObjective = null;
            gotoObjective = null;
            useExtinquisherTimer = 0;
            sinTime = 0;
        }
    }
}
