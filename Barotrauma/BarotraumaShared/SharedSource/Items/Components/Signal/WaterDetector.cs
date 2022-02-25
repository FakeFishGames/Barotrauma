using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        //how often the detector can switch from state to another
        const float StateSwitchInterval = 1.0f;

        private int prevSentWaterPercentageValue;
        private string waterPercentageSignal;

        private bool isInWater;
        private float stateSwitchDelay;

        private int maxOutputLength;
        [Editable, Serialize(200, IsPropertySaveable.No, description: "The maximum length of the output strings. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        private string output;
        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal the item sends out when it's underwater.", alwaysUseInstanceValues: true)]
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
        [InGameEditable, Serialize("0", IsPropertySaveable.Yes, description: "The signal the item sends out when it's not underwater.", alwaysUseInstanceValues: true)]
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

        public WaterDetector(Item item, ContentXElement element)
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
                else if (item.CurrentHull != null && item.CurrentHull.WaterPercentage > 0.0f && item.CurrentHull.WaterVolume > 1.0f)
                {
                    //(center of the) item in not water -> check if the water surface is below the bottom of the item's rect
                    if (item.CurrentHull.Surface > item.Rect.Y - item.Rect.Height)
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
                int waterPercentage = 0;
                //ignore minuscule amounts of water
                if (item.CurrentHull.WaterVolume > 1.0f)
                {
                    waterPercentage = MathHelper.Clamp((int)Math.Ceiling(item.CurrentHull.WaterPercentage), 0, 100);
                }
                if (prevSentWaterPercentageValue != waterPercentage || waterPercentageSignal == null)
                {
                    prevSentWaterPercentageValue = waterPercentage;
                    waterPercentageSignal = prevSentWaterPercentageValue.ToString();
                }
                item.SendSignal(waterPercentageSignal, "water_%");
            }
            string highPressureOut = (item.CurrentHull == null || item.CurrentHull.LethalPressure > 5.0f) ? "1" : "0";
            item.SendSignal(highPressureOut, "high_pressure");
        }
    }
}
