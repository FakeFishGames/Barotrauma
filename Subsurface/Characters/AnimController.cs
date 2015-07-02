using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Subsurface
{
    class AnimController : Ragdoll
    {
        public bool IsStanding;

        public enum Animation { None, Climbing, UsingConstruction, Struggle };
        public Animation Anim;

        public Direction TargetDir;

        protected Character character;

        protected float walkSpeed, swimSpeed;  

        //how large impacts the character can take before being stunned
        //protected float impactTolerance;

        protected float stunTimer;
        
        protected float walkPos;

        protected readonly Vector2 stepSize;
        protected readonly float legTorque;
        protected readonly Vector2 stepOffset;

        public float StunTimer
        {
            get { return stunTimer; }
            set { stunTimer = value; }
        }
        
        public AnimController(Character character, XElement element)
            : base(character, element)
        {
            this.character = character;

            stepSize = ToolBox.GetAttributeVector2(element, "stepsize", Vector2.One);
            stepSize = ConvertUnits.ToSimUnits(stepSize);

            stepOffset = ToolBox.GetAttributeVector2(element, "stepoffset", Vector2.One);
            stepOffset = ConvertUnits.ToSimUnits(stepOffset);

            //impactTolerance = ToolBox.GetAttributeFloat(element, "impacttolerance", 10.0f);

            legTorque = ToolBox.GetAttributeFloat(element, "legtorque", 0.0f);
        }

        public virtual void UpdateAnim(float deltaTime) { }

        public virtual void HoldItem(float deltaTime, Camera cam, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, float holdAngle) { }

   }
}
