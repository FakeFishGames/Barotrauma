using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class AnimController : Ragdoll
    {
        protected abstract AnimParams AnimParams { get; }

        public enum Animation { None, Climbing, UsingConstruction, Struggle, CPR };
        public Animation Anim;

        public Vector2 AimSourcePos => ConvertUnits.ToDisplayUnits(AimSourceSimPos);
        public virtual Vector2 AimSourceSimPos => Collider.SimPosition;

        protected Character character;
        protected float walkPos;

        private readonly float _walkSpeed;
        public float WalkSpeed => AnimParams != null ? AnimParams.WalkSpeed : _walkSpeed;

        private readonly float _swimSpeed;
        public float SwimSpeed => AnimParams != null ? AnimParams.SwimSpeed : _swimSpeed;

        private readonly Vector2 _stepSize;
        protected Vector2 StepSize => AnimParams != null ? AnimParams.StepSize : _stepSize;

        private readonly float _legTorque;
        protected float LegTorque => AnimParams != null ? AnimParams.LegTorque : _legTorque;

        private readonly float _runSpeedMultiplier;
        public float RunSpeedMultiplier => AnimParams != null ? AnimParams.RunSpeedMultiplier : _runSpeedMultiplier;

        private readonly float _swimSpeedMultiplier;
        public float SwimSpeedMultiplier => AnimParams != null ? AnimParams.SwimSpeedMultiplier : _swimSpeedMultiplier;

        public AnimController(Character character, XElement element, string seed)
            : base(character, element, seed)
        {
            this.character = character;

            _stepSize = element.GetAttributeVector2("stepsize", Vector2.One);
            _stepSize = ConvertUnits.ToSimUnits(StepSize);

            _walkSpeed = element.GetAttributeFloat("walkspeed", 1.0f);
            _swimSpeed = element.GetAttributeFloat("swimspeed", 1.0f);

            _runSpeedMultiplier = element.GetAttributeFloat("runspeedmultiplier", 2f);
            _swimSpeedMultiplier = element.GetAttributeFloat("swimspeedmultiplier", 1.5f);
            
            _legTorque = element.GetAttributeFloat("legtorque", 0.0f);
        }

        public virtual void UpdateAnim(float deltaTime) { }

        public virtual void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle) { }

        public virtual void DragCharacter(Character target) { }

        public virtual void UpdateUseItem(bool allowMovement, Vector2 handPos) { }

   }
}
