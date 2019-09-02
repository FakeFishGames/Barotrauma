using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalKillTarget : Goal
        {
            public TraitorMission.CharacterFilter Filter { get; private set; }
            public Character Target { get; private set; }

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { Target?.Name ?? "(unknown)" });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && character == Target);

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

            public GoalKillTarget(TraitorMission.CharacterFilter filter) : base()
            {
                InfoTextId = "TraitorGoalKillTargetInfo";
                Filter = filter;
            }
        }
    }
}