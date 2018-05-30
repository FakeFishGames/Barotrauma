using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Xml.Linq;
using System.Collections.Generic;
using System;

namespace Barotrauma
{
    abstract class AnimController : Ragdoll
    {
        public abstract GroundedMovementParams WalkParams { get; }
        public abstract GroundedMovementParams RunParams { get; }
        public abstract AnimationParams SwimSlowParams { get; }
        public abstract AnimationParams SwimFastParams { get; }

        public AnimationParams CurrentAnimationParams => (InWater || !CanWalk) ? CurrentSwimParams : CurrentGroundedParams;
        public GroundedMovementParams CurrentGroundedParams
        {
            get
            {
                if (!CanWalk)
                {
                    DebugConsole.ThrowError($"{character.SpeciesName} cannot walk!");
                    return null;
                }
                else
                {
                    return IsMovingFast ? RunParams : WalkParams;
                }
            }
        }
        public AnimationParams CurrentSwimParams => IsMovingFast ? SwimFastParams : SwimSlowParams;

        public bool CanWalk => CanEnterSubmarine;
        public bool IsMovingFast
        {
            get
            {
                if (InWater || !CanWalk)
                {
                    return TargetMovement.LengthSquared() > SwimSlowParams.Speed * SwimSlowParams.Speed;
                }
                else
                {
                    return Math.Abs(TargetMovement.X) > WalkParams.Speed;
                }
            }
        }

        private List<AnimationParams> _allAnimParams;
        public List<AnimationParams> AllAnimParams
        {
            get
            {
                if (_allAnimParams == null)
                {
                    if (CanWalk)
                    {
                        _allAnimParams = new List<AnimationParams> { WalkParams, RunParams, SwimSlowParams, SwimFastParams };
                    }
                    else
                    {
                        _allAnimParams = new List<AnimationParams> { SwimSlowParams, SwimFastParams };
                    }
                }
                return _allAnimParams;
            }
        }

        public enum Animation { None, Climbing, UsingConstruction, Struggle, CPR };
        public Animation Anim;

        public Vector2 AimSourcePos => ConvertUnits.ToDisplayUnits(AimSourceSimPos);
        public virtual Vector2 AimSourceSimPos => Collider.SimPosition;

        protected Character character;
        protected float walkPos;

        public AnimController(Character character, XElement element, string seed) : base(character, element, seed)
        {
            this.character = character;
        }

        public virtual void UpdateAnim(float deltaTime) { }

        public virtual void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle) { }

        public virtual void DragCharacter(Character target) { }

        public virtual void UpdateUseItem(bool allowMovement, Vector2 handPos) { }

        public float GetSpeed(AnimationType type)
        {
            switch (type)
            {
                case AnimationType.Walk:
                    if (!CanWalk)
                    {
                        DebugConsole.ThrowError($"{character.SpeciesName} cannot walk!");
                        return 0;
                    }
                    return WalkParams.Speed;
                case AnimationType.Run:
                    if (!CanWalk)
                    {
                        DebugConsole.ThrowError($"{character.SpeciesName} cannot run!");
                        return 0;
                    }
                    return RunParams.Speed;
                case AnimationType.SwimSlow:
                    return SwimSlowParams.Speed;
                case AnimationType.SwimFast:
                    return SwimFastParams.Speed;
                default:
                    throw new NotImplementedException(type.ToString());
            }
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
    }
}
