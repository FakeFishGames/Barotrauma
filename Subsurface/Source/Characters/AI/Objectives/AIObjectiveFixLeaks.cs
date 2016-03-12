using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIObjective
    {
        const float UpdateGapListInterval = 10.0f;

        private float updateGapListTimer;

        private List<AIObjectiveFixLeak> objectiveList;

        public AIObjectiveFixLeaks(Character character)
            : base (character, "")
        {
            objectiveList = new List<AIObjectiveFixLeak>();
        }

        public override bool IsCompleted()
        {
            return false;
        }

        protected override void Act(float deltaTime)
        {
            updateGapListTimer -= deltaTime;

            if (updateGapListTimer<=0.0f)
            {
                UpdateGapList();

                updateGapListTimer = UpdateGapListInterval;
            }

            if (objectiveList.Any())
            {
                objectiveList[objectiveList.Count-1].TryComplete(deltaTime);

                if (!objectiveList[objectiveList.Count-1].CanBeCompleted || objectiveList[objectiveList.Count-1].IsCompleted())
                {
                    objectiveList.RemoveAt(objectiveList.Count - 1);
                }
            }
        }

        private void UpdateGapList()
        {
            objectiveList.Clear();
            
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.IsRoomToRoom || gap.ConnectedDoor != null || gap.Open < 0.1f) continue;

                float dist = Vector2.DistanceSquared(character.WorldPosition, gap.WorldPosition);

                int index = 0;
                while (index<objectiveList.Count && 
                    Vector2.DistanceSquared(objectiveList[index].Leak.WorldPosition, character.WorldPosition)>dist)
                {
                    index++;
                }

                objectiveList.Insert(index, new AIObjectiveFixLeak(gap, character));
            }
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveFixLeaks;
        }
    }
}
