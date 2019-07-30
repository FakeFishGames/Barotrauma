using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalKillTarget : Goal
        {
            public TraitorMission.CharacterFilter Filter { get; private set; }
            public Character Target { get; private set; }

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { Target?.Name ?? "(unknown)" });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && character == Target);

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = Target?.IsDead ?? false;
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                Target = traitor.Mission.FindKillTarget(server, traitor.Character, Filter);
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