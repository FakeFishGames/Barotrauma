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
            float xDist = Math.Abs(character.WorldPosition.X - Leak.WorldPosition.X);
            float yDist = Math.Abs(character.WorldPosition.Y - Leak.WorldPosition.Y);
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally).
            // If the target is close, ignore the distance factor alltogether so that we keep fixing the leaks that are nearby.
            float distanceFactor = xDist < 200 && yDist < 100 ? 1 : MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, xDist + yDist * 2.0f));
            float severity = AIObjectiveFixLeaks.GetLeakSeverity(Leak) / 100;
            float max = Math.Min((AIObjectiveManager.OrderPriority - 1), 90);
            float devotion = Math.Min(Priority, 10) / 100;
            return MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + severity * distanceFactor * PriorityModifier, 0, 1));
        }

        protected override void Act(float deltaTime)
        {
            var weldingTool = character.Inventory.FindItemByTag("weldingtool", true);
            if (weldingTool == null)
            {
                TryAddSubObjective(ref getWeldingTool, () => new AIObjectiveGetItem(character, "weldingtool", objectiveManager, true), 
                    onAbandon: () => Abandon = true,
                    onCompleted: () => RemoveSubObjective(ref getWeldingTool));
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
                    Abandon = true;
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
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, "weldingfueltank", weldingTool.GetComponent<ItemContainer>(), objectiveManager), 
                        onAbandon: () => Abandon = true,
                        onCompleted: () => RemoveSubObjective(ref refuelObjective));
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
                Abandon = true;
                return;
            }
            Vector2 toLeak = Leak.WorldPosition - character.WorldPosition;
            // TODO: use the collider size/reach?
            if (!character.AnimController.InWater && Math.Abs(toLeak.X) < 100 && toLeak.Y < 0.0f && toLeak.Y > -150)
            {
                HumanAIController.AnimController.Crouching = true;
            }
            float reach = repairTool.Range + ConvertUnits.ToDisplayUnits(((HumanoidAnimController)character.AnimController).ArmLength);
            bool canOperate = toLeak.LengthSquared() < reach * reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak), 
                    onAbandon: () => Abandon = true,
                    onCompleted: () =>
                    {
                        if (!Check())
                        {
                            // Failed to operate. Probably too far.
                            Abandon = true;
                        }
                        else
                        {
                            RemoveSubObjective(ref operateObjective);
                        }
                    });
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(Leak, character, objectiveManager)
                {
                    CloseEnough = reach
                }, 
                onAbandon: () =>
                {
                    // If we are almost there, we can try to operate.
                    if ((Leak.WorldPosition - character.WorldPosition).LengthSquared() > reach * reach * 2)
                    {
                        Abandon = true;
                    }
                    else
                    {
                        RemoveSubObjective(ref gotoObjective);
                    }
                },
                onCompleted: () => RemoveSubObjective(ref gotoObjective));
            }
        }
    }
}
