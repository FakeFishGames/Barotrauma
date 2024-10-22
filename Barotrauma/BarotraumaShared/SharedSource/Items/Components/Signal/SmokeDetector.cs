﻿using System;

namespace Barotrauma.Items.Components
{
    class SmokeDetector : ItemComponent
    {
        const float FireCheckInterval = 1.0f;
        private float fireCheckTimer;

        public bool FireInRange { get; private set; }

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
        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal the item outputs when it has detected a fire.", alwaysUseInstanceValues: true)]
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
        [InGameEditable, Serialize("0", IsPropertySaveable.Yes, description: "The signal the item outputs when it has not detected a fire.", alwaysUseInstanceValues: true)]
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

        public SmokeDetector(Item item, ContentXElement element)
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
                    if (fireSource.IsInDamageRange(item.WorldPosition, Math.Max(fireSource.DamageRange * 2.0f, 500.0f))) { return true; }
                }
            }

            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            fireCheckTimer -= deltaTime;
            if (fireCheckTimer <= 0.0f)
            {
                FireInRange = IsFireInRange();
                fireCheckTimer = FireCheckInterval;
            }
            string signalOut = FireInRange ? Output : FalseOutput;
            if (!string.IsNullOrEmpty(signalOut)) { item.SendSignal(signalOut, "signal_out"); }           
        }
    }
}
