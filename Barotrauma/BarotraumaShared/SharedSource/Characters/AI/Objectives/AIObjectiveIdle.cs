using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveIdle : AIObjective
    {
        public override Identifier Identifier { get; set; } = "idle".ToIdentifier();
        public override bool AllowAutomaticItemUnequipping => true;
        public override bool AllowInAnySub => true;

        private BehaviorType behavior;
        public BehaviorType Behavior
        {
            get { return behavior; }
            set
            {
                behavior = value;
                switch (behavior)
                {
                    case BehaviorType.Passive:
                    case BehaviorType.StayInHull:
                        newTargetIntervalMin = 60;
                        newTargetIntervalMax = 120;
                        standStillMin = 30;
                        standStillMax = 60;
                        break;
                    case BehaviorType.Active:
                        newTargetIntervalMin = 40;
                        newTargetIntervalMax = 60;
                        standStillMin = 20;
                        standStillMax = 40;
                        break;
                    case BehaviorType.Patrol:
                        newTargetIntervalMin = 15;
                        newTargetIntervalMax = 30;
                        standStillMin = 5;
                        standStillMax = 10;
                        break;
                }
            }
        }

        private float newTargetIntervalMin;
        private float newTargetIntervalMax;
        private float standStillMin;
        private float standStillMax;
        private readonly float walkDurationMin = 5;
        private readonly float walkDurationMax = 10;

        public enum BehaviorType
        {
            Patrol,
            Passive,
            StayInHull,
            Active
        }
        public Hull TargetHull { get; set; }
        private Hull currentTarget;
        private float newTargetTimer;

        private bool searchingNewHull;

        private float standStillTimer;
        private float walkDuration;

        private Character tooCloseCharacter;

        const float chairCheckInterval = 5.0f;
        private float chairCheckTimer;

        private float autonomousObjectiveRetryTimer = 10;

        private readonly List<Hull> targetHulls = new List<Hull>(20);
        private readonly List<float> hullWeights = new List<float>(20);

        public AIObjectiveIdle(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            Behavior = BehaviorType.Passive;
            standStillTimer = Rand.Range(-10.0f, 10.0f);
            walkDuration = Rand.Range(0.0f, 10.0f);
            chairCheckTimer = Rand.Range(0.0f, chairCheckInterval);
            CalculatePriority();
        }

        protected override bool CheckObjectiveSpecific() => false;
        public override bool CanBeCompleted => true;

        public override bool IsLoop { get => true; set => throw new Exception("Trying to set the value for IsLoop from: " + Environment.StackTrace.CleanupStackTrace()); }

        public readonly HashSet<Identifier> PreferredOutpostModuleTypes = new HashSet<Identifier>();

        public void CalculatePriority(float max = 0)
        {
            //Random = Rand.Range(0.5f, 1.5f);
            //randomTimer = randomUpdateInterval;
            //max = max > 0 ? max : Math.Min(Math.Min(AIObjectiveManager.RunPriority, AIObjectiveManager.OrderPriority) - 1, 100);
            //float initiative = character.GetSkillLevel("initiative");
            //Priority = MathHelper.Lerp(1, max, MathUtils.InverseLerp(100, 0, initiative * Random));
            Priority = 1;
        }

        protected override float GetPriority() => Priority;

        public override void Update(float deltaTime)
        {
            // Do nothing. Overrides the inherited devotion calculations.
        }

        private float timerMargin;

        private void SetTargetTimerLow()
        {
            // Increases the margin each time the method is called -> takes longer between the path finding calls.
            // The intention behind this is to reduce unnecessary path finding calls in cases where the bot can't find a path.
            timerMargin += 0.5f;
            timerMargin = Math.Min(timerMargin, newTargetIntervalMin);
            newTargetTimer = Math.Min(newTargetTimer, timerMargin);
        }

        private void SetTargetTimerHigh()
        {
            // This method is used to the timer between the current value and the min so that it never reaches 0.
            // Prevents pathfinder calls.
            newTargetTimer = Math.Max(newTargetTimer, newTargetIntervalMin);
            timerMargin = 0;
        }

        private void SetTargetTimerNormal()
        {
            newTargetTimer = currentTarget != null && character.AnimController.InWater ? newTargetIntervalMin : Rand.Range(newTargetIntervalMin, newTargetIntervalMax);
            timerMargin = 0;
        }

        private bool IsSteeringFinished() => PathSteering.CurrentPath != null && (PathSteering.CurrentPath.Finished || PathSteering.CurrentPath.Unreachable);

        protected override void Act(float deltaTime)
        {
            if (PathSteering == null) { return; }

            if (objectiveManager.FailedAutonomousObjectives)
            {
                if (autonomousObjectiveRetryTimer > 0)
                {
                    autonomousObjectiveRetryTimer -= deltaTime;
                }
                else
                {
                    objectiveManager.CreateAutonomousObjectives();
                }
            }

            //don't keep dragging others when idling
            if (character.SelectedCharacter != null)
            {
                character.DeselectCharacter();
            }

            character.SelectedItem = null;

            CleanupItems(deltaTime);

            if (behavior == BehaviorType.StayInHull && TargetHull == null && character.CurrentHull != null)
            {
                TargetHull = character.CurrentHull;
            }

            if (behavior == BehaviorType.StayInHull)
            {
                currentTarget = TargetHull;
                bool stayInHull = character.CurrentHull == currentTarget && IsSteeringFinished() && !character.IsClimbing;
                if (stayInHull)
                {
                    Wander(deltaTime);
                }
                else if (currentTarget != null)
                {
                    PathSteering.SteeringSeek(character.GetRelativeSimPosition(currentTarget), weight: 1, nodeFilter: node => node.Waypoint.CurrentHull != null);
                }
                else
                {
                    PathSteering.ResetPath();
                    PathSteering.Reset();
                }
            }
            else
            {
                bool currentTargetIsInvalid = currentTarget == null || IsForbidden(currentTarget) ||
                    (PathSteering.CurrentPath != null && PathSteering.CurrentPath.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull)));

                if (currentTarget != null && !currentTargetIsInvalid)
                {
                    if (character.TeamID == CharacterTeamType.FriendlyNPC && !character.IsEscorted)
                    {
                        if (currentTarget.Submarine.TeamID != character.TeamID)
                        {
                            currentTargetIsInvalid = true;
                        }
                    }
                    else
                    {
                        if (currentTarget.Submarine != character.Submarine)
                        {
                            currentTargetIsInvalid = true;
                        }
                    }
                }

                if (currentTargetIsInvalid || currentTarget == null || IsForbidden(character.CurrentHull) && IsSteeringFinished())
                {
                    if (newTargetTimer > timerMargin)
                    {
                        //don't reset to zero, otherwise the character will keep calling FindTargetHulls 
                        //almost constantly when there's a small number of potential hulls to move to
                        SetTargetTimerLow();
                    }
                }
                else if (character.IsClimbing)
                {
                    if (currentTarget == null)
                    {
                        SetTargetTimerLow();
                    }
                    else if (Math.Abs(character.AnimController.TargetMovement.Y) > 0.9f)
                    {
                        // Don't allow new targets when climbing straight up or down
                        SetTargetTimerHigh();
                    }
                }
                else if (character.AnimController.InWater)
                {
                    if (currentTarget == null)
                    {
                        SetTargetTimerLow();
                    }
                }
                if (newTargetTimer <= 0.0f)
                {
                    if (!searchingNewHull)
                    {
                        //find all available hulls first
                        searchingNewHull = true;
                        FindTargetHulls();
                    }
                    else if (targetHulls.Any())
                    {
                        //choose a random available hull
                        currentTarget = ToolBox.SelectWeightedRandom(targetHulls, hullWeights, Rand.RandSync.Unsynced);
                        bool isInWrongSub = (character.TeamID == CharacterTeamType.FriendlyNPC && !character.IsEscorted) && character.Submarine.TeamID != character.TeamID;
                        bool isCurrentHullAllowed = !isInWrongSub && !IsForbidden(character.CurrentHull);
                        var path = PathSteering.PathFinder.FindPath(character.SimPosition, currentTarget.SimPosition, character.Submarine, nodeFilter: node =>
                        {
                            if (node.Waypoint.CurrentHull == null) { return false; }
                            // Check that there is no unsafe hulls on the way to the target
                            if (node.Waypoint.CurrentHull != character.CurrentHull && HumanAIController.UnsafeHulls.Contains(node.Waypoint.CurrentHull)) { return false; }
                            return true;
                        }, endNodeFilter: node => !isCurrentHullAllowed | !IsForbidden(node.Waypoint.CurrentHull));
                        if (path.Unreachable)
                        {
                            //can't go to this room, remove it from the list and try another room
                            int index = targetHulls.IndexOf(currentTarget);
                            targetHulls.RemoveAt(index);
                            hullWeights.RemoveAt(index);
                            PathSteering.Reset();
                            currentTarget = null;
                            SetTargetTimerLow();
                            return;
                        }
                        character.AIController.SelectTarget(currentTarget.AiTarget);
                        PathSteering.SetPath(path);
                        SetTargetTimerNormal();
                        searchingNewHull = false;
                    }
                    else
                    {
                        // Couldn't find a valid hull
                        SetTargetTimerHigh();
                        searchingNewHull = false;
                    }
                }
                newTargetTimer -= deltaTime;
                if (!character.IsClimbing && (PathSteering == null || PathSteering.CurrentPath == null || IsSteeringFinished()))
                {
                    Wander(deltaTime);
                }
                else if (currentTarget != null)
                {
                    PathSteering.SteeringSeek(character.GetRelativeSimPosition(currentTarget), weight: 1, nodeFilter: node => node.Waypoint.CurrentHull != null);
                }
                else
                {
                    PathSteering.ResetPath();
                    PathSteering.Reset();
                }
            }
        }

        public void Wander(float deltaTime)
        {
            if (character.IsClimbing)
            {
                if (character.AnimController.GetHeightFromFloor() < 0.1f)
                {
                    character.AnimController.Anim = AnimController.Animation.None;
                    character.SelectedSecondaryItem = null;
                }
                return;
            }
            var currentHull = character.CurrentHull;
            if (!character.AnimController.InWater && currentHull != null)
            {
                standStillTimer -= deltaTime;
                if (standStillTimer > 0.0f)
                {
                    walkDuration = Rand.Range(walkDurationMin, walkDurationMax);
                    if (currentHull.Rect.Width > IndoorsSteeringManager.smallRoomSize / 2 && tooCloseCharacter == null)
                    {
                        foreach (Character c in Character.CharacterList)
                        {
                            if (c == character || !c.IsBot || c.CurrentHull != currentHull || !(c.AIController is HumanAIController humanAI)) { continue; }
                            if (Vector2.DistanceSquared(c.WorldPosition, character.WorldPosition) > 60.0f * 60.0f) { continue; }
                            if ((humanAI.ObjectiveManager.CurrentObjective is AIObjectiveIdle idleObjective && idleObjective.standStillTimer > 0.0f) ||
                                (humanAI.ObjectiveManager.CurrentObjective is AIObjectiveGoTo gotoObjective && gotoObjective.IsCloseEnough))
                            {
                                //if there are characters too close on both sides, don't try to steer away from them
                                //because it'll cause the character to spaz out trying to avoid both
                                if (tooCloseCharacter != null &&
                                    Math.Sign(tooCloseCharacter.WorldPosition.X - character.WorldPosition.X) != Math.Sign(c.WorldPosition.X - character.WorldPosition.X))
                                {
                                    tooCloseCharacter = null;
                                    break;
                                }
                                tooCloseCharacter = c;
                            }
                            HumanAIController.FaceTarget(c);
                        }
                    }

                    if (tooCloseCharacter != null && !tooCloseCharacter.Removed && Vector2.DistanceSquared(tooCloseCharacter.WorldPosition, character.WorldPosition) < 50.0f * 50.0f)
                    {
                        Vector2 diff = character.WorldPosition - tooCloseCharacter.WorldPosition;
                        if (diff.LengthSquared() < 0.0001f) { diff = Rand.Vector(1.0f); }
                        if (Math.Abs(diff.X) > 0 &&
                            (character.WorldPosition.X > currentHull.WorldRect.Right - 50 || character.WorldPosition.X < currentHull.WorldRect.Left + 50))
                        {
                            // Between a wall and a character -> move away
                            tooCloseCharacter = null;
                            PathSteering.Reset();
                            standStillTimer = 0;
                            walkDuration = Math.Min(walkDuration, walkDurationMin);
                            if (Behavior != BehaviorType.StayInHull && (currentHull.Size.X < IndoorsSteeringManager.smallRoomSize || currentHull.Size.X < (IndoorsSteeringManager.smallRoomSize / 2 * Character.CharacterList.Count(c => c.CurrentHull == currentHull))))
                            {
                                // Small room -> find another
                                newTargetTimer = Math.Min(newTargetTimer, 1);
                            }
                        }
                        else
                        {
                            PathSteering.SteeringManual(deltaTime, Vector2.Normalize(diff));
                        }
                        return;
                    }
                    else
                    {
                        PathSteering.Reset();
                        tooCloseCharacter = null;
                    }

                    chairCheckTimer -= deltaTime;
                    if (chairCheckTimer <= 0.0f && character.SelectedSecondaryItem == null)
                    {
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.CurrentHull != currentHull || !item.HasTag("chair")) { continue; }
                            var controller = item.GetComponent<Controller>();
                            if (controller == null || controller.User != null) { continue; }
                            item.TryInteract(character, forceSelectKey: true);
                        }
                        chairCheckTimer = chairCheckInterval;
                    }
                    return;
                }
                if (standStillTimer < -walkDuration)
                {
                    standStillTimer = Rand.Range(standStillMin, standStillMax);
                }
            }

            PathSteering.Wander(deltaTime);
        }

        public void FaceTargetAndWait(ISpatialEntity target, float waitTime)
        {
            standStillTimer = waitTime;
            HumanAIController.FaceTarget(target);
            currentTarget = null;
            SetTargetTimerHigh();
        }

        private void FindTargetHulls()
        {
            targetHulls.Clear();
            hullWeights.Clear();
            foreach (var hull in Hull.HullList)
            {
                if (character.Submarine == null) { break; }
                if (HumanAIController.UnsafeHulls.Contains(hull)) { continue; }
                if (hull.Submarine == null) { continue; }
                if (hull.Submarine.Info.IsRuin || hull.Submarine.Info.IsWreck) { continue; }
                if (character.TeamID == CharacterTeamType.FriendlyNPC && !character.IsEscorted)
                {
                    if (hull.Submarine.TeamID != character.TeamID)
                    {
                        // Don't allow npcs to idle in a sub that's not in their team (like the player sub)
                        continue;
                    }
                }
                else
                {
                    if (hull.Submarine.TeamID != character.Submarine.TeamID)
                    {
                        // Don't allow to idle in the subs that are not in the same team as the current sub
                        // -> the crew ai bots can't change the sub from outpost to main sub or vice versa on their own
                        continue;
                    }
                }
                if (IsForbidden(hull)) { continue; }
                // Check that the hull is linked
                if (!character.Submarine.IsConnectedTo(hull.Submarine)) { continue; }
                // Ignore very narrow hulls.
                if (hull.RectWidth < 200) { continue; }
                // Ignore hulls that are too low to stand inside.
                if (character.AnimController is HumanoidAnimController animController)
                {
                    if (hull.CeilingHeight < ConvertUnits.ToDisplayUnits(animController.HeadPosition.Value))
                    {
                        continue;
                    }
                }
                if (!targetHulls.Contains(hull))
                {
                    targetHulls.Add(hull);
                    float weight = hull.RectWidth;
                    // Prefer rooms that are closer. Avoid rooms that are not in the same level.
                    // If the behavior is active, prefer rooms that are not close.
                    float yDist = Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y);
                    yDist = yDist > 100 ? yDist * 5 : 0;
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + yDist;
                    float distanceFactor = behavior == BehaviorType.Patrol ? MathHelper.Lerp(1, 0, MathUtils.InverseLerp(2500, 0, dist)) : MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, 2500, dist));
                    float waterFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, 100, hull.WaterPercentage * 2));
                    weight *= distanceFactor * waterFactor;
                    hullWeights.Add(weight);
                }
            }

            if (PreferredOutpostModuleTypes.Any() && character.CurrentHull != null)
            {
                for (int i = 0; i < targetHulls.Count; i++)
                {
                    if (targetHulls[i].OutpostModuleTags.Any(t => PreferredOutpostModuleTypes.Contains(t)))
                    {
                        hullWeights[i] *= Rand.Range(10.0f, 100.0f);
                    }
                }
            }
        }

        #region Cleaning
        private readonly float checkItemsInterval = 1;
        private float checkItemsTimer;
        private readonly List<Item> itemsToClean = new List<Item>();
        private readonly List<Item> ignoredItems = new List<Item>();

        private void CleanupItems(float deltaTime)
        {
            if (checkItemsTimer <= 0)
            {
                checkItemsTimer = checkItemsInterval * Rand.Range(0.9f, 1.1f);
                var hull = character.CurrentHull;
                if (hull != null)
                {
                    itemsToClean.Clear();
                    foreach (Item item in Item.CleanableItems)
                    {
                        if (item.CurrentHull != hull) { continue; }
                        if (AIObjectiveCleanupItems.IsValidTarget(item, character, checkInventory: true, allowUnloading: false) && !ignoredItems.Contains(item))
                        {
                            itemsToClean.Add(item);
                        }
                    }
                    if (itemsToClean.Any())
                    {
                        var targetItem = itemsToClean.OrderBy(i => Math.Abs(character.WorldPosition.X - i.WorldPosition.X)).FirstOrDefault();
                        if (targetItem != null)
                        {
                            var cleanupObjective = new AIObjectiveCleanupItem(targetItem, character, objectiveManager, PriorityModifier);
                            cleanupObjective.Abandoned += () => ignoredItems.Add(targetItem);
                            subObjectives.Add(cleanupObjective);
                        }
                    }
                }
            }
            else
            {
                checkItemsTimer -= deltaTime;
            }
        }
        #endregion

        public static bool IsForbidden(Hull hull) => hull == null || hull.AvoidStaying;

        public override void Reset()
        {
            base.Reset();
            currentTarget = null;
            searchingNewHull = false;
            tooCloseCharacter = null;
            targetHulls.Clear();
            hullWeights.Clear();
            checkItemsTimer = 0;;
            itemsToClean.Clear();
            ignoredItems.Clear();
            autonomousObjectiveRetryTimer = 10;
        }

        public override void OnDeselected()
        {
            base.OnDeselected();
            foreach (var subObjective in SubObjectives)
            {
                if (subObjective is AIObjectiveCleanupItem cleanUpObjective)
                {
                    cleanUpObjective.DropTarget();
                }
            }
        }
    }
}
