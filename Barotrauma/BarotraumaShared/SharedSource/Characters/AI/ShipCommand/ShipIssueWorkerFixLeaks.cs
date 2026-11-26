using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ShipGlobalIssueFixLeaks : ShipGlobalIssue
    {
        /// <summary>
        /// Leak severity (0-100, see <see cref="AIObjectiveFixLeaks.GetLeakSeverity"/>) above which the issue is given a moderately large importance
        /// </summary>
        private const float LargeLeakSeverity = 50.0f;

        readonly List<float> hullSeverities = new List<float>();
        public ShipGlobalIssueFixLeaks(ShipCommandManager shipCommandManager) : base(shipCommandManager) { }
        public override void CalculateGlobalIssue()
        {
            hullSeverities.Clear();

            foreach (Gap gap in Gap.GapList)
            {
                if (AIObjectiveFixLeaks.IsValidTarget(gap, shipCommandManager.Commander))
                {
                    hullSeverities.Add(AIObjectiveFixLeaks.GetLeakSeverity(gap));
                }
            }

            float averagePercentage = 0f;
            if (hullSeverities.Any())
            {
                hullSeverities.Sort();
                averagePercentage = hullSeverities.TakeLast(3).Average(); // get the 3 most damaged wall sections on the ship and get their average
            }
            GlobalImportance = averagePercentage;
            //if there's any fairly large leaks, make sure we assign a moderately large importance
            if (hullSeverities.Any(h => h > LargeLeakSeverity))
            {
                GlobalImportance = MathHelper.Max(50.0f, GlobalImportance);
            }
        }
    }

    class ShipIssueWorkerFixLeaks : ShipIssueWorkerGlobal
    {
        public override bool StopIfHostilesOnBoard => false;
        public ShipIssueWorkerFixLeaks(ShipCommandManager shipCommandManager, Order order, ShipGlobalIssueFixLeaks shipGlobalIssueFixLeaks) : base(shipCommandManager, order, shipGlobalIssueFixLeaks) { }
    }
}
