using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalHasTimeLimit : Modifier
        {
            private readonly float timeLimit;
            private readonly string timeLimitInfoTextId;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[timelimit]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { $"{TimeSpan.FromSeconds(timeLimit):g}" });

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values)
            {
                var infoText = base.GetInfoText(traitor, textId, keys, values);
                return !string.IsNullOrEmpty(timeLimitInfoTextId) ? TextManager.FormatServerMessage(timeLimitInfoTextId, new[] { "[infotext]", "[timelimit]" }, new[] { infoText, $"{TimeSpan.FromSeconds(timeLimit):g}" }) : infoText;
            }

            public override bool CanBeCompleted => base.CanBeCompleted && (!Traitors.Any(IsStarted) || timeRemaining > 0.0f);

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

            public GoalHasTimeLimit(Goal goal, float timeLimit, string timeLimitInfoTextId) : base(goal)
            {
                this.timeLimit = timeLimit;
                this.timeLimitInfoTextId = timeLimitInfoTextId;
            }
        }
    }
}
