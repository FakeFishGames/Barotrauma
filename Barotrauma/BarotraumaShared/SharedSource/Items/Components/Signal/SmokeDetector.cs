using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SmokeDetector : ItemComponent
    {
        const float FireCheckInterval = 1.0f;
        private float fireCheckTimer;

        private bool fireInRange;

        [InGameEditable, Serialize("1", true, description: "The signal the item outputs when it has detected movement.", alwaysUseInstanceValues: true)]
        public string Output { get; set; }

        [InGameEditable, Serialize("0", true, description: "The signal the item outputs when it has not detected movement.", alwaysUseInstanceValues: true)]
        public string FalseOutput { get; set; }

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
            item.SendSignal(0, fireInRange ? "1" : "0", "signal_out", null);
        }
    }
}
