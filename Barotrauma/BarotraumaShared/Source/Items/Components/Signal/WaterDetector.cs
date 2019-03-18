using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        private string output, falseOutput;

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
            string signalOut = falseOutput;
            if (item.InWater)
            {
                //item in water -> we definitely want to send the True output
                signalOut = Output;
            }
            else if (item.CurrentHull != null)
            {
                //item in not water -> check if there's water anywhere within the rect of the item
                if (item.CurrentHull.Surface > item.CurrentHull.Rect.Y - item.CurrentHull.Rect.Height + 1 &&
                    item.CurrentHull.Surface > item.Rect.Y - item.Rect.Height)
                {
                    signalOut = output;
                }
            }

            if (!string.IsNullOrEmpty(signalOut))
            {
                item.SendSignal(0, signalOut, "signal_out", null);
            }
        }
    }
}
