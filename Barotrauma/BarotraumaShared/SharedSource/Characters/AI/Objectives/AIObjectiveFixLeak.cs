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
        public override Identifier Identifier { get; set; } = "fix leak".ToIdentifier();
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool AllowInAnySub => true;

        public Gap Leak { get; private set; }

        private AIObjectiveGetItem getWeldingTool;
        private AIObjectiveContainItem refuelObjective;
        private AIObjectiveGoTo gotoObjective;
        private AIObjectiveOperateItem operateObjective;

        public readonly bool isPriority;

        public AIObjectiveFixLeak(Gap leak, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool isPriority = false) : base (character, objectiveManager, priorityModifier)
        {
            Leak = leak;
            this.isPriority = isPriority;
        }

        protected override bool CheckObjectiveSpecific() => Leak.Open <= 0 || Leak.Removed;

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
            }
            else if (HumanAIController.IsTrueForAnyCrewMember(
                other => other != HumanAIController && 
                other.Character.IsBot &&
                other.ObjectiveManager.GetActiveObjective<AIObjectiveFixLeaks>() is AIObjectiveFixLeaks fixLeaks &&
                fixLeaks.SubObjectives.Any(so => so is AIObjectiveFixLeak fixObjective && fixObjective.Leak == Leak)))
            {
                Priority = 0;
            }
            else
            {
                float reduction = isPriority ? 1 : 2;
                float maxPriority = AIObjectiveManager.LowestOrderPriority - reduction;
                if (operateObjective != null && objectiveManager.GetActiveObjective<AIObjectiveFixLeaks>() is AIObjectiveFixLeaks fixLeaks && fixLeaks.CurrentSubObjective == this)
                {
                    // Prioritize leaks that we are already fixing
                    Priority = maxPriority;
                }
                else
                {
                    float xDist = Math.Abs(character.WorldPosition.X - Leak.WorldPosition.X);
                    float yDist = Math.Abs(character.WorldPosition.Y - Leak.WorldPosition.Y);
                    // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally).
                    // If the target is close, ignore the distance factor alltogether so that we keep fixing the leaks that are nearby.
                    float distanceFactor = isPriority || xDist < 200 && yDist < 100 ? 1 : MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 3000, xDist + yDist * 3.0f));
                    if (Leak.linkedTo.Any(e => e is Hull h && h == character.CurrentHull))
                    {
                        // Double the distance when the leak can be accessed from the current hull.
                        distanceFactor *= 2;
                    }
                    float severity = isPriority ? 1 : AIObjectiveFixLeaks.GetLeakSeverity(Leak) / 100;
                    float devotion = CumulatedDevotion / 100;
                    Priority = MathHelper.Lerp(0, maxPriority, MathHelper.Clamp(devotion + (severity * distanceFactor * PriorityModifier), 0, 1));
                }
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            var weldingTool = character.Inventory.FindItemByTag("weldingequipment".ToIdentifier(), true);
            if (weldingTool == null)
            {
                TryAddSubObjective(ref getWeldingTool, () => new AIObjectiveGetItem(character, "weldingequipment".ToIdentifier(), objectiveManager, equip: true, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC), 
                    onAbandon: () =>
                    {
                        if (character.IsOnPlayerTeam && objectiveManager.IsCurrentOrder<AIObjectiveFixLeaks>())
                        {
                            character.Speak(TextManager.Get("dialogcannotfindweldingequipment").Value, null, 0.0f, "dialogcannotfindweldingequipment".ToIdentifier(), 10.0f);
                        }
                        Abandon = true;
                    },
                    onCompleted: () => RemoveSubObjective(ref getWeldingTool));
                return;
            }
            else
            {
                if (weldingTool.OwnInventory == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no proper inventory");
#endif
                    Abandon = true;
                    return;
                }
                if (weldingTool.OwnInventory != null && weldingTool.OwnInventory.AllItems.None(i => i.HasTag("weldingfuel") && i.Condition > 0.0f))
                {
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, "weldingfuel".ToIdentifier(), weldingTool.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                    {
                        RemoveExisting = true
                    },
                    onAbandon: () =>
                    {
                        Abandon = true;
                        ReportWeldingFuelTankCount();
                    },
                    onCompleted: () => 
                    {
                        RemoveSubObjective(ref refuelObjective);
                        ReportWeldingFuelTankCount();
                    });

                    void ReportWeldingFuelTankCount()
                    {
                        if (character.Submarine != Submarine.MainSub) { return; }
                        int remainingOxygenTanks = Submarine.MainSub.GetItems(false).Count(i => i.HasTag("weldingfuel") && i.Condition > 1);
                        if (remainingOxygenTanks == 0)
                        {
                            character.Speak(TextManager.Get("DialogOutOfWeldingFuel").Value, null, 0.0f, "outofweldingfuel".ToIdentifier(), 30.0f);
                        }
                        else if (remainingOxygenTanks < 4)
                        {
                            character.Speak(TextManager.Get("DialogLowOnWeldingFuel").Value, null, 0.0f, "lowonweldingfuel".ToIdentifier(), 30.0f);
                        }
                    }
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
            Vector2 toLeak = Leak.WorldPosition - character.AnimController.AimSourceWorldPos;
            // TODO: use the collider size/reach?
            if (!character.AnimController.InWater && Math.Abs(toLeak.X) < 100 && toLeak.Y < 0.0f && toLeak.Y > -150)
            {
                HumanAIController.AnimController.Crouching = true;
            }
            float reach = CalculateReach(repairTool, character);
            bool canOperate = toLeak.LengthSquared() < reach * reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: Identifier.Empty, requireEquip: true, operateTarget: Leak), 
                    onAbandon: () => Abandon = true,
                    onCompleted: () =>
                    {
                        if (CheckObjectiveSpecific()) { IsCompleted = true; }
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
                    UseDistanceRelativeToAimSourcePos = true,
                    CloseEnough = reach,
                    DialogueIdentifier = Leak.FlowTargetHull != null ? "dialogcannotreachleak".ToIdentifier() : Identifier.Empty,
                    TargetName = Leak.FlowTargetHull?.DisplayName,
                    requiredCondition = () => 
                        Leak.Submarine == character.Submarine &&
                        Leak.linkedTo.Any(e => e is Hull h && (character.CurrentHull == h || h.linkedTo.Contains(character.CurrentHull))),
                    endNodeFilter = n => n.Waypoint.CurrentHull != null && Leak.linkedTo.Any(e => e is Hull h && h == n.Waypoint.CurrentHull),
                    // The Go To objective can be abandoned if the leak is fixed (in which case we don't want to use the dialogue)
                    SpeakCannotReachCondition = () => !CheckObjectiveSpecific()
                },
                onAbandon: () =>
                {
                    if (CheckObjectiveSpecific()) { IsCompleted = true; }
                    else if ((Leak.WorldPosition - character.AnimController.AimSourceWorldPos).LengthSquared() > MathUtils.Pow(reach * 2, 2))
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

        public override void Reset()
        {
            base.Reset();
            getWeldingTool = null;
            refuelObjective = null;
            gotoObjective = null;
            operateObjective = null;
        }

        public static float CalculateReach(RepairTool repairTool, Character character)
        {
            float armLength = ConvertUnits.ToDisplayUnits(((HumanoidAnimController)character.AnimController).ArmLength);
            // This is an approximation, because we don't know the exact reach until the pose is taken.
            // And even then the actual range depends on the direction we are aiming to.
            // Found out that without any multiplier the value (209) is often too short.
            return repairTool.Range + armLength * 2;
        }
    }
}
