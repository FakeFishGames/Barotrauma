using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        public override string DebugTag => "find safety";
        public override bool ForceRun => true;

        // TODO: expose?
        const float priorityIncrease = 25;
        const float priorityDecrease = 10;
        const float SearchHullInterval = 3.0f;
        const float clearUnreachableInterval = 30;

        public readonly HashSet<Hull> unreachable = new HashSet<Hull>();

        private float currenthullSafety;
        private float unreachableClearTimer;
        private float searchHullTimer;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveFindDivingGear divingGearObjective;

        public AIObjectiveFindSafety(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) {  }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFindSafety;

        public override void Update(float deltaTime)
        {
            if (unreachableClearTimer > 0)
            {
                unreachableClearTimer -= deltaTime;
            }
            else
            {
                unreachableClearTimer = clearUnreachableInterval;
                unreachable.Clear();
            }
            if (character.CurrentHull == null)
            {
                currenthullSafety = 0;
                Priority = 100;
                return;
            }
            if (character.OxygenAvailable < CharacterHealth.LowOxygenThreshold) { Priority = 100; }
            currenthullSafety = HumanAIController.GetHullSafety(character.CurrentHull);
            if (currenthullSafety > HumanAIController.HULL_SAFETY_THRESHOLD)
            {
                Priority -= priorityDecrease * deltaTime;
            }
            else
            {
                float dangerFactor = (100 - currenthullSafety) / 100;
                Priority += dangerFactor * priorityIncrease * deltaTime;
            }
            Priority = MathHelper.Clamp(Priority, 0, 100);
            if (divingGearObjective != null && !divingGearObjective.IsCompleted() && divingGearObjective.CanBeCompleted)
            {
                Priority = Math.Max(Priority, Math.Min(AIObjectiveManager.OrderPriority + 20, 100));
            }
        }

        protected override void Act(float deltaTime)
        {
            var currentHull = character.AnimController.CurrentHull;
            bool needsDivingGear = HumanAIController.NeedsDivingGear(currentHull);
            bool needsDivingSuit = needsDivingGear && (currentHull == null || currentHull.WaterPercentage > 90);
            bool needsEquipment = false;
            if (needsDivingSuit)
            {
                needsEquipment = !HumanAIController.HasDivingSuit(character);
            }
            else if (needsDivingGear)
            {
                needsEquipment = !HumanAIController.HasDivingGear(character);
            }
            if (needsEquipment)
            {
                TryAddSubObjective(ref divingGearObjective,
                    () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager),
                    onAbandon: () => searchHullTimer = Math.Min(1, searchHullTimer));
            }
            else
            {
                if (divingGearObjective != null && divingGearObjective.IsCompleted())
                {
                    // Reset the devotion.
                    Priority = 0;
                    divingGearObjective = null;
                }
                if (currenthullSafety < HumanAIController.HULL_SAFETY_THRESHOLD)
                {
                    searchHullTimer = Math.Min(1, searchHullTimer);
                }
                if (searchHullTimer > 0.0f)
                {
                    searchHullTimer -= deltaTime;
                }
                else
                {
                    searchHullTimer = SearchHullInterval;
                    var bestHull = FindBestHull();
                    if (bestHull != null && bestHull != currentHull)
                    {
                        if (goToObjective?.Target != bestHull)
                        {
                            goToObjective = null;
                        }
                        TryAddSubObjective(ref goToObjective, () =>
                            new AIObjectiveGoTo(bestHull, character, objectiveManager, getDivingGearIfNeeded: false)
                            {
                            // If we need diving gear, we should already have it, if possible.
                            AllowGoingOutside = HumanAIController.HasDivingSuit(character)
                            }, onAbandon: () => unreachable.Add(goToObjective.Target as Hull));
                    }
                    TryAddSubObjective(ref goToObjective, () =>
                        new AIObjectiveGoTo(bestHull, character, objectiveManager, getDivingGearIfNeeded: false)
                        {
                            // If we need diving gear, we should already have it, if possible.
                            AllowGoingOutside = HumanAIController.HasDivingSuit(character)
                        }, onAbandon: () => unreachable.Add(goToObjective.Target as Hull));
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
                if (goToObjective != null) { return; }
                if (currentHull == null) { return; }
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                }
                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious &&
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }
                if (escapeVel != Vector2.Zero)
                {
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
            }
        }

        public Hull FindBestHull(IEnumerable<Hull> ignoredHulls = null)
        {
            Hull bestHull = null;
            float bestValue = 0;
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine == null) { continue; }
                if (ignoredHulls != null && ignoredHulls.Contains(hull)) { continue; }
                float hullSafety = 0;
                if (character.Submarine != null && SteeringManager == PathSteering)
                {
                    // Inside or outside near the sub
                    if (unreachable.Contains(hull)) { continue; }
                    if (!character.Submarine.IsConnectedTo(hull.Submarine)) { continue; }
                    hullSafety = HumanAIController.GetHullSafety(hull, character);
                    // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y) * 2.0f;
                    float distanceFactor = MathHelper.Lerp(1, 0.9f, MathUtils.InverseLerp(0, 10000, dist));
                    hullSafety *= distanceFactor;
                    //skip the hull if the safety is already less than the best hull
                    //(no need to do the expensive pathfinding if we already know we're not going to choose this hull)
                    if (hullSafety < bestValue) { continue; }
                    // Each unsafe node reduces the hull safety value.
                    // Ignore current hull, because otherwise the would block all paths from the current hull to the target hull.
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, hull.SimPosition);
                    if (path.Unreachable)
                    {
                        unreachable.Add(hull);
                        continue;
                    }
                    if (!PathSteering.HasAccessToPath(path)) { continue; }
                    int unsafeNodes = path.Nodes.Count(n => n.CurrentHull != character.CurrentHull && HumanAIController.UnsafeHulls.Contains(n.CurrentHull));
                    hullSafety /= 1 + unsafeNodes;
                    // If the target is not inside a friendly submarine, considerably reduce the hull safety.
                    if (!character.Submarine.IsEntityFoundOnThisSub(hull, true))
                    {
                        hullSafety /= 10;
                    }
                }
                else
                {
                    // Outside
                    if (hull.RoomName?.ToLowerInvariant() == "airlock")
                    {
                        hullSafety = 100;
                    }
                    else
                    {
                        // TODO: could also target gaps that get us inside?
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.CurrentHull == hull && item.HasTag("airlock"))
                            {
                                hullSafety = 100;
                                break;
                            }
                        }
                    }
                    // Huge preference for closer targets
                    float distance = Vector2.DistanceSquared(character.WorldPosition, hull.WorldPosition);
                    float distanceFactor = MathHelper.Lerp(1, 0.2f, MathUtils.InverseLerp(0, MathUtils.Pow(100000, 2), distance));
                    hullSafety *= distanceFactor;
                    // If the target is not inside a friendly submarine, considerably reduce the hull safety.
                    if (hull.Submarine.TeamID != character.TeamID && hull.Submarine.TeamID != Character.TeamType.FriendlyNPC)
                    {
                        hullSafety /= 10;
                    }
                }
                if (hullSafety > bestValue)
                {
                    bestHull = hull;
                    bestValue = hullSafety;
                }
            }
            return bestHull;
        }
    }
}
