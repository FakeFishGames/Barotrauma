using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIObjective
    {
        const float UpdateGapListInterval = 10.0f;

        private float updateGapListTimer;

        private AIObjectiveIdle idleObjective;

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
                objectiveList[objectiveList.Count - 1].TryComplete(deltaTime);

                if (!objectiveList[objectiveList.Count - 1].CanBeCompleted ||
                    objectiveList[objectiveList.Count - 1].IsCompleted())
                {
                    objectiveList.RemoveAt(objectiveList.Count - 1);
                }
            }
            else
            {
                if (idleObjective == null) idleObjective = new AIObjectiveIdle(character);
                idleObjective.TryComplete(deltaTime);
            }
        }

        private void UpdateGapList()
        {
            objectiveList.Clear();
            
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.ConnectedWall == null) continue;
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) continue;

                float gapPriority = GetGapFixPriority(gap);

                int index = 0;
                while (index < objectiveList.Count &&
                    GetGapFixPriority(objectiveList[index].Leak) < gapPriority)
                {
                    index++;
                }

                objectiveList.Insert(index, new AIObjectiveFixLeak(gap, character));
            }
        }

        private float GetGapFixPriority(Gap gap)
        {
            if (gap == null) return 0.0f;

            //larger gap -> higher priority
            float gapPriority = (gap.isHorizontal ? gap.Rect.Width : gap.Rect.Height) * gap.Open;

            //prioritize gaps that are close
            gapPriority /= Math.Max(Vector2.Distance(character.WorldPosition, gap.WorldPosition), 1.0f);

            //gaps to outside are much higher priority
            if (!gap.IsRoomToRoom) gapPriority *= 10.0f;

            return gapPriority;

        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveFixLeaks;
        }
    }
}
