using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalInjectTarget : Goal
        {
            public TraitorMission.CharacterFilter Filter { get; private set; }
            public List<Character> Targets { get; private set; }

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]", "[poison]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { traitor.Mission.GetTargetNames(Targets) ?? "(unknown)", poisonName });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (!isCompleted && Targets.Contains(character));

            private string poisonId;
            private string afflictionId;
            private string poisonName;
            private int targetCount;
            private float targetPercentage;
            private bool[] targetWasInfected;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = WereAllTargetsInfected();
            }

            private bool WereAllTargetsInfected()
            {
                for (int i = 0; i < targetWasInfected.Length; i++)
                {
                    if (targetWasInfected[i]) continue;
                    targetWasInfected[i] = Targets[i].CharacterHealth.GetAffliction(afflictionId) != null;
                    if (!targetWasInfected[i]) return false;
                }

                return true;
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                poisonName = TextManager.FormatServerMessage(poisonId) ?? poisonId;

                Targets = traitor.Mission.FindKillTarget(traitor.Character, Filter, targetCount, targetPercentage);
                targetWasInfected = new bool[Targets.Count];
                return Targets != null && !Targets.All(t => t.IsDead);
            }

            public GoalInjectTarget(TraitorMission.CharacterFilter filter, string poisonId, string afflictionId, int targetCount, float targetPercentage) : base()
            {
                InfoTextId = "TraitorGoalPoisonInfo";
                Filter = filter;
                this.poisonId = poisonId;
                this.afflictionId = afflictionId;
                this.targetCount = targetCount;
                this.targetPercentage = targetPercentage;
            }
        }
    }
}