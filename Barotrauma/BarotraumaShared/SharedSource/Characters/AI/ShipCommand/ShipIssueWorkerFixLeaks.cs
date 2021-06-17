using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ShipGlobalIssueFixLeaks : ShipGlobalIssue
    {
        readonly List<float> hullSeverities = new List<float>();
        public ShipGlobalIssueFixLeaks(ShipCommandManager shipCommandManager) : base(shipCommandManager) { }
        public override void CalculateGlobalIssue()
        {
            hullSeverities.Clear();

            foreach (Gap gap in Gap.GapList)
            {
                if (AIObjectiveFixLeaks.IsValidTarget(gap, shipCommandManager.character))
                {
                    hullSeverities.Add(AIObjectiveFixLeaks.GetLeakSeverity(gap));
                }
            }

            float averagePercentage = 0f;
            if (hullSeverities.Any())
            {
                hullSeverities.Sort();
                averagePercentage = hullSeverities.TakeLast(3).Average(); // get the 3 most damaged items on the ship and get their average
            }
            GlobalImportance = averagePercentage;
        }
    }

    class ShipIssueWorkerFixLeaks : ShipIssueWorkerGlobal
    {
        public override bool StopDuringEmergency => false;
        public ShipIssueWorkerFixLeaks(ShipCommandManager shipCommandManager, Order order, ShipGlobalIssueFixLeaks shipGlobalIssueFixLeaks) : base(shipCommandManager, order, shipGlobalIssueFixLeaks) { }
    }
}
