using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalFloodPercentOfSub : Goal
        {
            private readonly float minimumFloodingAmount;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[percentage]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:0}", minimumFloodingAmount * 100.0f) });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                var validHullsCount = 0;
                var floodingAmount = 0.0f;
                foreach (Hull hull in Hull.hullList)
                {
                    if (hull.Submarine == null || hull.Submarine.IsOutpost || hull.Submarine.TeamID != Traitor.Character.TeamID) { continue; }
                    ++validHullsCount;
                    floodingAmount += hull.WaterVolume / hull.Volume;
                }
                if (validHullsCount > 0)
                {
                    floodingAmount /= validHullsCount;
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
