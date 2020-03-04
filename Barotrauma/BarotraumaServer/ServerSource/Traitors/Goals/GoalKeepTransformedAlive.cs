using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalKeepTransformedAlive : Goal
        {
            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[speciesname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { targetCharacterName });

            public override bool IsCompleted => isCompleted;
            private bool isCompleted;

            private const float gracePeriod = 1f;
            private string speciesId;
            private string targetCharacterName;
            private Character targetCharacter;
            private float timer;

            public override bool CanBeCompleted(ICollection<Traitor> traitors)
            {
                return timer < gracePeriod || targetCharacter != null && !targetCharacter.IsDead;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                if (timer <= gracePeriod)
                {
                    timer += deltaTime;
                }

                isCompleted = targetCharacter != null && !targetCharacter.IsDead && timer >= gracePeriod;
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }

                var startTime = Timing.TotalTime;

                foreach (Character character in Character.CharacterList)
                {
                    if (character.Submarine == null || Traitors.All(t => character.Submarine.TeamID != t.Character.TeamID) || character.SpawnTime + gracePeriod < startTime)
                    {
                        continue;
                    }
                    if (character.SpeciesName.Equals(speciesId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetCharacter = character;
                        break;
                    }
                }

                targetCharacterName = TextManager.FormatServerMessage($"character.{speciesId}").ToLowerInvariant();

                return targetCharacter != null;
            }

            public GoalKeepTransformedAlive(string speciesId) : base()
            {
                this.speciesId = speciesId.ToLowerInvariant();
            }
        }
    }
}
