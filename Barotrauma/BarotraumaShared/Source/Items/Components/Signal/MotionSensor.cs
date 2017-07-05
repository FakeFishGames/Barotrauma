using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class MotionSensor : ItemComponent
    {
        private const float UpdateInterval = 0.1f;

        private string output, falseOutput;

        private bool motionDetected;

        private float range;

        private float updateTimer;

        [InGameEditable, HasDefaultValue(0.0f, true)]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 500.0f);
            }
        }

        [InGameEditable, HasDefaultValue("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, HasDefaultValue("", true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
        }

        public MotionSensor(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (motionDetected)
            {
                item.SendSignal(1, output, "state_out", null);
            }
            else if (!string.IsNullOrWhiteSpace(falseOutput))
            {
                item.SendSignal(1, falseOutput, "state_out", null);
            }

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
                if (Math.Abs(c.WorldPosition.X - item.WorldPosition.X) < range &&
                    Math.Abs(c.WorldPosition.Y - item.WorldPosition.Y) < range)
                {
                    if (!c.AnimController.Limbs.Any(l => l.body.FarseerBody.Awake)) continue;

                    motionDetected = true;
                    break;
                }                
            }
        }
    }
}
