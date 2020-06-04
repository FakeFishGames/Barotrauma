using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveExtinguishFire : AIObjective
    {
        public override string DebugTag => "extinguish fire";
        public override bool ForceRun => true;
        public override bool ConcurrentObjectives => true;
        public override bool KeepDivingGearOn => true;

        private readonly Hull targetHull;

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
            if (!IsAllowed)
            {
                Priority = 0;
                return Priority;
            }
            if (!objectiveManager.IsCurrentOrder<AIObjectiveExtinguishFires>() 
                && Character.CharacterList.Any(c => c.CurrentHull == targetHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c)))
            {
                Priority = 0;
            }
            else
            {
                float yDist = Math.Abs(character.WorldPosition.Y - targetHull.WorldPosition.Y);
                yDist = yDist > 100 ? yDist * 3 : 0;
                float dist = Math.Abs(character.WorldPosition.X - targetHull.WorldPosition.X) + yDist;
                float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, dist));
                if (targetHull == character.CurrentHull)
                {
                    distanceFactor = 1;
                }
                float severity = AIObjectiveExtinguishFires.GetFireSeverity(targetHull);
                float severityFactor = MathHelper.Lerp(0, 1, severity / 100);
                float devotion = CumulatedDevotion / 100;
                Priority = MathHelper.Lerp(0, 100, MathHelper.Clamp(devotion + (severityFactor * distanceFactor * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        protected override bool Check() => targetHull.FireSources.None();

        private float sinTime;
        protected override void Act(float deltaTime)
        {
            var extinguisherItem = character.Inventory.FindItemByIdentifier("fireextinguisher") ?? character.Inventory.FindItemByTag("fireextinguisher");
            if (extinguisherItem == null || extinguisherItem.Condition <= 0.0f || !character.HasEquippedItem(extinguisherItem))
            {
                TryAddSubObjective(ref getExtinguisherObjective, () =>
                {
                    character.Speak(TextManager.Get("DialogFindExtinguisher"), null, 2.0f, "findextinguisher", 30.0f);
                    return new AIObjectiveGetItem(character, "fireextinguisher", objectiveManager, equip: true)
                    {
                        // If the item is inside an unsafe hull, decrease the priority
                        GetItemPriority = i => HumanAIController.UnsafeHulls.Contains(i.CurrentHull) ? 0.1f : 1
                    };
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
                    bool inRange = fs.IsInDamageRange(character, MathHelper.Clamp(fs.DamageRange * 1.5f, extinguisher.Range * 0.5f, extinguisher.Range));
                    bool move = !inRange || !HumanAIController.VisibleHulls.Contains(fs.Hull);
                    if (inRange || useExtinquisherTimer > 0.0f)
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
                            bool isOperatingButtons = false;
                            if (SteeringManager == PathSteering)
                            {
                                var door = PathSteering.CurrentPath?.CurrentNode?.ConnectedDoor;
                                if (door != null && !door.IsOpen && !door.IsBroken)
                                {
                                    isOperatingButtons = door.HasIntegratedButtons || door.Item.GetConnectedComponents<Controller>(true).Any();
                                }
                            }
                            if (!isOperatingButtons)
                            {
                                character.SetInput(InputType.Aim, false, true);
                            }
                            sinTime += deltaTime * 10;
                        }
                        character.SetInput(extinguisherItem.IsShootable ? InputType.Shoot : InputType.Use, false, true);
                        extinguisher.Use(deltaTime, character);
                        if (!targetHull.FireSources.Contains(fs))
                        {
                            character.Speak(TextManager.GetWithVariable("DialogPutOutFire", "[roomname]", targetHull.DisplayName, true), null, 0, "putoutfire", 10.0f);
                        }
                        if (!character.CanSeeTarget(fs))
                        {
                            move = true;
                        }
                    }
                    if (move)
                    {
                        //go to the first firesource
                        TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(fs, character, objectiveManager, closeEnough: extinguisher.Range / 2)
                        {
                            DialogueIdentifier = "dialogcannotreachfire",
                            TargetName = fs.Hull.DisplayName
                        }, 
                            onAbandon: () =>  Abandon = true, 
                            onCompleted: () => RemoveSubObjective(ref gotoObjective));
                    }
                    else
                    {
                        character.AIController.SteeringManager.Reset();
                    }
                    break;
                }
            }
        }
    }
}
