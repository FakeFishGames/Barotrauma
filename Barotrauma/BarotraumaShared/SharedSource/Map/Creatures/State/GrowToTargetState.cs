using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma.MapCreatures.Behavior
{
    class GrowToTargetState: GrowIdleState
    {
        public readonly List<BallastFloraBranch> TargetBranches = new List<BallastFloraBranch>();
        public readonly Item Target;

        private bool isFinished;

        public GrowToTargetState(BallastFloraBehavior behavior, BallastFloraBranch starter, Item target) : base(behavior)
        {
            Target = target;
            TargetBranches.Add(starter);
        }
        
        // do nothing
        public override void Enter() { }

        public override ExitState GetState() => isFinished ? ExitState.Terminate : ExitState.Running;

        protected override void Grow()
        {
            if (TargetBranches.Any(b => b.Removed))
            {
                if (!Behavior.IgnoredTargets.ContainsKey(Target))
                {
                    Behavior.IgnoredTargets.Add(Target, 10);
                }

                isFinished = true;
                return;
            }

            if (Target == null || Target.Removed)
            {
                isFinished = true;
                return;
            }

            GrowTowardsTarget();
        }
        
        private void GrowTowardsTarget()
        {
            bool succeeded = false;
            
            List<BallastFloraBranch> newList = new List<BallastFloraBranch>(TargetBranches);
            foreach (BallastFloraBranch branch in newList)
            {
                if (branch.FailedGrowthAttempts > 8 || !branch.CanGrowMore()) { continue; }

                // Get what side gets us closest to the target
                TileSide side = GetClosestSide(branch, Target.WorldPosition);
            
                if (branch.IsSideBlocked(side)) { continue; }

                succeeded |= Behavior.TryGrowBranch(branch, side, out List<BallastFloraBranch> newBranches);
                TargetBranches.AddRange(newBranches);

                foreach (BallastFloraBranch newBranch in newBranches)
                {
                    Rectangle worldRect = newBranch.Rect;
                    worldRect.Location = Behavior.GetWorldPosition().ToPoint() + worldRect.Location;
                    if (Behavior.BranchContainsTarget(newBranch, Target))
                    {
                        Behavior.ClaimTarget(Target, newBranch);
                        isFinished = true;
                        return;
                    }
                }
            }

            if (!succeeded)
            {
                if (!Behavior.IgnoredTargets.ContainsKey(Target))
                {
                    Behavior.IgnoredTargets.Add(Target, 1);
                }

                isFinished = true;
            }
        }

        private TileSide GetClosestSide(VineTile tile, Vector2 targetPos)
        {
            var (distX, distY) = tile.Position + Behavior.GetWorldPosition() - targetPos;
            int absDistX = (int) Math.Abs(distX), absDistY = (int) Math.Abs(distY);

            return absDistX > absDistY ? distX > 0 ? TileSide.Left : TileSide.Right : distY > 0 ? TileSide.Bottom : TileSide.Top;
        }
    }
}