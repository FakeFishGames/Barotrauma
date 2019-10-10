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

            private CauseOfDeathType requiredCauseOfDeath;
            private string afflictionId = string.Empty;
            private string requiredRoom = string.Empty;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = DoesDeathMatchCriteria();
            }

            private bool DoesDeathMatchCriteria()
            {
                if (Target == null || !Target.IsDead || Target.CauseOfDeath == null) return false;

                bool typeMatch = false;
                switch (Target.CauseOfDeath.Type)
                {
                    case CauseOfDeathType.Unknown:
                        typeMatch = requiredCauseOfDeath == CauseOfDeathType.Unknown;
                        break;
                    case CauseOfDeathType.Pressure:
                    case CauseOfDeathType.Suffocation:
                    case CauseOfDeathType.Drowning:
                        typeMatch = requiredCauseOfDeath == Target.CauseOfDeath.Type || requiredCauseOfDeath == CauseOfDeathType.Unknown;
                        break;
                    case CauseOfDeathType.Affliction:
                        typeMatch = Target.CauseOfDeath.Type == requiredCauseOfDeath && Target.CauseOfDeath.Affliction.Identifier == afflictionId || requiredCauseOfDeath == CauseOfDeathType.Unknown;
                        break;
                    case CauseOfDeathType.Disconnected:
                        typeMatch = false;
                        break;
                }

                if (requiredRoom != string.Empty)
                {
                    return typeMatch && Target.CurrentHull.RoomName == requiredRoom;
                }
                else
                {
                    return typeMatch;
                }
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

            public GoalKillTarget(TraitorMission.CharacterFilter filter, CauseOfDeathType requiredCauseOfDeath, string afflictionId, string requiredRoom) : base()
            {
                InfoTextId = "TraitorGoalKillTargetInfo";
                this.requiredCauseOfDeath = requiredCauseOfDeath;
                this.afflictionId = afflictionId;
                this.requiredRoom = requiredRoom;
                Filter = filter;
            }
        }
    }
}