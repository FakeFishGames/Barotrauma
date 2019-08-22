using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalHasDuration : Modifier
        {
            private const string GoalWithDurationInfoTextId = "TraitorGoalWithDurationInfoText";
            private const string GoalWithCumulativeDurationInfoTextId = "TraitorGoalWithCumulativeDurationInfoText";

            private readonly float requiredDuration;
            private readonly bool countTotalDuration;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[duration]" });

            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { $"{TimeSpan.FromSeconds(requiredDuration):g}" });

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => TextManager.FormatServerMessage(countTotalDuration ? GoalWithCumulativeDurationInfoTextId : GoalWithDurationInfoTextId, new []
            {
                "[infotext]",
                "[duration]"
            }, new []
            {
                base.GetInfoText(traitor, textId, keys, values),
                $"{TimeSpan.FromSeconds(requiredDuration):g}"
            });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private float remainingDuration = float.NaN;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (Goal.IsCompleted)
                {
                    if (!float.IsNaN(remainingDuration))
                    {
                        remainingDuration -= deltaTime;
                    }
                    else
                    {
                        remainingDuration = requiredDuration;
                    }
                    isCompleted |= remainingDuration <= 0.0f;
                }
                else if (!countTotalDuration)
                {
                    remainingDuration = float.NaN;
                }
            }

            public GoalHasDuration(Goal goal, float requiredDuration, bool countTotalDuration) : base(goal)
            {
                this.requiredDuration = requiredDuration;
                this.countTotalDuration = countTotalDuration;
            }
        }
    }
}

