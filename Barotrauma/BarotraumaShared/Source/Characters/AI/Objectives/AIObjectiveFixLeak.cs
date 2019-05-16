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

        public override bool IsCompleted()
        {
            return Leak.Open <= 0.0f || Leak.Removed;
        }

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

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveFixLeak fixLeak)) { return false; }
            return fixLeak.Leak == Leak;
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
            var weldingTool = character.Inventory.FindItemByTag("weldingtool");
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
                    DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no proper inventory");
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
                else if (character.Inventory.IsInLimbSlot(repairTool.Item, InvSlotType.LeftHand))
                {
                    sightLimb = character.AnimController.GetLimb(LimbType.LeftHand);
                }
                canReach = character.CanSeeTarget(leak, sightLimb);
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError("AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
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
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canOperate = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak));
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character, objectiveManager) { CloseEnough = reach * 0.75f });
            }
        }

        private Vector2 GetStandPosition()
        {
            Vector2 standPos = Leak.Position;
            var hull = Leak.FlowTargetHull;
            if (hull == null) { return standPos; }
            if (Leak.IsHorizontal)
            {
                standPos += Vector2.UnitX * Math.Sign(hull.Position.X - Leak.Position.X) * Leak.Rect.Width;
            }
            else
            {
                standPos += Vector2.UnitY * Math.Sign(hull.Position.Y - Leak.Position.Y) * Leak.Rect.Height;
            }
            return standPos;            
        }
    }
}
