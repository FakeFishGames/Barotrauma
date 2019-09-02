using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFixLeak : AIObjective
    {
        public override string DebugTag => "fix leak";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        public Gap Leak { get; private set; }

        private AIObjectiveFindDivingGear findDivingGear;
        private AIObjectiveGetItem getWeldingTool;
        private AIObjectiveContainItem refuelObjective;
        private AIObjectiveGoTo gotoObjective;
        private AIObjectiveOperateItem operateObjective;

        public AIObjectiveFixLeak(Gap leak, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base (character, objectiveManager, priorityModifier)
        {
            Leak = leak;
        }

        protected override bool Check() => Leak.Open <= 0.0f || Leak.Removed;

        public override float GetPriority()
        {
            if (Leak.Open == 0.0f) { return 0.0f; }
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
            float dist = Math.Abs(character.WorldPosition.X - Leak.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - Leak.WorldPosition.Y) * 2.0f;
            float distanceFactor = MathHelper.Lerp(1, 0.25f, MathUtils.InverseLerp(0, 10000, dist));
            float severity = AIObjectiveFixLeaks.GetLeakSeverity(Leak);
            float max = Math.Min((AIObjectiveManager.OrderPriority - 1), 90);
            float devotion = Math.Min(Priority, 10) / 100;
            return MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + severity * distanceFactor * PriorityModifier, 0, 1));
        }

        protected override void Act(float deltaTime)
        {
            if (!Leak.IsRoomToRoom)
            {
                if (!HumanAIController.HasDivingSuit(character))
                {
                    TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, true, objectiveManager));
                    return;
                }
            }
            var weldingTool = character.Inventory.FindItemByTag("weldingtool", true);
            if (weldingTool == null)
            {
                TryAddSubObjective(ref getWeldingTool, () => new AIObjectiveGetItem(character, "weldingtool", objectiveManager, true));
                return;
            }
            else
            {
                var containedItems = weldingTool.ContainedItems;
                if (containedItems == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no proper inventory");
#endif
                    abandon = true;
                    return;
                }
                // Drop empty tanks
                foreach (Item containedItem in containedItems)
                {
                    if (containedItem == null) { continue; }
                    if (containedItem.Condition <= 0.0f)
                    {
                        containedItem.Drop(character);
                    }
                }
                if (containedItems.None(i => i.HasTag("weldingfueltank") && i.Condition > 0.0f))
                {
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, "weldingfueltank", weldingTool.GetComponent<ItemContainer>(), objectiveManager));
                    return;
                }
            }
            if (subObjectives.Any()) { return; }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError($"{character.Name}: AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
#endif
                abandon = true;
                return;
            }
            Vector2 gapDiff = Leak.WorldPosition - character.WorldPosition;
            // TODO: use the collider size/reach?
            if (!character.AnimController.InWater && Math.Abs(gapDiff.X) < 100 && gapDiff.Y < 0.0f && gapDiff.Y > -150)
            {
                HumanAIController.AnimController.Crouching = true;
            }
            // Use a greater reach, because the distance is calculated from the character to the leak, not from the item to the leak.
            float reach = repairTool.Range + ((HumanoidAnimController)character.AnimController).ArmLength;
            bool canOperate = gapDiff.LengthSquared() < reach * reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(Leak, character, objectiveManager)
                {
                    CloseEnough = reach
                });
            }
        }
    }
}
