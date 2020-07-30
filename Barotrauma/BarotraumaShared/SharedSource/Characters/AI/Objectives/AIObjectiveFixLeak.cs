using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

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

        public bool IgnoreSeverityAndDistance { get; private set; }

        public AIObjectiveFixLeak(Gap leak, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool ignoreSeverityAndDistance = false) : base (character, objectiveManager, priorityModifier)
        {
            Leak = leak;
            IgnoreSeverityAndDistance = ignoreSeverityAndDistance;
        }

        protected override bool Check() => Leak.Open <= 0 || Leak.Removed;

        public override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                return Priority;
            }
            if (Leak.Removed || Leak.Open <= 0)
            {
                Priority = 0;
            }
            else
            {
                float xDist = Math.Abs(character.WorldPosition.X - Leak.WorldPosition.X);
                float yDist = Math.Abs(character.WorldPosition.Y - Leak.WorldPosition.Y);
                // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally).
                // If the target is close, ignore the distance factor alltogether so that we keep fixing the leaks that are nearby.
                float distanceFactor = IgnoreSeverityAndDistance || xDist < 200 && yDist < 100 ? 1 : MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 5000, xDist + yDist * 3.0f));
                float severity = IgnoreSeverityAndDistance ? 1 : AIObjectiveFixLeaks.GetLeakSeverity(Leak) / 100;
                float max = Math.Min((AIObjectiveManager.OrderPriority - 1), 90);
                float devotion = CumulatedDevotion / 100;
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (severity * distanceFactor * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            var weldingTool = character.Inventory.FindItemByTag("weldingequipment", true);
            if (weldingTool == null)
            {
                TryAddSubObjective(ref getWeldingTool, () => new AIObjectiveGetItem(character, "weldingequipment", objectiveManager, equip: true, spawnItemIfNotFound: character.TeamID == Character.TeamType.FriendlyNPC), 
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
                if (containedItems.None(i => i.HasTag("weldingfuel") && i.Condition > 0.0f))
                {
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, "weldingfuel", weldingTool.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == Character.TeamType.FriendlyNPC), 
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
                        if (Check()) { IsCompleted = true; }
                        else
                        {
                            // Failed to operate. Probably too far.
                            Abandon = true;
                        }
                    });
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(Leak, character, objectiveManager)
                {
                    // Disabled for now
                    //AllowGoingOutside = !Leak.IsRoomToRoom && objectiveManager.IsCurrentOrder<AIObjectiveFixLeaks>() && HumanAIController.HasDivingSuit(character, conditionPercentage: 50),
                    CloseEnough = reach,
                    DialogueIdentifier = Leak.FlowTargetHull != null ? "dialogcannotreachleak" : null,
                    TargetName = Leak.FlowTargetHull?.DisplayName
                },
                onAbandon: () =>
                {
                    if (Check()) { IsCompleted = true; }
                    else if ((Leak.WorldPosition - character.WorldPosition).LengthSquared() > reach * reach * 2)
                    {
                        // Too far
                        Abandon = true;
                    }
                    else
                    {
                        // We are close, try again.
                        RemoveSubObjective(ref gotoObjective);
                    }
                },
                onCompleted: () => RemoveSubObjective(ref gotoObjective));
            }
        }
    }
}
