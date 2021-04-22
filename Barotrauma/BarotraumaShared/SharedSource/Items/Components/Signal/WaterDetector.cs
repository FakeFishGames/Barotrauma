using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        //how often the detector can switch from state to another
        const float StateSwitchInterval = 1.0f;

        private bool isInWater;
        private float stateSwitchDelay;

        private string output;
        [InGameEditable, Serialize("1", true, description: "The signal the item sends out when it's underwater.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        private string falseOutput;
        [InGameEditable, Serialize("0", true, description: "The signal the item sends out when it's not underwater.", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null) { return; }
                falseOutput = value;
                if (falseOutput.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    falseOutput = falseOutput.Substring(0, MaxOutputLength);
                }
            }
        }

        private int maxOutputLength;
        [Editable, Serialize(200, false, description: "The maximum length of the output strings. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        public WaterDetector(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (stateSwitchDelay > 0.0f)
            {
                stateSwitchDelay -= deltaTime;
            }
            else
            {
                bool prevState = isInWater;

                isInWater = false;
                if (item.InWater)
                {
                    //item in water -> we definitely want to send the True output
                    isInWater = true;
                }
                else if (item.CurrentHull != null)
                {
                    //item in not water -> check if there's water anywhere within the rect of the item
                    if (item.CurrentHull.Surface > item.CurrentHull.Rect.Y - item.CurrentHull.Rect.Height + 1 &&
                        item.CurrentHull.Surface > item.Rect.Y - item.Rect.Height)
                    {
                        isInWater = true;
                    }
                }

                if (prevState != isInWater)
                {
                    stateSwitchDelay = StateSwitchInterval;
                }
            }

            string signalOut = isInWater ? Output : FalseOutput;
            if (!string.IsNullOrEmpty(signalOut))
            {
                item.SendSignal(signalOut, "signal_out");
            }

            if (item.CurrentHull != null)
            {
                int waterPercentage = MathHelper.Clamp((int)Math.Round(item.CurrentHull.WaterPercentage), 0, 100);
                item.SendSignal(waterPercentage.ToString(), "water_%");
            }
        }
    }
}
