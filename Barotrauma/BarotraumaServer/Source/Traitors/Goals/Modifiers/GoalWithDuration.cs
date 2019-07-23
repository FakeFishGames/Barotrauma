using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalWithDuration : Goal
        {
            private readonly Goal goal;
            private readonly float requiredDuration;
            private readonly bool countTotalDuration;

            public override IEnumerable<string> StatusTextKeys => goal.StatusTextKeys;
            public override IEnumerable<string> StatusTextValues => new string[] { InfoText, TextManager.Get(IsCompleted ? "done" : "pending") };

            public override IEnumerable<string> InfoTextKeys => goal.InfoTextKeys.Concat(new string[] { "[duration]" });
            public override IEnumerable<string> InfoTextValues => goal.InfoTextValues.Concat(new string[] { string.Format("{0:0}", requiredDuration) });

            public override IEnumerable<string> CompletedTextKeys => goal.CompletedTextKeys;
            public override IEnumerable<string> CompletedTextValues => goal.CompletedTextValues;

            public override string InfoText => TextManager.GetWithVariables(InfoTextId, InfoTextKeys.ToArray(), InfoTextValues.ToArray());

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;
            public override bool IsStarted => base.IsStarted && goal.IsStarted;

            private float remainingDuration = float.NaN;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                goal.Update(deltaTime);
                if (goal.IsCompleted)
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

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                return goal.Start(server, traitor);
            }

            public GoalWithDuration(Goal goal, float requiredDuration, bool countTotalDuration) : base()
            {
                this.goal = goal;
                this.requiredDuration = requiredDuration;
                this.countTotalDuration = countTotalDuration;
                StatusTextId = goal.StatusTextId;
                InfoTextId = goal.InfoTextId;
                CompletedTextId = goal.CompletedTextId;
            }
        }
    }
}

