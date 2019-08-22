using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalHasTimeLimit : Modifier
        {
            private const string GoalWithTimeLimitInfoTextId = "TraitorGoalWithTimeLimitInfoText";

            private readonly float timeLimit;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[timelimit]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { $"{TimeSpan.FromSeconds(timeLimit):g}" });

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => TextManager.FormatServerMessage(GoalWithTimeLimitInfoTextId, new[]
            {
                "[infotext]",
                "[timelimit]"
            }, new[]
            {
                base.GetInfoText(traitor, textId, keys, values),
                $"{TimeSpan.FromSeconds(timeLimit):g}"
            });

            public override bool CanBeCompleted => base.CanBeCompleted && (!IsStarted || timeRemaining > 0.0f);

            private float timeRemaining;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                timeRemaining = System.Math.Max(0.0f, timeRemaining - deltaTime);
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                timeRemaining = timeLimit;
                return true;
            }

            public GoalHasTimeLimit(Goal goal, float timeLimit) : base(goal)
            {
                this.timeLimit = timeLimit;
            }
        }
    }
}
