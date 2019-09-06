using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MotionSensor : ItemComponent
    {
        private const float UpdateInterval = 0.1f;

        private string output, falseOutput;

        private bool motionDetected;

        private float rangeX, rangeY;

        private Vector2 detectOffset;

        private float updateTimer;

        [Serialize(false, false)]
        public bool MotionDetected
        {
            get { return motionDetected; }
            set { motionDetected = value; }
        }

        [Serialize(false, true), Editable]
        public bool OnlyHumans
        {
            get;
            set;
        }

        [InGameEditable, Serialize(0.0f, true)]
        public float RangeX
        {
            get { return rangeX; }
            set
            {
                rangeX = MathHelper.Clamp(value, 0.0f, 1000.0f);
            }
        }
        [InGameEditable, Serialize(0.0f, true)]
        public float RangeY
        {
            get { return rangeY; }
            set
            {
                rangeY = MathHelper.Clamp(value, 0.0f, 1000.0f);
            }
        }

        [Editable, Serialize("0,0", true, description: "The position to detect the movement at relative to the item. For example, 0,100 would detect movement 100 units above the item.")]
        public Vector2 DetectOffset
        {
            get { return detectOffset; }
            set
            {
                detectOffset = value;
                detectOffset.X = MathHelper.Clamp(value.X, -rangeX, rangeX);
                detectOffset.Y = MathHelper.Clamp(value.Y, -rangeY, rangeY);
            }
        }

        [InGameEditable, Serialize("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, Serialize("", true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
        }

        [Editable(DecimalCount = 3), Serialize(0.01f, true, description: "How fast the objects within the detector's range have to be moving (in m/s).")]
        public float MinimumVelocity
        {
            get;
            set;
        }


        public MotionSensor(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            //backwards compatibility
            if (element.Attribute("range") != null)
            {
                rangeX = rangeY = element.GetAttributeFloat("range", 0.0f);
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            string signalOut = motionDetected ? output : falseOutput;

            if (!string.IsNullOrEmpty(signalOut)) item.SendSignal(1, signalOut, "state_out", null);

            updateTimer -= deltaTime;
            if (updateTimer > 0.0f) return;

            motionDetected = false;
            updateTimer = UpdateInterval;

            if (item.body != null && item.body.Enabled)
            {
                if (Math.Abs(item.body.LinearVelocity.X) > MinimumVelocity || Math.Abs(item.body.LinearVelocity.Y) > MinimumVelocity)
                {
                    motionDetected = true;
                }
            }

            Vector2 detectPos = item.WorldPosition + detectOffset;
            Rectangle detectRect = new Rectangle((int)(detectPos.X - rangeX), (int)(detectPos.Y - rangeY), (int)(rangeX * 2), (int)(rangeY * 2));
            float broadRangeX = Math.Max(rangeX * 2, 500);
            float broadRangeY = Math.Max(rangeY * 2, 500);

            foreach (Character c in Character.CharacterList)
            {
                if (OnlyHumans && !c.IsHuman) { continue; }

                //do a rough check based on the position of the character's collider first
                //before the more accurate limb-based check
                if (Math.Abs(c.WorldPosition.X - detectPos.X) > broadRangeX || Math.Abs(c.WorldPosition.Y - detectPos.Y) > broadRangeY)
                {
                    continue;
                }

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.LinearVelocity.LengthSquared() <= MinimumVelocity * MinimumVelocity) continue;
                    if (MathUtils.CircleIntersectsRectangle(limb.WorldPosition, ConvertUnits.ToDisplayUnits(limb.body.GetMaxExtent()), detectRect))
                    {
                        motionDetected = true;
                        break;
                    }
                }
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            detectOffset.X = -detectOffset.X;
        }
        public override void FlipY(bool relativeToSub)
        {
            detectOffset.Y = -detectOffset.Y;
        }
    }
}
