using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalFloodPercentOfSub : Goal
        {
            private readonly float minimumFloodingAmount;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[percentage]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:0}", minimumFloodingAmount * 100.0f) });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                var validHullsCount = Hull.hullList.Count(hull => hull.Submarine != null && !hull.Submarine.IsOutpost);
                var floodingAmount = 0.0f;
                foreach (Hull hull in Hull.hullList)
                {
                    if (hull.Submarine == null || hull.Submarine.IsOutpost) { continue; }
                    floodingAmount += hull.WaterVolume / hull.Volume / validHullsCount;
                }
                isCompleted = floodingAmount >= minimumFloodingAmount;
            }

            public GoalFloodPercentOfSub(float minimumFloodingAmount) : base()
            {
                InfoTextId = "TraitorGoalFloodPercentOfSub";
                this.minimumFloodingAmount = minimumFloodingAmount;
            }
        }

    }
}
