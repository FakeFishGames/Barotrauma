using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveExtinguishFire : AIObjective
    {
        public override Identifier Identifier { get; set; } = "extinguish fire".ToIdentifier();
        public override bool ForceRun => true;
        public override bool ConcurrentObjectives => true;
        public override bool KeepDivingGearOn => true;

        public override bool AllowInAnySub => true;

        private readonly Hull targetHull;

        private AIObjectiveGetItem getExtinguisherObjective;
        private AIObjectiveGoTo gotoObjective;

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
                float characterY = character.CurrentHull?.WorldPosition.Y ?? character.WorldPosition.Y;
                float yDist = Math.Abs(characterY - targetHull.WorldPosition.Y);
                yDist = yDist > 100 ? yDist * 3 : 0;
                float dist = Math.Abs(character.WorldPosition.X - targetHull.WorldPosition.X) + yDist;
                float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, dist));
                if (targetHull == character.CurrentHull || HumanAIController.VisibleHulls.Contains(targetHull))
                {
                    distanceFactor = 1;
                }
                float severity = AIObjectiveExtinguishFires.GetFireSeverity(targetHull);
                if (severity > 0.75f && !isOrder && 
                    targetHull.RoomName != null &&
                    !targetHull.RoomName.Contains("reactor", StringComparison.OrdinalIgnoreCase) && 
                    !targetHull.RoomName.Contains("engine", StringComparison.OrdinalIgnoreCase) && 
                    !targetHull.RoomName.Contains("command", StringComparison.OrdinalIgnoreCase))
                {
                    // Ignore severe fires to prevent casualities unless ordered to extinguish.
                    Priority = 0;
                    Abandon = true;
                }
                else
                {
                    float devotion = CumulatedDevotion / 100;
                    Priority = MathHelper.Lerp(0, AIObjectiveManager.MaxObjectivePriority, MathHelper.Clamp(devotion + (severity * distanceFactor * PriorityModifier), 0, 1));
                }
            }
            return Priority;
        }

        protected override bool CheckObjectiveSpecific() => targetHull.FireSources.None();

        private float sinTime;
        protected override void Act(float deltaTime)
        {
            var extinguisherItem = character.Inventory.FindItemByTag("fireextinguisher".ToIdentifier());
            if (extinguisherItem == null || extinguisherItem.Condition <= 0.0f || !character.HasEquippedItem(extinguisherItem))
            {
                TryAddSubObjective(ref getExtinguisherObjective, () =>
                {
                    if (character.IsOnPlayerTeam && !character.HasEquippedItem("fireextinguisher".ToIdentifier(), allowBroken: false))
                    {
                        character.Speak(TextManager.Get("DialogFindExtinguisher").Value, null, 2.0f, "findextinguisher".ToIdentifier(), 30.0f);
                    }
                    var getItemObjective = new AIObjectiveGetItem(character, "fireextinguisher".ToIdentifier(), objectiveManager, equip: true)
                    {
                        AllowStealing = true,
                        // If the item is inside an unsafe hull, decrease the priority
                        GetItemPriority = i => HumanAIController.UnsafeHulls.Contains(i.CurrentHull) ? 0.1f : 1
                    };
                    if (objectiveManager.HasOrder<AIObjectiveExtinguishFires>())
                    {
                        getItemObjective.Abandoned += () => character.Speak(TextManager.Get("dialogcannotfindfireextinguisher").Value, null, 0.0f, "dialogcannotfindfireextinguisher".ToIdentifier(), 10.0f);
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
                    if (fs == null) { continue; }
                    if (fs.Removed) { continue; }
                    if (character.CurrentHull == null)
                    {
                        Abandon = true;
                        break;
                    }
                    float xDist = Math.Abs(character.WorldPosition.X - fs.WorldPosition.X);
                    float yDist = Math.Abs(character.CurrentHull.WorldPosition.Y - targetHull.WorldPosition.Y);
                    float dist = xDist + yDist;
                    bool inRange = dist < extinguisher.Range;
                    bool isInDamageRange = fs.IsInDamageRange(character, fs.DamageRange) && character.CanSeeTarget(targetHull);
                    bool moveCloser = !isInDamageRange && (!inRange || !character.CanSeeTarget(targetHull));
                    bool operateExtinguisher = !moveCloser || (dist < extinguisher.Range * 1.2f && character.CanSeeTarget(targetHull));
                    if (operateExtinguisher)
                    {
                        character.CursorPosition = fs.Position;
                        Vector2 fromCharacterToFireSource = fs.WorldPosition - character.WorldPosition;
                        character.CursorPosition += VectorExtensions.Forward(extinguisherItem.body.TransformedRotation + (float)Math.Sin(sinTime) / 2, fromCharacterToFireSource.Length() / 2);
                        if (extinguisherItem.RequireAimToUse)
                        {
                            character.SetInput(InputType.Aim, false, true);
                            sinTime += deltaTime * 10;
                        }
                        character.SetInput(extinguisherItem.IsShootable ? InputType.Shoot : InputType.Use, false, true);
                        extinguisher.Use(deltaTime, character);
                        if (!targetHull.FireSources.Contains(fs))
                        {
                            character.Speak(TextManager.GetWithVariable("DialogPutOutFire", "[roomname]", targetHull.DisplayName, FormatCapitals.Yes).Value, null, 0, "putoutfire".ToIdentifier(), 10.0f);
                        }
                        // Prevents running into the flames.
                        objectiveManager.CurrentObjective.ForceWalk = true;
                    }
                    if (moveCloser)
                    {
                        if (TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(fs, character, objectiveManager, closeEnough: extinguisher.Range * 0.8f)
                        {
                            DialogueIdentifier = "dialogcannotreachfire".ToIdentifier(),
                            TargetName = fs.Hull.DisplayName,
                        },
                        onAbandon: () => Abandon = true,
                        onCompleted: () => RemoveSubObjective(ref gotoObjective)))
                        {
                            gotoObjective.requiredCondition = () => character.CanSeeTarget(targetHull);
                        }
                    }
                    else if (!operateExtinguisher || isInDamageRange)
                    {
                        // Don't walk into the flames.
                        RemoveSubObjective(ref gotoObjective);
                        SteeringManager.Reset();
                    }
                    // Only target one fire source at the time.
                    break;
                }
            }
        }

        public override void Reset()
        {
            base.Reset();
            getExtinguisherObjective = null;
            gotoObjective = null;
            sinTime = 0;
            SteeringManager.Reset();
        }

        protected override void OnCompleted()
        {
            base.OnCompleted();
            SteeringManager.Reset();
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            SteeringManager.Reset();
        }
    }
}
