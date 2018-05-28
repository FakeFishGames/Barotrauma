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

        public GroundedMovementParams CurrentGroundedParams => IsRunning ? RunParams : WalkParams;
        public AnimationParams CurrentSwimParams => IsSwimmingFast ? SwimFastParams : SwimSlowParams;
        // TODO: test that this is right
        public bool IsRunning => Math.Abs(TargetMovement.X) > WalkParams.Speed;
        public bool IsSwimmingFast => Math.Abs(TargetMovement.X) > SwimSlowParams.Speed;

        /// <summary>
        /// Note: creates a new list each time accessed. If you need to acces frequently, consider caching or change the implementation.
        /// </summary>
        public List<AnimationParams> AllAnimParams => new List<AnimationParams> { WalkParams, RunParams, SwimSlowParams, SwimFastParams };

        public enum Animation { None, Climbing, UsingConstruction, Struggle, CPR };
        public Animation Anim;

        public Vector2 AimSourcePos => ConvertUnits.ToDisplayUnits(AimSourceSimPos);
        public virtual Vector2 AimSourceSimPos => Collider.SimPosition;

        private readonly float _runSpeedMultiplier;

        protected Character character;
        protected float walkPos;

        public AnimController(Character character, XElement element, string seed)
            : base(character, element, seed)
        {
            this.character = character;

            //_stepSize = element.GetAttributeVector2("stepsize", Vector2.One);
            //_stepSize = ConvertUnits.ToSimUnits(_stepSize);

            // only applies to fishes?

            //_walkSpeed = element.GetAttributeFloat("walkspeed", 1.0f);
            //_swimSpeed = element.GetAttributeFloat("swimspeed", 1.0f);

            //_runSpeedMultiplier = element.GetAttributeFloat("runspeedmultiplier", 2f);
            //_swimSpeedMultiplier = element.GetAttributeFloat("swimspeedmultiplier", 1.5f);
            
            //_legTorque = element.GetAttributeFloat("legtorque", 0.0f);
        }

        public virtual void UpdateAnim(float deltaTime) { }

        public virtual void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle) { }

        public virtual void DragCharacter(Character target) { }

        public virtual void UpdateUseItem(bool allowMovement, Vector2 handPos) { }

   }
}
