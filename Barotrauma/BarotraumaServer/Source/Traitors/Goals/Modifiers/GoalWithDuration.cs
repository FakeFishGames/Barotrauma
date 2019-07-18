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
            public override IEnumerable<string> StatusTextValues => goal.StatusTextValues;

            public override IEnumerable<string> InfoTextKeys => goal.InfoTextKeys.Concat(new string[] { "[duration]" }).ToArray();
            public override IEnumerable<string> InfoTextValues => goal.InfoTextValues.Concat(new string[] { string.Format("{0:f}", requiredDuration) }).ToArray();

            public override IEnumerable<string> CompletedTextKeys => goal.CompletedTextKeys;
            public override IEnumerable<string> CompletedTextValues => goal.CompletedTextValues;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private float remainingDuration = float.NaN;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
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

