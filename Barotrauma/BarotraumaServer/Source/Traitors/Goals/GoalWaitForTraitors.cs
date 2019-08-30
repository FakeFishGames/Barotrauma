using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalWaitForTraitors : Goal
        {
            private readonly int requiredCount;
            private int count = 0;

            public override bool IsCompleted => count >= requiredCount;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[remaining]", "[count]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { $"{requiredCount - count}", $"{requiredCount}" });

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                ++count;
                return true;
            }

            public GoalWaitForTraitors(int requiredCount) : base()
            {
                this.requiredCount = requiredCount;
                InfoTextId = "TraitorGoalWaitForTraitorsInfoText";
            }
        }
    }
}
