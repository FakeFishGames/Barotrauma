using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalRandom : Goal
        {
            private readonly List<Goal> allGoals;

            private readonly List<Goal> selectedGoals = new List<Goal>();

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { Target?.Name ?? "(unknown)" });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && character == Target);

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = Target?.IsDead ?? false;
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                Target = traitor.Mission.FindKillTarget(traitor.Character, Filter);
                return Target != null && !Target.IsDead;
            }

            public GoalRandom(params Goal[] goals, int count)
            {
                this.goals = goals;
            }
        }
    }
}