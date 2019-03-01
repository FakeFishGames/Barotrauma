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

        private float ignoreListClearInterval = 60;
        private float ignoreListTimer;

        private AIObjectiveFindDivingGear findDivingGear;
        private List<Gap> gaps = new List<Gap>();
        private Dictionary<Gap, AIObjectiveFixLeak> fixObjectives = new Dictionary<Gap, AIObjectiveFixLeak>();
        private HashSet<Gap> ignoreList = new HashSet<Gap>();

        public AIObjectiveFixLeaks(Character character) : base (character, "")
        {
            UpdateObjectiveList();
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override void UpdatePriority(AIObjectiveManager objectiveManager, float deltaTime)
        {
            if (ignoreListTimer > ignoreListClearInterval)
            {
                ignoreList.Clear();
                ignoreListTimer = 0;
            }
            else
            {
                ignoreListTimer += deltaTime;
            }
            if (updateCounter < updateGapListInterval)
            {
                updateCounter += deltaTime;
            }
            else
            {
                UpdateObjectiveList();
            }
            priority = 0.0f;
            if (character.Submarine == null)
            {
                // Don't fix leaks when outside, should go back inside instead.
                // The ai should not go out on own -> the player has controlled the character and left it here.
                priority = 0;
            }
            else
            {
                foreach (Gap gap in gaps)
                {
                    // Gaps from outside to inside significantly increase the priority 
                    if (!gap.IsRoomToRoom)
                    {
                        // Max 50 priority per gap
                        priority = Math.Max(priority + gap.Open * 100.0f, 50.0f);
                    }
                    else
                    {
                        // Max 10 priority per gap
                        priority += gap.Open * 10.0f;
                    }

                    if (priority >= 100.0f) break;
                }
            }
            priority = MathHelper.Clamp(priority, 0, 100);
        }

        private bool cannotFindDivingGear;
        protected override void Act(float deltaTime)
        {
            if (gaps.None()) { return; }
            foreach (var objective in fixObjectives)
            {
                if (!objective.Value.CanBeCompleted)
                {
                    ignoreList.Add(objective.Key);
                }
            }
            SyncRemovedObjectives(fixObjectives, gaps);
            if (fixObjectives.None())
            {
                // Objectives not yet created.
                if (!cannotFindDivingGear && gaps.Any(g => g.IsRoomToRoom))
                {
                    if (findDivingGear == null)
                    {
                        findDivingGear = new AIObjectiveFindDivingGear(character, true);
                        if (!findDivingGear.IsCompleted())
                        {
                            findDivingGear.TryComplete(deltaTime);
                        }
                    }
                    if (!findDivingGear.CanBeCompleted)
                    {
                        cannotFindDivingGear = true;
                    }
                }
                if (findDivingGear == null || findDivingGear.IsCompleted() || cannotFindDivingGear)
                {
                    foreach (var gap in gaps)
                    {
                        if (!fixObjectives.TryGetValue(gap, out AIObjectiveFixLeak objective))
                        {
                            objective = new AIObjectiveFixLeak(gap, character);
                            fixObjectives.Add(gap, objective);
                            AddSubObjective(objective);
                        }
                    }
                }
            }
        }

        private void UpdateObjectiveList()
        {
            updateCounter = 0;
            gaps.Clear();
            foreach (Gap gap in Gap.GapList)
            {
                if (ignoreList.Contains(gap)) { continue; }
                if (gap.ConnectedWall == null) { continue; }
                // Door or not open
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) { continue; }
                // Not linked to a hull -> ignore
                if (gap.linkedTo.All(l => l == null)) { continue; }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(gap, true)) { continue; }
                    if (cannotFindDivingGear && gap.IsRoomToRoom) { continue; }
                    if (!gaps.Contains(gap))
                    {
                        gaps.Add(gap);
                    }
                }
            }
            gaps.Sort((x, y) => GetGapFixPriority(y).CompareTo(GetGapFixPriority(x)));
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
