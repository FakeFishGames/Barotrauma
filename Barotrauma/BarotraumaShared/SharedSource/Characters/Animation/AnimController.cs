using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System;

namespace Barotrauma
{
    abstract class AnimController : Ragdoll
    {
        public abstract GroundedMovementParams WalkParams { get; set; }
        public abstract GroundedMovementParams RunParams { get; set; }
        public abstract SwimParams SwimSlowParams { get; set; }
        public abstract SwimParams SwimFastParams { get; set; }

        public AnimationParams CurrentAnimationParams
        {
            get
            {
                if (ForceSelectAnimationType == AnimationType.NotDefined)
                {
                    return (InWater || !CanWalk) ? (AnimationParams)CurrentSwimParams : CurrentGroundedParams;
                }
                else
                {
                    return GetAnimationParamsFromType(ForceSelectAnimationType);
                }
            }
        }
        public AnimationType ForceSelectAnimationType { get; set; }
        public GroundedMovementParams CurrentGroundedParams
        {
            get
            {
                if (ForceSelectAnimationType != AnimationType.NotDefined)
                {
                    return GetAnimationParamsFromType(ForceSelectAnimationType) as GroundedMovementParams;
                }
                if (!CanWalk)
                {
                    //DebugConsole.ThrowError($"{character.SpeciesName} cannot walk!");
                    return null;
                }
                else
                {
                    return IsMovingFast ? RunParams : WalkParams;
                }
            }
        }
        public SwimParams CurrentSwimParams
        {
            get
            {
                if (ForceSelectAnimationType != AnimationType.NotDefined)
                {
                    return GetAnimationParamsFromType(ForceSelectAnimationType) as SwimParams;
                }
                else
                {
                    return IsMovingFast? SwimFastParams : SwimSlowParams;
                }
            }
        }

        public bool CanWalk => RagdollParams.CanWalk;
        public bool IsMovingBackwards => !InWater && Math.Sign(targetMovement.X) == -Math.Sign(Dir);
        
        // TODO: define death anim duration in XML
        protected float deathAnimTimer, deathAnimDuration = 5.0f;

        /// <summary>
        /// Note: Presupposes that the slow speed is lower than the high speed. Otherwise will give invalid results.
        /// </summary>
        public bool IsMovingFast
        {
            get
            {
                if (InWater || !CanWalk)
                {
                    float avg = (SwimSlowParams.MovementSpeed + SwimFastParams.MovementSpeed) / 2.0f;
                    return TargetMovement.LengthSquared() > avg * avg;
                }
                else
                {
                    return Math.Abs(TargetMovement.X) > (WalkParams.MovementSpeed + RunParams.MovementSpeed) / 2.0f;
                }
            }
        }

        /// <summary>
        /// Note: creates a new list every time, because the params might have changed. If there is a need to access the property frequently, change the implementation to an array, where the slot is updated when the param is updated(?)
        /// Currently it's not simple to implement, since the properties are not implemented here, but in the derived classes. Would require to change the params virtual and to call the base property getter/setter or something.
        /// </summary>
        public List<AnimationParams> AllAnimParams
        {
            get
            {
                if (CanWalk)
                {
                    return new List<AnimationParams> { WalkParams, RunParams, SwimSlowParams, SwimFastParams };
                }
                else
                {
                    return new List<AnimationParams> { SwimSlowParams, SwimFastParams };
                }
            }
        }

        public enum Animation { None, Climbing, UsingConstruction, Struggle, CPR };
        public Animation Anim;

        public Vector2 AimSourceWorldPos
        {
            get
            {
                Vector2 sourcePos = character.AnimController.AimSourcePos;
                if (character.Submarine != null) { sourcePos += character.Submarine.Position; }
                return sourcePos;
            }
        }

        public Vector2 AimSourcePos => ConvertUnits.ToDisplayUnits(AimSourceSimPos);
        public virtual Vector2 AimSourceSimPos => Collider.SimPosition;

        protected float? GetValidOrNull(AnimationParams p, float? v)
        {
            if (p == null) { return null; }
            if (v == null) { return null; }
            if (!MathUtils.IsValid(v.Value)) { return null; }
            return v.Value;
        }
        protected Vector2? GetValidOrNull(AnimationParams p, Vector2 v)
        {
            if (p == null) { return null; }
            return v;
        }

        public override float? HeadPosition => GetValidOrNull(CurrentGroundedParams, CurrentGroundedParams?.HeadPosition * RagdollParams.JointScale);
        public override float? TorsoPosition => GetValidOrNull(CurrentGroundedParams, CurrentGroundedParams?.TorsoPosition * RagdollParams.JointScale);
        public override float? HeadAngle => GetValidOrNull(CurrentAnimationParams, CurrentAnimationParams?.HeadAngleInRadians);
        public override float? TorsoAngle => GetValidOrNull(CurrentAnimationParams, CurrentAnimationParams?.TorsoAngleInRadians);
        public virtual Vector2? StepSize => GetValidOrNull(CurrentGroundedParams, CurrentGroundedParams.StepSize * RagdollParams.JointScale);

        public bool AnimationTestPose { get; set; }

        public float WalkPos { get; protected set; }

        public AnimController(Character character, string seed, RagdollParams ragdollParams = null) : base(character, seed, ragdollParams) { }

        public virtual void UpdateAnim(float deltaTime) { }

        public virtual void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle, float itemAngleRelativeToHoldAngle = 0.0f, bool aimingMelee = false) { }

        public virtual void DragCharacter(Character target, float deltaTime) { }

        public virtual void UpdateUseItem(bool allowMovement, Vector2 handWorldPos) { }

        public float GetSpeed(AnimationType type)
        {
            GroundedMovementParams movementParams;
            switch (type)
            {
                case AnimationType.Walk:
                    if (!CanWalk)
                    {
                        DebugConsole.ThrowError($"{character.SpeciesName} cannot walk!");
                        return 0;
                    }
                    movementParams = WalkParams;
                    break;
                case AnimationType.Run:
                    if (!CanWalk)
                    {
                        DebugConsole.ThrowError($"{character.SpeciesName} cannot run!");
                        return 0;
                    }
                    movementParams = RunParams;
                    break;
                case AnimationType.SwimSlow:
                    return SwimSlowParams.MovementSpeed;
                case AnimationType.SwimFast:
                    return SwimFastParams.MovementSpeed;
                default:
                    throw new NotImplementedException(type.ToString());
            }
            return IsMovingBackwards ? movementParams.MovementSpeed * movementParams.BackwardsMovementMultiplier : movementParams.MovementSpeed;
        }

        public float GetCurrentSpeed(bool useMaxSpeed)
        {
            AnimationType animType;
            if (InWater || !CanWalk)
            {
                if (useMaxSpeed)
                {
                    animType = AnimationType.SwimFast;
                }
                else
                {
                    animType = AnimationType.SwimSlow;
                }
            }
            else
            {
                if (useMaxSpeed)
                {
                    animType = AnimationType.Run;
                }
                else
                {
                    animType = AnimationType.Walk;
                }
            }
            return GetSpeed(animType);
        }

        public AnimationParams GetAnimationParamsFromType(AnimationType type)
        {
            switch (type)
            {
                case AnimationType.Walk:
                    return WalkParams;
                case AnimationType.Run:
                    return RunParams;
                case AnimationType.SwimSlow:
                    return SwimSlowParams;
                case AnimationType.SwimFast:
                    return SwimFastParams;
                case AnimationType.NotDefined:
                    return null;
                default:
                    throw new NotImplementedException(type.ToString());
            }
        }
    }
}
