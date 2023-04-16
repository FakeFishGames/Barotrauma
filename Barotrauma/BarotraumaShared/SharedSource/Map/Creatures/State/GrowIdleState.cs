#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace Barotrauma.MapCreatures.Behavior
{
    internal class GrowIdleState: IBallastFloraState
    {
        public readonly BallastFloraBehavior Behavior;
        private float growthTimer;

        public GrowIdleState(BallastFloraBehavior behavior)
        {
            Behavior = behavior;
        }

        public virtual ExitState GetState() => ExitState.Running;

        public virtual void Enter()
        {
            foreach (BallastFloraBranch branch in Behavior.Branches.Where(b => b.CanGrowMore()))
            {
                if (TryScanTargets(branch)) { return; }
            }
        }

        public void Exit() { }

        private bool TryScanTargets(BallastFloraBranch branch)
        {
            if (ScanForTargets(branch) is { } newTarget)
            {
                Behavior.StateMachine.EnterState(new GrowToTargetState(Behavior, branch, newTarget));
                return true;
            }

            return false;
        }

        public void Update(float deltaTime)
        {
            if (growthTimer > 0)
            {
                growthTimer -= Behavior.GetGrowthSpeed(deltaTime);
            }
            else
            {
                Grow();
                UpdateIgnoredTargets();
                growthTimer = Behavior.GrowthWarps > 0 ? 0f : 5f;
            }
        }

        protected virtual void Grow()
        {
            List<BallastFloraBranch> newBranches = GrowRandomly();
#if DEBUG
            Behavior.debugSearchLines.Clear();
#endif
            foreach (var branch in newBranches)
            {
                TryScanTargets(branch);
            }
        }

        public void UpdateIgnoredTargets()
        {
            Behavior.IgnoredTargets.ForEachMod(pair =>
            {
                var (item, delay) = pair;

                if (delay <= 0)
                {
                    Behavior.IgnoredTargets.Remove(item);
                }
                else
                {
                    Behavior.IgnoredTargets[item] = --delay;
                }
            });
        }

        private List<BallastFloraBranch> GrowRandomly()
        {
            List<BallastFloraBranch> availableBranches = Behavior.Branches.Where(b => !b.DisconnectedFromRoot && b.FailedGrowthAttempts <= 8 && b.CanGrowMore()).ToList();
            if (availableBranches.Count == 0) { return availableBranches; }

            //prefer growing from the branches furthest from the root (ones with the largest branch depth)
            var branch = ToolBox.SelectWeightedRandom(availableBranches, b => (float)b.BranchDepth, Rand.RandSync.Unsynced);

            TileSide side = branch.GetRandomFreeSide();
            if (side == TileSide.None) { return availableBranches; }

            Behavior.TryGrowBranch(branch, side, out List<BallastFloraBranch> result);
            availableBranches.Clear();
            availableBranches.Add(branch);

            return availableBranches;
        }

        private Item? ScanForTargets(VineTile branch)
        {
            Hull parent = Behavior.Parent;
            Vector2 worldPos = Behavior.GetWorldPosition() + branch.Position;
            Vector2 pos = parent.Position + Behavior.Offset + branch.Position;

            Vector2 diameter = ConvertUnits.ToSimUnits(new Vector2(branch.Rect.Width / 2f, branch.Rect.Height / 2f));
            Vector2 topLeft = ConvertUnits.ToSimUnits(pos) - diameter;
            Vector2 bottomRight = ConvertUnits.ToSimUnits(pos) + diameter;

            int highestPriority = 0;
            Item? currentItem = null;

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != parent.Submarine || Vector2.DistanceSquared(worldPos, item.WorldPosition) > Behavior.Sight * Behavior.Sight) { continue; }
                if (Behavior.ClaimedTargets.Contains(item)) { continue; }
                if (Behavior.IgnoredTargets.ContainsKey(item)) { continue; }

                int priority = 0;
                foreach (BallastFloraBehavior.AITarget target in Behavior.Targets)
                {
                    if (target.Priority <= highestPriority || !target.Matches(item)) { continue; }
                    priority = target.Priority;
                    break;
                }

                if (priority == 0) { continue; }

                Vector2 itemSimPos = ConvertUnits.ToSimUnits(item.Position);

#if DEBUG
                Tuple<Vector2, Vector2> debugLine1 = Tuple.Create(parent.Position - ConvertUnits.ToDisplayUnits(topLeft), parent.Position - ConvertUnits.ToDisplayUnits(itemSimPos - diameter));
                Tuple<Vector2, Vector2> debugLine2 = Tuple.Create(parent.Position - ConvertUnits.ToDisplayUnits(bottomRight), parent.Position - ConvertUnits.ToDisplayUnits(itemSimPos + diameter));
                Behavior.debugSearchLines.Add(debugLine2);
                Behavior.debugSearchLines.Add(debugLine1);
#endif

                Body? body1 = Submarine.CheckVisibility(itemSimPos - diameter, topLeft);
                if (Blocks(body1, item)) { continue; }

                Body? body2 = Submarine.CheckVisibility(itemSimPos + diameter, bottomRight);
                if (Blocks(body2, item)) { continue; }

                highestPriority = priority;
                currentItem = item;
            }

            if (currentItem != null)
            {
                foreach (BallastFloraBranch existingBranch in Behavior.Branches)
                {
                    if (existingBranch.Health <= 0 || existingBranch.IsRootGrowth) { continue; }
                    if (Behavior.BranchContainsTarget(existingBranch, currentItem))
                    {
                        Behavior.ClaimTarget(currentItem, existingBranch);
                        return null;
                    }
                }

                return currentItem;
            }

            return null;

            static bool Blocks(Body body, Item target)
            {
                if (body == null) { return false; }

                switch (body.UserData)
                {
                    case Submarine _:
                    case Structure _:
                    case Item it when it != target:
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}