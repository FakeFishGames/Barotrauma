using System;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    interface ISpatialEntity
    {
        Vector2 Position { get; }
        Vector2 WorldPosition { get; }
        Vector2 SimPosition { get; }
        Submarine Submarine { get; }
        
        public static bool IsTargetVisible(ISpatialEntity target, ISpatialEntity seeingEntity, bool seeThroughWindows = false, bool checkFacing = false)
        {
            if (seeingEntity is Character seeingCharacter)
            {
                return seeingCharacter.CanSeeTarget(target, seeThroughWindows: seeThroughWindows, checkFacing: checkFacing);
            }
            if (target is Character targetCharacter)
            {
                return IsCharacterVisible(targetCharacter, seeingEntity, seeThroughWindows, checkFacing);
            }
            else
            {
                return CheckVisibility(target, seeingEntity, seeThroughWindows, checkFacing);
            }
        }

        public static bool IsCharacterVisible(Character target, ISpatialEntity seeingEntity, bool seeThroughWindows = false, bool checkFacing = false)
        {
            System.Diagnostics.Debug.Assert(target != null);
            if (target == null || target.Removed) { return false; }
            if (seeingEntity == null) { return false; }
            if (CheckVisibility(target, seeingEntity, seeThroughWindows, checkFacing)) { return true; }
            if (!target.AnimController.SimplePhysicsEnabled)
            {
                //find the limbs that are furthest from the target's position (from the viewer's point of view)
                Limb leftExtremity = null, rightExtremity = null;
                float leftMostDot = 0.0f, rightMostDot = 0.0f;
                Vector2 dir = target.WorldPosition - seeingEntity.WorldPosition;
                Vector2 leftDir = new Vector2(dir.Y, -dir.X);
                Vector2 rightDir = new Vector2(-dir.Y, dir.X);
                foreach (Limb limb in target.AnimController.Limbs)
                {
                    if (limb.IsSevered || limb == target.AnimController.MainLimb) { continue; }
                    if (limb.Hidden) { continue; }
                    Vector2 limbDir = limb.WorldPosition - seeingEntity.WorldPosition;
                    float leftDot = Vector2.Dot(limbDir, leftDir);
                    if (leftDot > leftMostDot)
                    {
                        leftMostDot = leftDot;
                        leftExtremity = limb;
                        continue;
                    }
                    float rightDot = Vector2.Dot(limbDir, rightDir);
                    if (rightDot > rightMostDot)
                    {
                        rightMostDot = rightDot;
                        rightExtremity = limb;
                    }
                }
                if (leftExtremity != null && CheckVisibility(leftExtremity, seeingEntity, seeThroughWindows, checkFacing)) { return true; }
                if (rightExtremity != null && CheckVisibility(rightExtremity, seeingEntity, seeThroughWindows, checkFacing)) { return true; }
            }
            return false;
        }

        public static bool CheckVisibility(ISpatialEntity target, ISpatialEntity seeingEntity, bool seeThroughWindows = true, bool checkFacing = false)
        {
            System.Diagnostics.Debug.Assert(target != null);
            if (target == null) { return false; }
            if (seeingEntity == null) { return false; }
            // TODO: Could we just use the method below? If not, let's refactor it so that we can.
            Vector2 diff = ConvertUnits.ToSimUnits(target.WorldPosition - seeingEntity.WorldPosition);
            if (checkFacing && seeingEntity is Character seeingCharacter)
            {
                if (Math.Sign(diff.X) != seeingCharacter.AnimController.Dir) { return false; }
            }
            //both inside the same sub (or both outside)
            //OR the we're inside, the other character outside
            if (target.Submarine == seeingEntity.Submarine || target.Submarine == null)
            {
                return Submarine.CheckVisibility(seeingEntity.SimPosition, seeingEntity.SimPosition + diff, blocksVisibilityPredicate: IsBlocking) == null;
            }
            //we're outside, the other character inside
            else if (seeingEntity.Submarine == null)
            {
                return Submarine.CheckVisibility(target.SimPosition, target.SimPosition - diff, blocksVisibilityPredicate: IsBlocking) == null;
            }
            //both inside different subs
            else
            {
                return 
                    Submarine.CheckVisibility(seeingEntity.SimPosition, seeingEntity.SimPosition + diff, blocksVisibilityPredicate: IsBlocking) == null &&
                    Submarine.CheckVisibility(target.SimPosition, target.SimPosition - diff, blocksVisibilityPredicate: IsBlocking) == null;                
            }

            bool IsBlocking(Fixture f)
            {
                var body = f.Body;
                if (body == null) { return false; }
                if (body.UserData is Structure wall)
                {
                    if (!wall.CastShadow && seeThroughWindows) { return false; }
                    return wall != target;
                }
                else if (body.UserData is Item item)
                {
                    if (item.GetComponent<Door>() is { HasWindow: true } door && seeThroughWindows)
                    {
                        if (door.IsPositionOnWindow(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition))) { return false; }
                    }
                    return item != target;
                }
                return true;
            }
        }
    }

    interface IIgnorable : ISpatialEntity
    {
        bool IgnoreByAI(Character character);
        bool OrderedToBeIgnored { get; set; }
    }
}
