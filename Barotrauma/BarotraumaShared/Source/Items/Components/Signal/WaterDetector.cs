using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        private string output, falseOutput;

        //how often the detector can switch from state to another
        const float StateSwitchInterval = 1.0f;

        private bool isInWater;
        private float stateSwitchDelay;

        [InGameEditable, Serialize("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, Serialize("0", true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
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

            string signalOut = isInWater ? output : falseOutput;
            if (!string.IsNullOrEmpty(signalOut))
            {
                item.SendSignal(0, signalOut, "signal_out", null);
            }
        }
    }
}
