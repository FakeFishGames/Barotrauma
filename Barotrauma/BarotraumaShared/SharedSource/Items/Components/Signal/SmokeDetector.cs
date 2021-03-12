using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SmokeDetector : ItemComponent
    {
        const float FireCheckInterval = 1.0f;
        private float fireCheckTimer;

        private bool fireInRange;

        private string output;
        [InGameEditable, Serialize("1", true, description: "The signal the item outputs when it has detected a fire.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength)
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        private string falseOutput;
        [InGameEditable, Serialize("0", true, description: "The signal the item outputs when it has not detected a fire.", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null) { return; }
                falseOutput = value;
                if (falseOutput.Length > MaxOutputLength)
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

        public SmokeDetector(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        private bool IsFireInRange()
        {
            if (item.CurrentHull == null || item.InWater) { return false; }

            var connectedHulls = item.CurrentHull.GetConnectedHulls(includingThis: true, searchDepth: 10, ignoreClosedGaps: true);
            foreach (Hull hull in connectedHulls)
            {
                foreach (FireSource fireSource in hull.FireSources)
                {
                    if (fireSource.IsInDamageRange(item.WorldPosition, fireSource.DamageRange * 2.0f)) { return true; }
                }
            }

            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            fireCheckTimer -= deltaTime;
            if (fireCheckTimer <= 0.0f)
            {
                fireInRange = IsFireInRange();
                fireCheckTimer = FireCheckInterval;
            }
            item.SendSignal(fireInRange ? Output : FalseOutput, "signal_out");
        }
    }
}
