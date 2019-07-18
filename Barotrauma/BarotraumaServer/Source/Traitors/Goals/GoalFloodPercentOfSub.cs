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
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:f}", minimumFloodingAmount * 100.0f) });

            public override bool IsCompleted => GameMain.GameSession.EventManager.CurrentFloodingAmount >= minimumFloodingAmount;

            public GoalFloodPercentOfSub(float minimumFloodingAmount) : base()
            {
                InfoTextId = "TraitorGoalFloodPercentOfSub";
                this.minimumFloodingAmount = minimumFloodingAmount;
            }
        }

    }
}
