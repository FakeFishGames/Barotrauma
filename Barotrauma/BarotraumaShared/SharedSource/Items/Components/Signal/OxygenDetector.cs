namespace Barotrauma.Items.Components
{
    class OxygenDetector : ItemComponent
    {
        public const int LowOxygenPercentage = 35;

        private int prevSentOxygenValue;
        public string OxygenSignal { get; private set; }

        public OxygenDetector(Item item, ContentXElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.CurrentHull == null) { return; }

            int currOxygenPercentage = (int)item.CurrentHull.OxygenPercentage;
            if (prevSentOxygenValue != currOxygenPercentage || OxygenSignal == null)
            {
                prevSentOxygenValue = currOxygenPercentage;
                OxygenSignal = prevSentOxygenValue.ToString();
            }

            item.SendSignal(OxygenSignal, "signal_out");
            item.SendSignal(currOxygenPercentage <= LowOxygenPercentage ? "1" : "0", "low_oxygen");
        }

    }
}
