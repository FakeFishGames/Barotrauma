using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalWithTimeLimit : Goal
        {
            private readonly Goal goal;
            private readonly float timeLimit;

            public override IEnumerable<string> StatusTextKeys => goal.StatusTextKeys;
            public override IEnumerable<string> StatusTextValues => new string[] { InfoText, TextManager.Get(IsCompleted ? "done" : "pending") };

            public override IEnumerable<string> InfoTextKeys => goal.InfoTextKeys.Concat(new string[] { "[timelimit]" });
            public override IEnumerable<string> InfoTextValues => goal.InfoTextValues.Concat(new string[] { string.Format("{0:0}", timeLimit) });

            public override IEnumerable<string> CompletedTextKeys => goal.CompletedTextKeys;
            public override IEnumerable<string> CompletedTextValues => goal.CompletedTextValues;

            public override string InfoText => TextManager.GetWithVariables(InfoTextId, InfoTextKeys.ToArray(), InfoTextValues.ToArray());

            public override bool IsCompleted => goal.IsCompleted;
            public override bool IsStarted => base.IsStarted && goal.IsStarted;
            public override bool CanBeCompleted => base.CanBeCompleted && (!IsStarted || timeRemaining > 0.0f);

            private float timeRemaining = 0.0f;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                goal.Update(deltaTime);
                timeRemaining = System.Math.Max(0.0f, timeRemaining - deltaTime);
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                if (!goal.Start(traitor))
                {
                    return false;
                }
                timeRemaining = timeLimit;
                return true;
            }

            public GoalWithTimeLimit(Goal goal, float timeLimit) : base()
            {
                this.goal = goal;
                this.timeLimit = timeLimit;
                StatusTextId = goal.StatusTextId;
                InfoTextId = goal.InfoTextId;
                CompletedTextId = goal.CompletedTextId;
            }

        }
    }
}
