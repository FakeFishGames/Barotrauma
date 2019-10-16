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
            public List<Character> Targets { get; private set; }

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]", "[causeofdeath]", "[roomname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] 
            { traitor.Mission.GetTargetNames(Targets) ?? "(unknown)", GetCauseOfDeath(), TextManager.Get($"roomname.{requiredRoom}") });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && Targets.Contains(character));

            private CauseOfDeathType requiredCauseOfDeath;
            private string afflictionId;
            private string requiredRoom;
            private int targetCount;
            private float targetPercentage;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = DoesDeathMatchCriteria();
            }

            private bool DoesDeathMatchCriteria()
            {
                if (Targets == null || Targets.Any(t => !t.IsDead) || Targets.Any(t => t.CauseOfDeath == null)) return false;

                bool typeMatch = false;

                for (int i = 0; i < Targets.Count; i++)
                {
                    // No specified cause of death required
                    if (requiredCauseOfDeath == CauseOfDeathType.Unknown)
                    {
                        typeMatch = true;
                    }
                    else
                    {
                        switch (Targets[i].CauseOfDeath.Type)
                        {
                            // If a cause of death is labeled as unknown, side with the traitor and accept this regardless of the required type
                            case CauseOfDeathType.Unknown:
                                typeMatch = true;
                                break;
                            case CauseOfDeathType.Pressure:
                            case CauseOfDeathType.Suffocation:
                            case CauseOfDeathType.Drowning:
                                typeMatch = requiredCauseOfDeath == Targets[i].CauseOfDeath.Type;
                                break;
                            case CauseOfDeathType.Affliction:
                                typeMatch = Targets[i].CauseOfDeath.Type == requiredCauseOfDeath && Targets[i].CauseOfDeath.Affliction.Identifier == afflictionId;
                                break;
                            case CauseOfDeathType.Disconnected:
                                typeMatch = false;
                                break;
                        }
                    }

                    if (requiredRoom != string.Empty)
                    {
                        if (Targets[i].CurrentHull != null)
                        {
                            if (typeMatch && Targets[i].CurrentHull.RoomName == requiredRoom || Targets[i].CurrentHull.RoomName.Contains(requiredRoom))
                            {
                                continue;
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else
                        {
                            // Outside the submarine, not supported for now
                            return false;
                        }
                    }
                    else
                    {
                        if (typeMatch)
                        {
                            continue;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                return true;
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

                Targets = traitor.Mission.FindKillTarget(traitor.Character, Filter, targetCount, targetPercentage);
                return Targets != null && !Targets.All(t => t.IsDead);
            }

            public GoalKillTarget(TraitorMission.CharacterFilter filter, CauseOfDeathType requiredCauseOfDeath, string afflictionId, string requiredRoom, int targetCount, float targetPercentage) : base()
            {
                Filter = filter;
                this.requiredCauseOfDeath = requiredCauseOfDeath;
                this.afflictionId = afflictionId;
                this.requiredRoom = requiredRoom;
                this.targetCount = targetCount;
                this.targetPercentage = targetPercentage;

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