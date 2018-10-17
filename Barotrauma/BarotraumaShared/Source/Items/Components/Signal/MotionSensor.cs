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

        private float updateTimer;
        
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

        public MotionSensor(Item item, XElement element)
            : base (item, element)
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
                if (Math.Abs(item.body.LinearVelocity.X) > 0.01f || Math.Abs(item.body.LinearVelocity.Y) > 0.1f)
                {
                    motionDetected = true;
                }
            }

            foreach (Character c in Character.CharacterList)
            {
                if (Math.Abs(c.WorldPosition.X - item.WorldPosition.X) < rangeX &&
                    Math.Abs(c.WorldPosition.Y - item.WorldPosition.Y) < rangeY)
                {
                    if (!c.AnimController.Limbs.Any(l => l.body.FarseerBody.Awake)) continue;

                    motionDetected = true;
                    break;
                }                
            }
        }
    }
}
