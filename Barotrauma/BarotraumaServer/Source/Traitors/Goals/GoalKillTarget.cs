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

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]", "[causeofdeath]", "[targethullname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] 
            { traitor.Mission.GetTargetNames(Targets) ?? "(unknown)", GetCauseOfDeath(), targetHull != null ? TextManager.Get($"roomname.{targetHull}") : string.Empty });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && Targets.Contains(character));

            private CauseOfDeathType requiredCauseOfDeath;
            private string afflictionId;
            private string targetHull;
            private int targetCount;
            private float targetPercentage;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = DoesDeathMatchCriteria();
            }

            private bool DoesDeathMatchCriteria()
            {
                if (Targets == null || Targets.Any(t => !t.IsDead)) return false;

                bool typeMatch = false;

                for (int i = 0; i < Targets.Count; i++)
                {
                    // No specified cause of death required or missing cause of death
                    if (requiredCauseOfDeath == CauseOfDeathType.Unknown || Targets[i].CauseOfDeath == null)
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

                    if (targetHull != null)
                    {
                        if (Targets[i].CurrentHull != null)
                        {
                            if (typeMatch && Targets[i].CurrentHull.RoomName == targetHull || Targets[i].CurrentHull.RoomName.Contains(targetHull))
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

            public GoalKillTarget(TraitorMission.CharacterFilter filter, CauseOfDeathType requiredCauseOfDeath, string afflictionId, string targetHull, int targetCount, float targetPercentage) : base()
            {
                Filter = filter;
                this.requiredCauseOfDeath = requiredCauseOfDeath;
                this.afflictionId = afflictionId;
                this.targetHull = targetHull;
                this.targetCount = targetCount;
                this.targetPercentage = targetPercentage;

                if (targetPercentage < 1f)
                {
                    if (this.requiredCauseOfDeath == CauseOfDeathType.Unknown && targetHull == null)
                    {
                        InfoTextId = "traitorgoalkilltargetinfo";
                    }
                    else if (this.requiredCauseOfDeath != CauseOfDeathType.Unknown && targetHull == null)
                    {
                        InfoTextId = "traitorgoalkilltargetinfowithcause";
                    }
                    else if (this.requiredCauseOfDeath == CauseOfDeathType.Unknown && targetHull != null)
                    {
                        InfoTextId = "traitorgoalkilltargetinfowithhull";
                    }
                    else if (this.requiredCauseOfDeath != CauseOfDeathType.Unknown && targetHull != null)
                    {
                        InfoTextId = "traitorgoalkilltargetinfowithcauseandhull";
                    }
                }
                else
                {
                    InfoTextId = "traitorgoalkilleveryoneinfo";
                }
            }
        }
    }
}