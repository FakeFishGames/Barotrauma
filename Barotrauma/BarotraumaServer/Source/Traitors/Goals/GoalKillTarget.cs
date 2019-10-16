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

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]", "[causeofdeath]", "[roomname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] 
            { Target?.Name ?? "(unknown)", GetCauseOfDeath(), TextManager.Get($"roomname.{requiredRoom}") });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && character == Target);

            private CauseOfDeathType requiredCauseOfDeath;
            private string afflictionId;
            private string requiredRoom;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = DoesDeathMatchCriteria();
            }

            private bool DoesDeathMatchCriteria()
            {
                if (Target == null || !Target.IsDead || Target.CauseOfDeath == null) return false;

                bool typeMatch = false;

                // No specified cause of death required
                if (requiredCauseOfDeath == CauseOfDeathType.Unknown)
                {
                    typeMatch = true;
                }
                else
                {
                    switch (Target.CauseOfDeath.Type)
                    {
                        // If a cause of death is labeled as unknown, side with the traitor and accept this regardless of the required type
                        case CauseOfDeathType.Unknown:
                            typeMatch = true;
                            break;
                        case CauseOfDeathType.Pressure:
                        case CauseOfDeathType.Suffocation:
                        case CauseOfDeathType.Drowning:
                            typeMatch = requiredCauseOfDeath == Target.CauseOfDeath.Type;
                            break;
                        case CauseOfDeathType.Affliction:
                            typeMatch = Target.CauseOfDeath.Type == requiredCauseOfDeath && Target.CauseOfDeath.Affliction.Identifier == afflictionId;
                            break;
                        case CauseOfDeathType.Disconnected:
                            typeMatch = false;
                            break;
                    }
                }

                if (requiredRoom != string.Empty)
                {
                    if (Target.CurrentHull != null)
                    {
                        return typeMatch && Target.CurrentHull.RoomName == requiredRoom || Target.CurrentHull.RoomName.Contains(requiredRoom);
                    }
                    else
                    {
                        // Outside the submarine, not supported for now
                        return false;
                    }
                }
                else
                {
                    return typeMatch;
                }
            }

            private string GetCauseOfDeath()
            {
                if (requiredCauseOfDeath != CauseOfDeathType.Affliction || afflictionId == string.Empty)
                {
                    return requiredCauseOfDeath.ToString().ToLower();
                }
                else
                {
                    return TextManager.Get($"afflictionname.{afflictionId}").ToLower();
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
                Filter = filter;
                this.requiredCauseOfDeath = requiredCauseOfDeath;
                this.afflictionId = afflictionId;
                this.requiredRoom = requiredRoom;

                if (this.requiredCauseOfDeath == CauseOfDeathType.Unknown && this.requiredRoom == string.Empty)
                {
                    InfoTextId = "TraitorGoalKillTargetInfo";
                }
                else if (this.requiredCauseOfDeath != CauseOfDeathType.Unknown && this.requiredRoom == string.Empty)
                {
                    InfoTextId = "TraitorGoalKillTargetInfoWithCause";
                }
                else if (this.requiredCauseOfDeath == CauseOfDeathType.Unknown && this.requiredRoom != string.Empty)
                {
                    InfoTextId = "TraitorGoalKillTargetInfoWithRoom";
                }
                else if (this.requiredCauseOfDeath != CauseOfDeathType.Unknown && this.requiredRoom != string.Empty)
                {
                    InfoTextId = "TraitorGoalKillTargetInfoWithCauseAndRoom";
                }
            }
        }
    }
}