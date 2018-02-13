using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace Barotrauma
{
    /*class AIObjectiveRescue : AIObjective
    {
        private readonly Character targetCharacter;

        public AIObjectiveRescue(Character character, Character targetCharacter)
            : base (character, "")
        {
            Debug.Assert(character != targetCharacter);

            this.targetCharacter = targetCharacter;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveRescue rescueObjective = otherObjective as AIObjectiveRescue;
            return rescueObjective != null && rescueObjective.targetCharacter == targetCharacter;
        }

        public override float GetPriority(Character character)
        {
            if (targetCharacter.AnimController.CurrentHull == null) return 0.0f;

            float distance = Vector2.DistanceSquared(character.WorldPosition, targetCharacter.WorldPosition);

            return targetCharacter.IsDead ? 1000.0f / distance : 10000.0f / distance;
        }

    }*/
}
