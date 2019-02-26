using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIObjective
    {
        public override string DebugTag => "fix leaks";

        const float updateGapListInterval = 3.0f;
        private float updateCounter;

        private AIObjectiveIdle idleObjective;

        private AIObjectiveFindDivingGear findDivingGear;

        private List<AIObjectiveFixLeak> objectiveList = new List<AIObjectiveFixLeak>();

        public AIObjectiveFixLeaks(Character character) : base (character, "")
        {
            UpdateGapList();
        }

        public override bool IsCompleted() => false;

        public override void UpdatePriority(AIObjectiveManager objectiveManager, float deltaTime)
        {
            if (updateCounter < updateGapListInterval)
            {
                updateCounter += deltaTime;
            }
            else
            {
                UpdateGapList();
            }
            priority = 0.0f;
            foreach (AIObjectiveFixLeak fixObjective in objectiveList)
            {
                // Gaps from outside to inside significantly increase the priority 
                if (!fixObjective.Leak.IsRoomToRoom)
                {
                    // Max 50 priority per gap
                    priority = Math.Max(priority + fixObjective.Leak.Open * 100.0f, 50.0f);
                }
                else
                {
                    // Max 10 priority per gap
                    priority += fixObjective.Leak.Open * 10.0f;
                }

                if (priority >= 100.0f) break;
            }
            priority = MathHelper.Clamp(priority, 0, 100);
        }

        protected override void Act(float deltaTime)
        {
            if (objectiveList.Any())
            {
                if (!objectiveList[objectiveList.Count - 1].Leak.IsRoomToRoom)
                {
                    if (findDivingGear == null) findDivingGear = new AIObjectiveFindDivingGear(character, true);

                    if (!findDivingGear.IsCompleted() && findDivingGear.CanBeCompleted)
                    {
                        findDivingGear.TryComplete(deltaTime);
                        return;
                    }
                }

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
            updateCounter = 0;
            objectiveList.Clear(); 
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.ConnectedWall == null) { continue; }
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) { continue; }
                //not linked to a hull -> ignore
                if (gap.linkedTo.All(l => l == null)) { continue; }

                if (character.TeamID == 0)
                {
                    if (gap.Submarine == null) continue;
                }
                else
                {
                    //prevent characters from attempting to fix leaks in the enemy sub
                    //team 1 plays in sub 0, team 2 in sub 1
                    Submarine mySub = character.TeamID < 1 || character.TeamID > Submarine.MainSubs.Length ?
                        Submarine.MainSub : Submarine.MainSubs[character.TeamID - 1];

                    if (gap.Submarine != mySub) continue;
                }

                float gapPriority = GetGapFixPriority(gap);

                int index = 0;
                while (index < objectiveList.Count && GetGapFixPriority(objectiveList[index].Leak) < gapPriority)
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
            float gapPriority = (gap.IsHorizontal ? gap.Rect.Width : gap.Rect.Height) * gap.Open;

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
