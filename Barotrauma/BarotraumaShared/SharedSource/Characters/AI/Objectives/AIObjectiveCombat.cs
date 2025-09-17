using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using static Barotrauma.AIObjectiveFindSafety;
using System.Collections.Immutable;
using System.Diagnostics;
using FarseerPhysics;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override Identifier Identifier { get; set; } = "combat".ToIdentifier();
        
        public override string DebugTag => $"{Identifier} ({Mode})";

        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;
        protected override bool AllowOutsideSubmarine => true;
        protected override bool AllowInAnySub => true;

        private readonly CombatMode initialMode;

        private float checkWeaponsTimer;
        private const float CheckWeaponsInterval = 1;
        private float ignoreWeaponTimer;
        private const float IgnoredWeaponsClearTime = 10;

        private const float GoodWeaponPriority = 30;
        
        private float holdFireTimer;
        private bool hasAimed;
        private bool isLethalWeapon;
        private bool AllowCoolDown => allowCooldown || !IsOffensiveOrArrest || Mode != initialMode || character.TeamID == Enemy.TeamID;
        private bool allowCooldown;

        public Character Enemy { get; private set; }
        
        private Item _weapon;
        public Item Weapon
        {
            get { return _weapon; }
            set
            {
                _weapon = value;
                _weaponComponent = null;
            }
        }
        private ItemComponent _weaponComponent;
        private bool hasValidRangedWeapon;
        private ItemComponent WeaponComponent
        {
            get
            {
                if (Weapon == null) { return null; }
                return _weaponComponent ?? GetWeaponComponent(Weapon);
            }
        }

        protected override bool ConcurrentObjectives => true;
        public override bool AbandonWhenCannotCompleteSubObjectives => false;

        private readonly AIObjectiveFindSafety findSafety;
        private readonly HashSet<ItemComponent> weapons = new HashSet<ItemComponent>();
        private readonly HashSet<ItemComponent> ignoredWeapons = new HashSet<ItemComponent>();

        private AIObjectiveContainItem seekAmmunitionObjective;
        private AIObjectiveGoTo retreatObjective;
        private AIObjectiveGoTo followTargetObjective;
        private AIObjectiveGetItem seekWeaponObjective;

        private Hull retreatTarget;
        private float coolDownTimer;
        private float pathBackTimer;
        private const float DefaultCoolDown = 10.0f;
        private const float PathBackCheckTime = 1.0f;
        private float aimTimer;
        private float reloadTimer;
        private float spreadTimer;

        private bool canSeeTarget;
        private float visibilityCheckTimer;
        private const float VisibilityCheckInterval = 0.2f;

        private float sqrDistance;
        private const float MaxDistance = 2000;
        private const float DistanceCheckInterval = 0.2f;
        private float distanceTimer;
        
        private const float CloseDistance = 300;
        private const float MeleeDistance = 125;
        private const float TooCloseToShoot = 100;
        private const float FloorHeightApproximate = 100;

        public bool AllowHoldFire;
        public bool SpeakWarnings;
        private bool firstWarningTriggered;
        private bool lastWarningTriggered;
        
        public float ArrestHoldFireTime { get; init; } = 10;
        
        private const float ArrestTargetDistance = 100;
        private bool arrestingRegistered;

        /// <summary>
        /// Don't start using a weapon if this condition is true
        /// </summary>
        public Func<bool> holdFireCondition;

        public enum CombatMode
        {
            /// <summary>
            /// Use weapons against the enemy, but try to retreat to a safe place.
            /// </summary>
            Defensive,
            /// <summary>
            /// Engage the enemy and keep attacking it.
            /// </summary>
            Offensive,
            /// <summary>
            /// Try to arrest the enemy without using lethal weapons (stunning + handcuffs).
            /// </summary>
            Arrest,
            /// <summary>
            /// Attempt to retreat to a safe place. Unlike in the Defensive mode, the character won't try to attack the enemy.
            /// </summary>
            Retreat,
            /// <summary>
            /// Does nothing.
            /// </summary>
            None
        }

        public CombatMode Mode { get; private set; }

        private bool IsOffensiveOrArrest => initialMode is CombatMode.Offensive or CombatMode.Arrest;
        private bool TargetEliminated => IsEnemyDisabled || (Enemy.IsUnconscious && Enemy.Params.Health.ConstantHealthRegeneration <= 0.0f) || (!character.IsInstigator && Enemy.IsHandcuffed && Enemy.IsKnockedDown);
        private bool IsEnemyDisabled => Enemy == null || Enemy.Removed || Enemy.IsDead;

        private float AimSpeed => HumanAIController.AimSpeed;
        private float AimAccuracy => HumanAIController.AimAccuracy;

        /// <summary>
        /// This is just an approximation that attempts to take different rooms and floors into account.
        /// It can be equal to a simple distance check, but when the target is nearby, we only use the horizontal axis.
        /// It's used for checking whether the enemy is close in certain situations, not for checking the distance to the enemy in general.
        /// </summary>
        private bool IsEnemyClose(float margin)
        {
            if (Enemy == null) { return false; }
            Vector2 toEnemy = Enemy.WorldPosition - character.WorldPosition;
            if (character.CurrentHull != null && Enemy.CurrentHull != null && character.CurrentHull != Enemy.CurrentHull)
            {
                // Inside, not in the same hull with the enemy
                if (Math.Abs(toEnemy.Y) > FloorHeightApproximate)
                {
                    // Different floor
                    return false;
                }
                if (HumanAIController.VisibleHulls.Contains(Enemy.CurrentHull))
                {
                    // Potentially visible and on the same floor -> use only the horizontal distance.
                    return Math.Abs(toEnemy.X) < margin;   
                }
            }
            // Outside or inside in the same hull -> use the normal distance check.
            return Vector2.DistanceSquared(character.WorldPosition, Enemy.WorldPosition) < margin * margin;
        }

        public AIObjectiveCombat(Character character, Character enemy, CombatMode mode, AIObjectiveManager objectiveManager, float priorityModifier = 1, float coolDown = DefaultCoolDown) 
            : base(character, objectiveManager, priorityModifier)
        {
            Debug.Assert(mode != CombatMode.None);
            Enemy = enemy;
            coolDownTimer = coolDown;
            findSafety = objectiveManager.GetObjective<AIObjectiveFindSafety>();
            if (findSafety != null)
            {
                findSafety.Priority = 0;
                HumanAIController.UnreachableHulls.Clear();
            }
            Mode = mode;
            initialMode = Mode;
            if (Enemy == null)
            {
                Mode = CombatMode.Retreat;
            }
            spreadTimer = Rand.Range(-10f, 10f);
            SetAimTimer(Rand.Range(1f, 1.5f) / AimSpeed);
            HumanAIController.SortTimer = 0;
        }

        protected override float GetPriority()
        {
            if (TargetEliminated)
            {
                Priority = 0;
                return Priority;
            }
            // 91-100
            const float minPriority = AIObjectiveManager.EmergencyObjectivePriority + 1;
            const float maxPriority = AIObjectiveManager.MaxObjectivePriority;
            const float priorityScale = maxPriority - minPriority;
            float xDist = Math.Abs(character.WorldPosition.X - Enemy.WorldPosition.X);
            float yDist = Math.Abs(character.WorldPosition.Y - Enemy.WorldPosition.Y);
            if (HumanAIController.VisibleHulls.Contains(Enemy.CurrentHull))
            {
                xDist /= 2;
                yDist /= 2;
            }
            float distanceFactor = MathUtils.InverseLerp(3000, 0, xDist + yDist * 5);
            float devotion = CumulatedDevotion / 100;
            float additionalPriority = MathHelper.Lerp(0, priorityScale, Math.Clamp(devotion + distanceFactor, 0, 1));
            Priority = Math.Min((minPriority + additionalPriority) * PriorityModifier, maxPriority);
            if (Priority > 0)
            {
                if (EnemyAIController.IsLatchedToSomeoneElse(Enemy, character))
                {
                    Priority = 0;
                }
            }
            return Priority;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            isAimBlocked = false;
            ignoreWeaponTimer -= deltaTime;
            checkWeaponsTimer -= deltaTime;
            if (reloadTimer > 0)
            {
                reloadTimer -= deltaTime;
            }
            if (ignoreWeaponTimer < 0)
            {
                ignoredWeapons.Clear();
                ignoreWeaponTimer = IgnoredWeaponsClearTime;
            }
            bool isFightingIntruders = objectiveManager.IsCurrentObjective<AIObjectiveFightIntruders>();
            if (findSafety != null && isFightingIntruders)
            {
                findSafety.Priority = 0;
            }
            if (!AllowCoolDown && !character.IsOnPlayerTeam && !isFightingIntruders)
            {
                distanceTimer -= deltaTime;
                if (distanceTimer < 0)
                {
                    distanceTimer = DistanceCheckInterval;
                    sqrDistance = Vector2.DistanceSquared(character.WorldPosition, Enemy.WorldPosition);
                }
            }
        }

        protected override bool CheckObjectiveState()
        {
            if (character.Submarine is { TeamID: CharacterTeamType.FriendlyNPC } && character.Submarine == Enemy.Submarine)
            {
                // Target still in the outpost
                if (character.TeamID == CharacterTeamType.FriendlyNPC && !character.IsSecurity)
                {
                    // Outpost guards shouldn't lose the target in friendly outposts,
                    // However, if we are not a guard, let's ensure that we allow the cooldown.
                    allowCooldown = true;
                }
            }
            else
            {
                if ((Enemy.Submarine == null && character.Submarine != null) || sqrDistance > MaxDistance * MaxDistance)
                {
                    // The target escaped from us.
                    Abandon = true;
                    if (character.TeamID == CharacterTeamType.FriendlyNPC && IsOffensiveOrArrest)
                    {
                        Enemy.IsCriminal = true;
                    }
                    return false;
                }
                if (Enemy.Submarine != null && character.Submarine != null && character.TeamID == CharacterTeamType.FriendlyNPC)
                {
                    if (Enemy.Submarine.TeamID != character.TeamID)
                    {
                        allowCooldown = true;
                        // Target not in the outpost anymore.
                        if (character.CanSeeTarget(Enemy))
                        {
                            allowCooldown = false;
                            coolDownTimer = DefaultCoolDown;
                        }
                        else if (pathBackTimer <= 0)
                        {
                            // Check once per sec during the cooldown whether we can find a path back to the docking port
                            pathBackTimer = PathBackCheckTime;
                            foreach ((Submarine sub, DockingPort dockingPort) in character.Submarine.ConnectedDockingPorts)
                            {
                                if (sub.TeamID != character.TeamID) { continue; }
                                var path = PathSteering.PathFinder.FindPath(character.SimPosition, character.GetRelativeSimPosition(dockingPort.Item), character.Submarine, nodeFilter: node => node.Waypoint.CurrentHull != null);
                                if (path.Unreachable)
                                {
                                    allowCooldown = false;
                                    coolDownTimer = DefaultCoolDown;
                                }
                            }
                        }
                        if (IsOffensiveOrArrest)
                        {
                            Enemy.IsCriminal = true;
                        }
                    }
                }
            }
            return TargetEliminated || (AllowCoolDown && coolDownTimer <= 0);
        }

        protected override void Act(float deltaTime)
        {
            if (IsEnemyDisabled)
            {
                IsCompleted = true;
                return;
            }
            if (AllowCoolDown)
            {
                coolDownTimer -= deltaTime;
                if (pathBackTimer > 0)
                {
                    pathBackTimer -= deltaTime;
                }
            }
            if (standUpTimer > 0)
            {
                standUpTimer -= deltaTime;
            }
            else
            {
                // Crouch by default so that others can shoot from behind. Disabled when the line of sight is blocked and while moving.
                allowCrouching = true;
            }
            if (HumanAIController.DebugAI)
            {
                BlockedPositions ??= new List<Vector2>();
                BlockedPositions.Clear();
            }
            if (seekAmmunitionObjective == null && seekWeaponObjective == null)
            {
                if (Mode != CombatMode.Retreat && TryArm())
                {
                    OperateWeapon(deltaTime);
                }
                isMoving = false;
                if (seekAmmunitionObjective == null && seekWeaponObjective == null)
                {
                    Move(deltaTime);
                }
            }
        }
        
        private bool isMoving;
        private void Move(float deltaTime)
        {
            if (Mode == CombatMode.Retreat)
            {
                Retreat(deltaTime);
            }
            else if (character.IsOnPlayerTeam && !Enemy.IsPlayer && objectiveManager.CurrentOrder is AIObjectiveGoTo gotoObjective)
            {
                if (gotoObjective.IsWaitOrder && WeaponComponent is MeleeWeapon && IsEnemyClose(CloseDistance))
                {
                    // Ordered to wait near the enemy with a melee weapon -> engage.
                    Engage(deltaTime);
                }
                else
                {
                    // Ordered to follow -> keep following.
                    if (!gotoObjective.IsCloseEnough)
                    {
                        isMoving = true;
                    }
                    gotoObjective.FaceTargetOnCompleted = false;
                    gotoObjective.ForceAct(deltaTime);
                    if (!character.AnimController.InWater && IsEnemyClose(CloseDistance))
                    {
                        // If close to the enemy, face it, so that we can attack it.
                        HumanAIController.FaceTarget(Enemy);
                        HumanAIController.AutoFaceMovement = false;
                        if (!gotoObjective.ShouldRun(true))
                        {
                            ForceWalk = true;
                        }
                    }
                }
            }
            else
            {
                switch (Mode)
                {
                    case CombatMode.Defensive:
                        Retreat(deltaTime);
                        break;
                    case CombatMode.Offensive when hasValidRangedWeapon && IsEnemyClose(CloseDistance):
                        // Too close to the enemy -> try to back off.
                        Hull currentHull = character.CurrentHull;
                        bool backOff = currentHull != null;
                        Vector2 escapeVel = Vector2.Zero;
                        if (backOff)
                        {
                            int previousEnemyDir = 0;
                            foreach (Character enemy in Character.CharacterList)
                            {
                                if (!HumanAIController.IsActive(enemy) || HumanAIController.IsFriendly(enemy) || enemy.IsHandcuffed) { continue; }
                                if (enemy.CurrentHull == null) { continue; }
                                if (currentHull != enemy.CurrentHull && !currentHull.linkedTo.Contains(enemy.CurrentHull)) { continue; }
                                Vector2 dir = character.Position - enemy.Position;
                                int enemyDir = Math.Sign(dir.X);
                                if (enemyDir == 0)
                                {
                                    // Exactly at the same pos.
                                    if (previousEnemyDir != 0)
                                    {
                                        // Another enemy at either side -> Ignore this enemy.
                                        continue;
                                    }
                                    else
                                    {
                                        // Just choose either direction.
                                        enemyDir = Rand.Value() > 0.5f ? 1 : -1;
                                    }
                                }
                                if (previousEnemyDir != 0 && enemyDir != previousEnemyDir)
                                {
                                    // Don't back off when there are enemies in different directions, because that's doomed.
                                    backOff = false;
                                    break;
                                }
                                previousEnemyDir = enemyDir;
                                // This formula is slightly modified from AIObjectiveFindSafety.UpdateSimpleEscape().
                                float distMultiplier = MathHelper.Clamp(MeleeDistance / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                                escapeVel += new Vector2(enemyDir * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                            }
                            if (escapeVel == Vector2.Zero)
                            {
                                backOff = false;
                            }
                            if (backOff)
                            {
                                // Only move if we haven't reached the edge of the room.
                                float left = currentHull.Rect.X + 50;
                                float right = currentHull.Rect.Right - 50;
                                backOff = escapeVel.X < 0 && character.Position.X > left || escapeVel.X > 0 && character.Position.X < right;
                            }
                        }
                        if (backOff)
                        {
                            BackOff();
                        }
                        else
                        {
                            Engage(deltaTime);
                        }
                        
                        void BackOff()
                        {
                            RemoveFollowTarget();
                            isMoving = true;
                            if (!IsEnemyClose(MeleeDistance))
                            {
                                ForceWalk = true;
                            }
                            HumanAIController.FaceTarget(Enemy);
                            HumanAIController.AutoFaceMovement = false;
                            character.ReleaseSecondaryItem();
                            character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                        }
                        break;
                    default:
                        Engage(deltaTime);
                        break;
                }
            }
        }

        private bool TryArm()
        {
            if (character.LockHands || Enemy == null)
            {
                Weapon = null;
                RemoveSubObjective(ref seekAmmunitionObjective);
                return false;
            }
            bool isAllowedToSeekWeapons = character.IsHostileEscortee || character.IsPrisoner || // Prisoners and terrorists etc are always allowed to seek new weapons.
                                          (character.IsInFriendlySub // Other characters need to be on a friendly sub in order to "know" where the weapons are. This also prevents NPCs "stealing" player items.
                                            && IsOffensiveOrArrest // = Defensive or retreating AI shouldn't seek new weapons.
                                            && !character.IsInstigator // Instigators (= aggressive NPCs spawned with events) shouldn't seek new weapons, because we don't want them to grab e.g. an smg, if they spawn with a wrench or something.
                                            && objectiveManager.CurrentOrder is not AIObjectiveGoTo); // if ordered to wait/follow, shouldn't go seeking new weapons.
            if (checkWeaponsTimer < 0)
            {
                checkWeaponsTimer = CheckWeaponsInterval;
                // First go through all weapons and try to reload without seeking ammunition
                HashSet<ItemComponent> allWeapons = FindWeaponsFromInventory();
                while (allWeapons.Any())
                {
                    Weapon = GetWeapon(allWeapons, out ItemComponent newWeaponComponent);
                    _weaponComponent = newWeaponComponent;
                    if (Weapon == null)
                    {
                        // No weapons
                        break;
                    }
                    if (!character.Inventory.Contains(Weapon) || WeaponComponent == null)
                    {
                        // Not in the inventory anymore or cannot find the weapon component
                        allWeapons.RemoveWhere(weaponComponent => weaponComponent.Item == Weapon);
                        Weapon = null;
                        continue;
                    }
                    if (!WeaponComponent.IsEmpty(character))
                    {
                        // All good, the weapon is loaded
                        break;
                    }
                    bool seekAmmo = isAllowedToSeekWeapons && seekAmmunitionObjective == null;
                    if (seekAmmo)
                    {
                        // Bots set to arrest the target are always allowed to seek (or spawn) more ammo, because otherwise they might not be able to stun the target and need to use lethal weapons.
                        seekAmmo = Mode == CombatMode.Arrest || !IsEnemyClose(CloseDistance);
                    }
                    if (Reload(seekAmmo: seekAmmo))
                    {
                        // All good, we can use the weapon.
                        break;
                    }
                    else if (seekAmmunitionObjective != null)
                    {
                        // Seeking ammo.
                        break;
                    }
                    else
                    {
                        // No ammo and should not try to seek ammo.
                        allWeapons.RemoveWhere(weaponComponent => weaponComponent.Item == Weapon);
                        Weapon = null;
                    }
                }
                if (Weapon == null)
                {
                    // No weapon found with the conditions above. Try again, now let's try to seek ammunition too
                    Weapon = FindWeapon(out _weaponComponent);
                    if (Weapon != null)
                    {
                        if (!CheckWeapon(seekAmmo: true))
                        {
                            if (seekAmmunitionObjective != null)
                            {
                                // No loaded weapon, but we are trying to seek ammunition.
                                return false;
                            }
                            else
                            {
                                Weapon = null;
                            }
                        }
                    }
                }
                if (!isAllowedToSeekWeapons)
                {
                    if (WeaponComponent == null)
                    {
                        SpeakNoWeapons();
                        Mode = CombatMode.Retreat;
                    }
                }
                else if (seekAmmunitionObjective == null && (WeaponComponent == null || (WeaponComponent.CombatPriority < GoodWeaponPriority && !IsEnemyClose(CloseDistance))))
                {
                    // No weapon or only a poor weapon equipped -> try to find better.
                    RemoveSubObjective(ref retreatObjective);
                    RemoveSubObjective(ref followTargetObjective);
                    TryAddSubObjective(ref seekWeaponObjective,
                        constructor: () => new AIObjectiveGetItem(character, "weapon".ToIdentifier(), objectiveManager, equip: true, checkInventory: false)
                        {
                            AllowStealing = HumanAIController.IsMentallyUnstable,
                            AbortCondition = _ => IsEnemyClose(CloseDistance / 2),
                            EvaluateCombatPriority = false,  // Use a custom formula instead
                            GetItemPriority = i =>
                            {
                                if (Weapon != null && (i == Weapon || i.Prefab.Identifier == Weapon.Prefab.Identifier)) { return 0; }
                                if (i.IsOwnedBy(character)) { return 0; }
                                float priority = 0;
                                if (GetWeaponComponent(i) is ItemComponent ic)
                                {
                                    priority = GetWeaponPriority(ic, prioritizeMelee: false, canSeekAmmo: true, out _) / 100;
                                }
                                if (priority <= 0) { return 0; }
                                // Check that we are not running directly towards the enemy.
                                Vector2 toItem = i.WorldPosition - character.WorldPosition;
                                float range = HumanAIController.FindWeaponsRange;
                                if (range is > 0 and < float.PositiveInfinity)
                                {
                                    // Y distance is irrelevant when we are on the same floor. If we are on a different floor, let's double it.
                                    float yDiff = Math.Abs(toItem.Y) > FloorHeightApproximate ? toItem.Y * 2 : 0;
                                    Vector2 adjustedDiff = new Vector2(toItem.X, yDiff);
                                    if (adjustedDiff.LengthSquared() > MathUtils.Pow2(range))
                                    {
                                        // Too far -> not allowed to seek.
                                        return 0;
                                    }
                                }
                                Vector2 toEnemy = Enemy.WorldPosition - character.WorldPosition;
                                if (Math.Sign(toItem.X) == Math.Sign(toEnemy.X))
                                {
                                    // Going towards the enemy -> reduce the priority.
                                    priority *= 0.5f;
                                }
                                if (i.CurrentHull != null && !HumanAIController.VisibleHulls.Contains(i.CurrentHull))
                                {
                                    if (Math.Abs(toItem.Y) > FloorHeightApproximate && Math.Abs(toEnemy.Y) > FloorHeightApproximate)
                                    {
                                        if (Math.Sign(toItem.Y) == Math.Sign(toEnemy.Y))
                                        {
                                            // Different floor, at the direction of the enemy -> reduce the priority.
                                            priority *= 0.75f;
                                        }
                                    }
                                }
                                return priority;
                            }
                        },
                        onCompleted: () => RemoveSubObjective(ref seekWeaponObjective),
                        onAbandon: () =>
                        {
                            RemoveSubObjective(ref seekWeaponObjective);
                            if (Weapon == null)
                            {
                                SpeakNoWeapons();
                                Mode = CombatMode.Retreat;
                            }
                            else if (!objectiveManager.HasObjectiveOrOrder<AIObjectiveFightIntruders>())
                            {
                                // Poor weapon equipped
                                Mode = CombatMode.Defensive;
                            }
                        });
                }
            }
            else if (seekAmmunitionObjective == null && seekWeaponObjective == null)
            {
                if (!CheckWeapon(seekAmmo: false))
                {
                    Weapon = null;
                }
            }
            return Weapon != null;

            bool CheckWeapon(bool seekAmmo)
            {
                if (!character.Inventory.Contains(Weapon) || WeaponComponent == null)
                {
                    // Not in the inventory anymore or cannot find the weapon component
                    return false;
                }
                if (WeaponComponent.IsEmpty(character))
                {
                    // Try reloading (and seek ammo)
                    if (!Reload(seekAmmo))
                    {
                        return false;
                    }
                }
                return true;
            };
        }

        private void OperateWeapon(float deltaTime)
        {
            if (isMoving && character.IsClimbing)
            {
                // Don't climb and shoot at the same time, because it messes up the aiming.
                ClearInputs();
                return;
            }
            switch (Mode)
            {
                case CombatMode.Offensive:
                case CombatMode.Defensive:
                case CombatMode.Arrest:
                    if (Equip())
                    {
                        Attack(deltaTime);
                    }
                    break;
                case CombatMode.Retreat:
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private Item FindWeapon(out ItemComponent weaponComponent) => GetWeapon(FindWeaponsFromInventory(), out weaponComponent);

        private static ItemComponent GetWeaponComponent(Item item) => 
            item.GetComponent<MeleeWeapon>() ??
            item.GetComponent<RangedWeapon>() ??
            item.GetComponent<RepairTool>() ??
            item.GetComponent<Holdable>() as ItemComponent;

        /// <summary>
        /// Normal range of combat priority is 0-100, but the value is not clamped.
        /// </summary>
        private float GetWeaponPriority(ItemComponent weapon, bool prioritizeMelee, bool canSeekAmmo, out float lethalDmg)
        {
            lethalDmg = -1;
            float priority = weapon.CombatPriority;
            if (priority <= 0) { return 0; }
            if (weapon is RepairTool repairTool)
            {
                switch (repairTool.UsableIn)
                {
                    case RepairTool.UseEnvironment.Air:
                        if (character.InWater) { return 0; }
                        break;
                    case RepairTool.UseEnvironment.Water:
                        if (!character.InWater) { return 0; }
                        break;
                    case RepairTool.UseEnvironment.None:
                        return 0;
                    case RepairTool.UseEnvironment.Both:
                    default:
                        break;
                }
            }
            if (prioritizeMelee && weapon is MeleeWeapon)
            {
                priority *= 5;
            }
            if (weapon.IsEmpty(character))
            {
                if (weapon is RangedWeapon && !canSeekAmmo)
                {
                    // Ignore weapons that don't have any ammunition, when we are not allowed to seek more ammo.
                    return 0;
                }
                else
                {
                    // Reduce the priority for weapons that don't have proper ammunition loaded.
                    if (character.HasEquippedItem(Weapon, predicate: CharacterInventory.IsHandSlotType))
                    {
                        // Yet prefer the equipped weapon.
                        priority *= 0.75f;
                    }
                    else
                    {
                        priority *= 0.5f;
                    }
                }
            }
            if (Enemy.Params.Health.StunImmunity)
            {
                if (weapon.Item.HasTag(Tags.StunnerItem))
                {
                    priority /= 2;
                }
            }
            else if (Enemy.IsKnockedDown && Mode != CombatMode.Arrest)
            {
                // Enemy is stunned, reduce the priority of stunner weapons.
                Attack attack = GetAttackDefinition(weapon);
                if (attack != null)
                {
                    lethalDmg = attack.GetTotalDamage();
                    float max = lethalDmg + 1;
                    if (weapon.Item.HasTag(Tags.StunnerItem))
                    {
                        priority = max;
                    }
                    else
                    {
                        float stunDmg = ApproximateStunDamage(weapon, attack);
                        float diff = stunDmg - lethalDmg;
                        priority = Math.Clamp(priority - Math.Max(diff * 2, 0), min: 1, max);
                    }
                }
            }
            else if (Mode == CombatMode.Arrest)
            {
                // Enemy is not stunned, increase the priority of stunner weapons and decrease the priority of lethal weapons.
                if (weapon.Item.HasTag(Tags.StunnerItem))
                {
                    priority *= 5;
                }
                else
                {
                    Attack attack = GetAttackDefinition(weapon);
                    if (attack != null)
                    {
                        lethalDmg = attack.GetTotalDamage();
                        float stunDmg = ApproximateStunDamage(weapon, attack);
                        float diff = stunDmg - lethalDmg;
                        if (diff < 0)
                        {
                            priority /= 2;
                        }
                    }
                }
            }
            else if (weapon is MeleeWeapon && weapon.Item.HasTag(Tags.StunnerItem) && (Enemy.Params.Health.StunImmunity || !CanMeleeStunnerStun(weapon)))
            {
                // Cannot do stun damage -> use the melee damage to determine the priority.
                Attack attack = GetAttackDefinition(weapon);
                priority = attack?.GetTotalDamage() ?? priority / 2;
            }
            // Reduce the priority of the weapon, if we don't have requires skills to use it.
            float startPriority = priority;
            var skillRequirementHints = weapon.Item.Prefab.SkillRequirementHints;
            if (skillRequirementHints != null)
            {
                // If there are any skill requirement hints defined, let's use them.
                // This should be the most accurate (manually defined) representation of the requirements (taking into account property conditionals etc).
                foreach (SkillRequirementHint hint in skillRequirementHints)
                {
                    float skillLevel = character.GetSkillLevel(hint.Skill);
                    float targetLevel = hint.Level;
                    priority = ReducePriority(priority, skillLevel, targetLevel);
                }
            }
            else
            {
                // If no skill requirement hints are defined, let's rely on the required skill definition.
                // This can be inaccurate in some cases (hmg, rifle), but in those cases there should be a skill requirement hint defined for the weapon.
                foreach (Skill skill in weapon.RequiredSkills)
                {
                    float skillLevel = character.GetSkillLevel(skill.Identifier);
                    // Skill multiplier is currently always 1, so it's not really needed, but that could change(?)
                    float targetLevel = skill.Level * weapon.GetSkillMultiplier();
                    priority = ReducePriority(priority, skillLevel, targetLevel);
                }
            }
            // Don't allow to reduce more than half, because an assault rifle is still an assault rifle, even in untrained hands.
            priority = Math.Max(priority, startPriority / 2);
            return priority;
            
            float ReducePriority(float prio, float skillLevel, float targetLevel)
            {
                float diff = targetLevel - skillLevel;
                if (diff > 0)
                {
                    prio -= diff;
                }
                return prio;
            }
        }

        private float ApproximateStunDamage(ItemComponent weapon, Attack attack)
        {
            // Try to reduce the priority using the actual damage values and status effects.
            // This is an approximation, because we can't check the status effect conditions here.
            // The result might be incorrect if there is a high stun effect that's only applied in certain conditions.
            var statusEffects = attack.StatusEffects.Where(se => !se.HasConditions && se.type == ActionType.OnUse && se.HasRequiredItems(character));
            if (weapon.statusEffectLists != null && weapon.statusEffectLists.TryGetValue(ActionType.OnUse, out List<StatusEffect> hitEffects))
            {
                statusEffects = statusEffects.Concat(hitEffects);
            }
            float afflictionsStun = attack.Afflictions.Keys.Sum(a => a.Identifier == AfflictionPrefab.StunType ? a.Strength : 0);
            float effectsStun = statusEffects.None() ? 0 : statusEffects.Max(se =>
            {
                float stunAmount = 0;
                var stunAffliction = se.Afflictions.Find(a => a.Identifier == AfflictionPrefab.StunType);
                if (stunAffliction != null)
                {
                    stunAmount = stunAffliction.Strength;
                }
                return stunAmount;
            });
            return attack.Stun + afflictionsStun + effectsStun;
        }

        private static bool CanMeleeStunnerStun(ItemComponent weapon)
        {
            // If there's an item container that takes a battery,
            // assume that it's required for the stun effect
            // as we can't check the status effect conditions here.
            Identifier mobileBatteryTag = Tags.MobileBattery;
            var containers = weapon.Item.Components.Where(ic =>
                ic is ItemContainer container &&
                container.ContainableItemIdentifiers.Contains(mobileBatteryTag));
            // If there's no such container, assume that the melee weapon can stun without a battery.
            return containers.None() || containers.Any(container =>
                (container as ItemContainer)?.Inventory.AllItems.Any(i => i != null && i.HasTag(mobileBatteryTag) && i.Condition > 0.0f) ?? false);
        }
        
        private Item GetWeapon(IEnumerable<ItemComponent> weaponList, out ItemComponent weaponComponent)
        {
            hasValidRangedWeapon = false;
            weaponComponent = null;
            float bestPriority = 0;
            float lethalDmg = -1;
            bool prioritizeMelee = IsEnemyClose(TooCloseToShoot) || EnemyAIController.IsLatchedTo(Enemy, character);
            bool isCloseToEnemy = prioritizeMelee || IsEnemyClose(CloseDistance);
            foreach (ItemComponent weapon in weaponList)
            {
                float priority = GetWeaponPriority(weapon, prioritizeMelee, canSeekAmmo: !isCloseToEnemy, out lethalDmg);
                if (priority >= GoodWeaponPriority && weapon is RangedWeapon or RepairTool)
                {
                    hasValidRangedWeapon = true;
                }
                if (priority > bestPriority)
                {
                    weaponComponent = weapon;
                    bestPriority = priority;
                }
            }
            if (weaponComponent == null) { return null; }
            if (bestPriority < 1) { return null; }
            if (Mode == CombatMode.Arrest)
            {
                if (weaponComponent.Item.HasTag(Tags.StunnerItem))
                {
                    isLethalWeapon = false;
                }
                else
                {
                    if (lethalDmg < 0)
                    {
                        lethalDmg = GetLethalDamage(weaponComponent);
                    }
                    isLethalWeapon = lethalDmg > 1;
                }
                if (AllowHoldFire)
                {
                    if (!hasAimed && holdFireTimer <= 0)
                    {
                        holdFireTimer = ArrestHoldFireTime * Rand.Range(0.9f, 1.1f);
                    }
                    else
                    {
                        if (SpeakWarnings)
                        {
                            if (!lastWarningTriggered && holdFireTimer < ArrestHoldFireTime * 0.3f)
                            {
                                FriendlyGuardSpeak("dialogarrest.lastwarning".ToIdentifier(), delay: 0, minDurationBetweenSimilar: 0f);
                                lastWarningTriggered = true;
                            }
                            else if (!firstWarningTriggered && holdFireTimer < ArrestHoldFireTime * 0.8f)
                            {
                                FriendlyGuardSpeak("dialogarrest.firstwarning".ToIdentifier(), delay: 0, minDurationBetweenSimilar: 0f);
                                firstWarningTriggered = true;
                            }
                        }   
                    }
                }
            }
            return weaponComponent.Item;
        }

        public static float GetLethalDamage(ItemComponent weapon)
        {
            float lethalDmg = 0;
            Attack attack = GetAttackDefinition(weapon);
            if (attack != null)
            {
                lethalDmg = attack.GetTotalDamage();
            }
            return lethalDmg;
        }

        private static Attack GetAttackDefinition(ItemComponent weapon)
        {
            Attack attack = weapon switch
            {
                MeleeWeapon meleeWeapon => meleeWeapon.Attack,
                RangedWeapon rangedWeapon => rangedWeapon.FindProjectile(triggerOnUseOnContainers: false)?.Attack,
                _ => null
            };
            return attack;
        }

        private HashSet<ItemComponent> FindWeaponsFromInventory()
        {
            weapons.Clear();
            foreach (var item in character.Inventory.AllItems)
            {
                GetWeapons(item, weapons);
                if (item.OwnInventory != null)
                {
                    item.OwnInventory.AllItems.ForEach(i => GetWeapons(i, weapons));
                }
            }
            return weapons;
        }

        private void GetWeapons(Item item, ICollection<ItemComponent> weaponList)
        {
            if (item == null) { return; }
            foreach (var component in item.Components)
            {
                if (ignoredWeapons.Contains(component)) { continue; }
                if (component.CombatPriority > 0)
                {
                    weaponList.Add(component);
                }
            }
        }
        
        private void UnequipWeapon()
        {
            if (Weapon == null) { return; }
            if (character.LockHands) { return; }
            if (character.HeldItems.Contains(Weapon)) { return; }
            character.Unequip(Weapon);
        }

        private bool Equip()
        {
            if (character.LockHands) { return false; }
            if (WeaponComponent.IsEmpty(character))
            {
                return false;
            }
            if (!character.HasEquippedItem(Weapon, predicate: CharacterInventory.IsHandSlotType))
            {
                ClearInputs();
                Weapon.TryInteract(character, forceSelectKey: true);
                var slots = Weapon.AllowedSlots.Where(CharacterInventory.IsHandSlotType);
                bool successfullyEquipped = character.TryPutItem(Weapon, slots);
                if (!successfullyEquipped && character.HasHandsFull(out (Item leftHandItem, Item rightHandItem) items))
                {
                    // Unequip and try again.
                    character.Unequip(items.leftHandItem);
                    character.Unequip(items.rightHandItem);
                    successfullyEquipped = character.TryPutItem(Weapon, slots);
                }
                if (successfullyEquipped)
                {
                    SetAimTimer(Rand.Range(0.2f, 0.4f) / AimSpeed);
                    SetReloadTime(WeaponComponent);
                }
                else
                {
                    SpeakNoWeapons();
                    Weapon = null;
                    Mode = CombatMode.Retreat;
                    return false;
                }
            }
            return true;
        }

        private float findHullTimer;
        private const float findHullInterval = 1.0f;

        private void Retreat(float deltaTime)
        {
            isMoving = true;
            if (!Enemy.IsHuman && !character.IsInFriendlySub)
            {
                // Only relevant when we are retreating from monsters and are not inside a friendly sub.
                PlayerCrewSpeak("dialogcombatretreating".ToIdentifier(), delay: Rand.Range(0f, 1f), minDurationBetweenSimilar: 20);
            }
            RemoveFollowTarget();
            RemoveSubObjective(ref seekAmmunitionObjective);
            if (retreatTarget != null)
            {
                if (HumanAIController.VisibleHulls.Contains(Enemy.CurrentHull))
                {
                    // In the same hull with the enemy
                    if (retreatTarget == character.CurrentHull)
                    {
                        // Go elsewhere
                        retreatTarget = null;
                    }
                }
            }
            if (retreatObjective != null && retreatObjective.Target != retreatTarget)
            {
                RemoveSubObjective(ref retreatObjective);
            }
            if (character.Submarine == null && sqrDistance < MathUtils.Pow2(MaxDistance))
            {
                // Swim away
                SteeringManager.Reset();
                character.ReleaseSecondaryItem();
                SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(character.WorldPosition - Enemy.WorldPosition));
                SteeringManager.SteeringAvoid(deltaTime, 5, weight: 2);
                return;
            }
            if (retreatTarget == null || retreatObjective is { CanBeCompleted: false })
            {
                if (findHullTimer > 0)
                {
                    findHullTimer -= deltaTime;
                }
                else
                {
                    HullSearchStatus hullSearchStatus = findSafety.FindBestHull(out Hull potentialSafeHull, HumanAIController.VisibleHulls, allowChangingSubmarine: character.TeamID != CharacterTeamType.FriendlyNPC);
                    if (hullSearchStatus != HullSearchStatus.Finished)
                    {
                        findSafety.UpdateSimpleEscape(deltaTime);
                        return;
                    }
                    retreatTarget = potentialSafeHull;
                    findHullTimer = findHullInterval * Rand.Range(0.9f, 1.1f);
                }
            }
            if (retreatTarget != null && character.CurrentHull != retreatTarget)
            {
                TryAddSubObjective(ref retreatObjective, () => new AIObjectiveGoTo(retreatTarget, character, objectiveManager)
                {
                    UsePathingOutside = false,
                    SpeakIfFails = false
                },
                onAbandon: () =>
                {
                    if (Enemy != null && HumanAIController.VisibleHulls.Contains(Enemy.CurrentHull))
                    {
                        // If in the same room with an enemy -> don't try to escape because we'd want to fight it
                        SteeringManager.Reset();
                        RemoveSubObjective(ref retreatObjective);
                    }
                    else
                    {
                        // else abandon and fall back to find safety mode
                        Abandon = true;
                    }
                }, 
                onCompleted: () => RemoveSubObjective(ref retreatObjective));
            }
        }

        private void Engage(float deltaTime)
        {
            if (WeaponComponent == null)
            {
                RemoveFollowTarget();
                SteeringManager.Reset();
                return;
            }
            if (character.LockHands || Enemy == null)
            {
                Mode = CombatMode.Retreat;
                SteeringManager.Reset();
                return;
            }
            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveSubObjective(ref seekAmmunitionObjective);
            RemoveSubObjective(ref seekWeaponObjective);
            if (character.Submarine == null && WeaponComponent is MeleeWeapon meleeWeapon)
            {
                if (sqrDistance > MathUtils.Pow2(meleeWeapon.Range))
                {
                    isMoving = true;
                    character.ReleaseSecondaryItem();
                    // Swim towards the target
                    SteeringManager.Reset();
                    SteeringManager.SteeringSeek(character.GetRelativeSimPosition(Enemy), weight: 10);
                    SteeringManager.SteeringAvoid(deltaTime, 5, weight: 15);
                }
                else
                {
                    SteeringManager.Reset();
                }
                return;
            }
            if (character.TeamID == CharacterTeamType.FriendlyNPC && character.Submarine != null && character.Submarine.TeamID != character.TeamID)
            {
                // An outpost guard following the target (possibly a player) to another sub -> don't go further, unless can see the enemy.
                if (!character.IsClimbing && !character.CanSeeTarget(Enemy))
                {
                    SteeringManager.Reset();
                    RemoveFollowTarget();
                    return;
                }
            }
            if (followTargetObjective != null && followTargetObjective.Target != Enemy)
            {
                RemoveFollowTarget();
            }
            TryAddSubObjective(ref followTargetObjective,
                constructor: () => new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: true, getDivingGearIfNeeded: true, closeEnough: 50)
                {
                    UsePathingOutside = false,
                    IgnoreIfTargetDead = true,
                    TargetName = Enemy.DisplayName,
                    AlwaysUseEuclideanDistance = false,
                    SpeakIfFails = false
                },
                onAbandon: () =>
                {
                    if (Enemy != null && HumanAIController.VisibleHulls.Contains(Enemy.CurrentHull))
                    {
                        // If in the same room with an enemy -> don't try to escape because we'd want to fight it
                        SteeringManager.Reset();
                        RemoveSubObjective(ref followTargetObjective);
                    }
                    else
                    {
                        // else abandon and fall back to find safety mode
                        Abandon = true;
                    }
                });
            if (followTargetObjective == null) { return; }
            if (Mode == CombatMode.Arrest && Enemy.IsKnockedDownOrRagdolled && !arrestingRegistered)
            {
                bool hasHandCuffs = HumanAIController.HasItem(character, Tags.HandLockerItem, out _);
                if (!hasHandCuffs && character.TeamID == CharacterTeamType.FriendlyNPC)
                {
                    // Spawn handcuffs
                    ItemPrefab prefab = ItemPrefab.Find(null, "handcuffs".ToIdentifier());
                    if (prefab != null)
                    {
                        Entity.Spawner.AddItemToSpawnQueue(prefab, character.Inventory, onSpawned: i =>
                        {
                            i.SpawnedInCurrentOutpost = true;
                            i.AllowStealing = false;
                        });
                    }
                }
                arrestingRegistered = true;
                followTargetObjective.Completed += OnArrestTargetReached;
                followTargetObjective.CloseEnough = ArrestTargetDistance;
            }
            if (!arrestingRegistered)
            {
                followTargetObjective.CloseEnough =
                    WeaponComponent switch
                    {
                        RangedWeapon => isAimBlocked ? BlockedDistance : 1000,
                        MeleeWeapon mw => mw.Range,
                        RepairTool rt => rt.Range,
                        _ => 50
                    };
            }
            if (isAimBlocked)
            {
                ForceWalk = true;
            }
            if (!followTargetObjective.IsCloseEnough)
            {
                isMoving = true;
            }
        }

        private void RemoveFollowTarget()
        {
            if (followTargetObjective != null)
            {
                if (arrestingRegistered)
                {
                    followTargetObjective.Completed -= OnArrestTargetReached;
                }
                RemoveSubObjective(ref followTargetObjective);
            }
            arrestingRegistered = false;
        }

        private void OnArrestTargetReached()
        {
            if (!Enemy.IsKnockedDownOrRagdolled)
            {
                RemoveFollowTarget();
                return;
            }
            if (character.TeamID == CharacterTeamType.FriendlyNPC)
            {
                foreach (var item in Enemy.Inventory.AllItemsMod)
                {
                    AIObjectiveFindThieves.MarkTargetAsInspected(character);
                    bool confiscateItem = AIObjectiveCheckStolenItems.IsItemIllegitimate(Enemy, item);
                    if (!confiscateItem && Enemy.IsActingOffensively)
                    {
                        // Confiscate any weapons or items that can be used offensively.
                        confiscateItem = item.HasTag(Tags.Weapon) || item.HasTag(Tags.Poison) || GetWeaponComponent(item) is { CombatPriority: > 0 };   
                    }
                    if (confiscateItem)
                    {
                        item.Drop(character);
                        character.Inventory.TryPutItem(item, character, CharacterInventory.AnySlot);
                    }
                }
            }

            //prefer using handcuffs already on the enemy's inventory
            if (!HumanAIController.HasItem(Enemy, Tags.HandLockerItem, out IEnumerable<Item> matchingItems))
            {
                HumanAIController.HasItem(character, Tags.HandLockerItem, out matchingItems);
            }

            if (matchingItems.Any() && 
                !Enemy.IsUnconscious && Enemy.IsKnockedDownOrRagdolled && character.CanInteractWith(Enemy) && !Enemy.LockHands)
            {
                var handCuffs = matchingItems.First();
                if (!HumanAIController.TakeItem(handCuffs, Enemy.Inventory, equip: true, wear: true))
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Failed to handcuff the target.", Color.Red);
#endif
                    if (objectiveManager.IsCurrentObjective<AIObjectiveFightIntruders>())
                    {
                        Abandon = true;
                        return;
                    }
                }
                character.Speak(TextManager.Get("DialogTargetArrested").Value, null, 3.0f, "targetarrested".ToIdentifier(), 30.0f);
            }
            if (!objectiveManager.IsCurrentObjective<AIObjectiveFightIntruders>())
            {
                IsCompleted = true;
            }
        }

        /// <summary>
        /// Seeks for more ammunition. Creates a new subobjective.
        /// </summary>
        private void SeekAmmunition(ImmutableHashSet<Identifier> ammunitionIdentifiers)
        {
            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveSubObjective(ref seekWeaponObjective);
            RemoveFollowTarget();
            var itemContainer = Weapon.GetComponent<ItemContainer>();
            TryAddSubObjective(ref seekAmmunitionObjective,
                constructor: () => new AIObjectiveContainItem(character, ammunitionIdentifiers, itemContainer, objectiveManager, 
                    spawnItemIfNotFound: !character.IsOnPlayerTeam && character.AIController.HasInfiniteItemSpawns(ammunitionIdentifiers))
                {
                    ItemCount = itemContainer.MainContainerCapacity * itemContainer.MaxStackSize,
                    checkInventory = false,
                    MoveWholeStack = true
                },
                onCompleted: () => RemoveSubObjective(ref seekAmmunitionObjective),
                onAbandon: () =>
                {
                    SteeringManager.Reset();
                    RemoveSubObjective(ref seekAmmunitionObjective);
                    ignoredWeapons.Add(WeaponComponent);
                    Weapon = null;
                });
        }

        /// <summary>
        /// Reloads the ammunition found in the inventory.
        /// If seekAmmo is true, tries to get find the ammo elsewhere.
        /// </summary>
        /// <returns>True if the weapon was reloaded successfully.</returns>
        private bool Reload(bool seekAmmo)
        {
            if (WeaponComponent == null) { return false; }
            if (Weapon.OwnInventory == null) { return true; }
            if (!Weapon.IsInteractable(character)) { return false; }
            HumanAIController.UnequipEmptyItems(Weapon, allowDestroying: !character.IsOnPlayerTeam);
            ImmutableHashSet<Identifier> ammunitionIdentifiers = null;
            if (WeaponComponent.RequiredItems.ContainsKey(RelatedItem.RelationType.Contained))
            {
                foreach (RelatedItem requiredItem in WeaponComponent.RequiredItems[RelatedItem.RelationType.Contained])
                {
                    if (Weapon.OwnInventory.AllItems.Any(it => it.Condition > 0 && requiredItem.MatchesItem(it))) { continue; }
                    ammunitionIdentifiers = requiredItem.Identifiers;
                    break;
                }
            }
            else if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                ammunitionIdentifiers = meleeWeapon.PreferredContainedItems;
            }
            // No ammo
            if (ammunitionIdentifiers != null)
            {
                // Try reload ammunition from inventory
                static bool IsInsideHeadset(Item i) => i.ParentInventory?.Owner is Item ownerItem && ownerItem.HasTag(Tags.MobileRadio);
                Item ammunition = character.Inventory.FindItem(i => 
                    i.HasIdentifierOrTags(ammunitionIdentifiers) && i.Condition > 0 && !IsInsideHeadset(i) && i.IsInteractable(character), recursive: true);
                if (ammunition != null)
                {
                    var container = Weapon.GetComponent<ItemContainer>();
                    if (container.Inventory.TryPutItem(ammunition, user: character))
                    {
                        ClearInputs();
                        SetReloadTime(WeaponComponent);
                    }
                    else if (ammunition.ParentInventory == character.Inventory)
                    {
                        ammunition.Drop(character);
                    }
                }
            }
            if (!WeaponComponent.IsEmpty(character))
            {
                return true;
            }
            else if (IsOffensiveOrArrest && seekAmmo && ammunitionIdentifiers != null)
            {
                // Inventory not drawn = it's not interactable
                // If the weapon is empty and the inventory is inaccessible, it can't be reloaded
                if (!Weapon.OwnInventory.Container.DrawInventory) { return false; }
                SeekAmmunition(ammunitionIdentifiers);
            }
            return false;
        }

        private bool isAimBlocked;
        private float _blockedDistance;
        private float BlockedDistance
        {
            get
            {
                if (_blockedDistance <= 0)
                {
                    _blockedDistance = CloseDistance * Rand.Range(1.0f, 1.3f);
                }
                return _blockedDistance;
            }
        }
        public List<Vector2> BlockedPositions;
        private void Attack(float deltaTime)
        {
            character.CursorPosition = Enemy.WorldPosition;
            if (AimAccuracy < 1)
            {
                spreadTimer += deltaTime * Rand.Range(0.01f, 1f);
                float shake = Rand.Range(0.95f, 1.05f);
                float offsetAmount = (1 - AimAccuracy) * Rand.Range(300f, 500f);
                float distanceFactor = MathUtils.InverseLerp(0, 1000 * 1000, sqrDistance);
                float offset = (float)Math.Sin(spreadTimer * shake) * offsetAmount * distanceFactor;
                character.CursorPosition += new Vector2(0, offset);
            }
            if (character.Submarine != null)
            {
                character.CursorPosition -= character.Submarine.Position;
            }
            visibilityCheckTimer -= deltaTime;
            if (visibilityCheckTimer <= 0.0f)
            {
                canSeeTarget = character.CanSeeTarget(Enemy);
                visibilityCheckTimer = VisibilityCheckInterval;
            }
            if (!canSeeTarget)
            {
                SetAimTimer(Rand.Range(0.2f, 0.4f) / AimSpeed);
                return;
            }
            if (Weapon.RequireAimToUse)
            {
                character.SetInput(InputType.Aim, hit: false, held: true);
            }
            hasAimed = true;
            if (AllowHoldFire && holdFireTimer > 0)
            {
                holdFireTimer -= deltaTime;
                return;
            }
            if (aimTimer > 0)
            {
                aimTimer -= deltaTime;
                return;
            }
            sqrDistance = Vector2.DistanceSquared(character.WorldPosition, Enemy.WorldPosition);
            distanceTimer = DistanceCheckInterval;
            if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                bool closeEnough = true;
                float sqrRange = meleeWeapon.Range * meleeWeapon.Range;
                if (character.AnimController.InWater)
                {
                    if (sqrDistance > sqrRange) 
                    {
                        closeEnough = false;
                    }
                }
                else
                {
                    // It's possible that the center point of the creature is out of reach, but we could still hit the character.
                    float xDiff = Math.Abs(Enemy.WorldPosition.X - character.WorldPosition.X);
                    if (xDiff > meleeWeapon.Range)
                    {
                        closeEnough = false;
                    }
                    float yDiff = Math.Abs(Enemy.WorldPosition.Y - character.WorldPosition.Y);
                    if (yDiff > Math.Max(meleeWeapon.Range, 100))
                    {
                        closeEnough = false;
                    }
                    if (closeEnough && Enemy.WorldPosition.Y < character.WorldPosition.Y && yDiff > 25)
                    {
                        // The target is probably knocked down? -> try to reach it by crouching.
                        HumanAIController.AnimController.Crouch();
                    }
                }
                if (reloadTimer > 0) { return; }
                if (holdFireCondition != null && holdFireCondition()) { return; }
                if (closeEnough)
                {
                    UseWeapon(deltaTime);
                    character.AIController.SteeringManager.Reset();
                }
                else if (!character.IsFacing(Enemy.WorldPosition))
                {
                    // Don't do the facing check if we are close to the target, because it easily causes the character to get stuck here when it flips around.
                    SetAimTimer(Rand.Range(1f, 1.5f) / AimSpeed);
                }
            }
            else
            {
                if (WeaponComponent is RepairTool repairTool)
                {
                    float reach = AIObjectiveFixLeak.CalculateReach(repairTool, character);
                    if (sqrDistance > reach * reach) { return; }
                }
                float aimFactor = MathHelper.PiOver2 * (1 - AimAccuracy);
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.WorldPosition - Weapon.WorldPosition) < MathHelper.PiOver4 + aimFactor)
                {
                    // Check that we don't hit friendlies. No need to check the walls, because there's a separate check for that at 1096 (which intentionally has a small delay)
                    var pickedBodies = Submarine.PickBodies(Weapon.SimPosition, Submarine.GetRelativeSimPosition(from: Weapon, to: Enemy), 
                        ignoredBodies: character.AnimController.LimbBodies, 
                        Physics.CollisionCharacter);
                    
                    foreach (var body in pickedBodies)
                    {
                        if (body.UserData is Limb limb)
                        {
                            Character target = limb.character;
                            if (target != null && target != Enemy && HumanAIController.IsFriendly(target))
                            {
                                // Blocked by a friendly target.
                                isAimBlocked = true;
                                if (HumanAIController.DebugAI)
                                {
                                    BlockedPositions.Add(ConvertUnits.ToDisplayUnits(body.Position));
                                }
                                // Stand up, so that we might shoot past the friendlies that are crouching.
                                allowCrouching = false;
                                standUpTimer = StandUpCooldown;
                                return;
                            }
                        }

                    }
                    UseWeapon(deltaTime);
                }
            }
        }

        private bool allowCrouching;
        private float standUpTimer;
        private const float StandUpCooldown = 5;
        private void UseWeapon(float deltaTime)
        {
            // Enable this to debug intentional friendly fire.
            // if (isLethalWeapon && character.TeamID == Enemy.TeamID && character.IsOnPlayerTeam)
            // {
            //     // Never allow friendly crew (bots) to attack with deadly weapons (this check should be redundant)
            //     Debugger.Break();
            //     return;
            // }
            if (allowCrouching && !isMoving && !character.AnimController.InWater && WeaponComponent is not MeleeWeapon)
            {
                HumanAIController.AnimController.Crouch();
            }
            character.SetInput(InputType.Shoot, hit: false, held: true);
            Weapon.Use(deltaTime, user: character);
            SetReloadTime(WeaponComponent);
        }
        
        private float GetReloadTime(ItemComponent weaponComponent)
        {
            float reloadTime = 0;
            switch (weaponComponent)
            {
                case RangedWeapon rangedWeapon:
                {
                    if (rangedWeapon.ReloadTimer <= 0 && !rangedWeapon.HoldTrigger)
                    {
                        reloadTime = rangedWeapon.Reload;
                    }
                    break;
                }
                case MeleeWeapon mw:
                { 
                    reloadTime = mw.Reload;
                    break;
                }
            }
            return reloadTime;
        }
        
        private void SetReloadTime(ItemComponent weaponComponent)
        {
            float reloadTime = GetReloadTime(weaponComponent);
            reloadTimer = Math.Max(reloadTime, reloadTime * Rand.Range(1f, 1.25f) / AimSpeed);
        }
        
        private void ClearInputs()
        {
            //clear aim and shoot inputs so the bot doesn't immediately fire the weapon if it was previously e.g. using a scooter
            character.ClearInput(InputType.Aim);
            character.ClearInput(InputType.Shoot);
        }

        private bool ShouldUnequipWeapon =>
            Weapon != null &&
            character.Submarine != null &&
            character.Submarine.TeamID == character.TeamID &&
            Character.CharacterList.None(c => c.Submarine == character.Submarine && HumanAIController.IsActive(c) && !HumanAIController.IsFriendly(character, c) && HumanAIController.VisibleHulls.Contains(c.CurrentHull));

        protected override void OnCompleted()
        {
            base.OnCompleted();
            if (Enemy != null)
            {
                switch (Mode)
                {
                    case CombatMode.Offensive when Enemy.IsUnconscious && objectiveManager.HasObjectiveOrOrder<AIObjectiveFightIntruders>():
                        character.Speak(TextManager.Get("DialogTargetDown").Value, null, 3.0f, "targetdown".ToIdentifier(), 30.0f);
                        break;
                    case CombatMode.Arrest when IsCompleted:
                        if (!HumanAIController.IsTrueForAnyBotInTheCrew(bot => 
                                (bot != HumanAIController && bot.ObjectiveManager.CurrentObjective is AIObjectiveCombat { Mode: CombatMode.Arrest } combatObj && combatObj.Enemy == Enemy) || 
                                bot.ObjectiveManager.CurrentObjective is AIObjectiveGoTo { SourceObjective: AIObjectiveCombat combatObjective } && combatObjective.Enemy == Enemy))
                        {
                            // Go to the target and confiscate any stolen items, unless someone is already on it.
                            // Added on the root level, because the lifetime of the new objective exceeds the lifetime of this objective.
                            RemoveFollowTarget();
                            var approachArrestTarget = new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: false, getDivingGearIfNeeded: false, closeEnough: ArrestTargetDistance)
                            {
                                UsePathingOutside = false,
                                IgnoreIfTargetDead = true,
                                TargetName = Enemy.DisplayName,
                                AlwaysUseEuclideanDistance = false,
                                SpeakIfFails = false,
                                SourceObjective = this
                            };
                            approachArrestTarget.Completed += OnArrestTargetReached;
                            objectiveManager.AddObjective(approachArrestTarget);
                        }
                        break;
                }   
            }
            if (ShouldUnequipWeapon)
            {
                UnequipWeapon();
            }
            SteeringManager?.Reset();
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            if (ShouldUnequipWeapon)
            {
                UnequipWeapon();
            }
            SteeringManager?.Reset();
        }
        
        public override void OnDeselected()
        {
            base.OnDeselected();
            if (character.TeamID == CharacterTeamType.FriendlyNPC && IsOffensiveOrArrest && (!AllowHoldFire || (hasAimed && holdFireTimer <= 0)))
            {
                // Remember that the target resisted or acted offensively (we've aimed or tried to arrest/attack)
                Enemy.IsCriminal = true;
            }
        }
        
        public override void Reset()
        {
            base.Reset();
            hasAimed = false;
            holdFireTimer = 0;
            pathBackTimer = 0;
            standUpTimer = 0;
            isLethalWeapon = false;
            canSeeTarget = false;
            seekWeaponObjective = null;
            seekAmmunitionObjective = null;
            retreatObjective = null;
            followTargetObjective = null;
            retreatTarget = null;
            firstWarningTriggered = false;
            lastWarningTriggered = false;
        }

        /// <summary>
        /// Speak that we don't have weapons. But only outside of friendly subs (not that relevant there, reduces spam).
        /// </summary>
        private void SpeakNoWeapons()
        {
            if (!character.IsInFriendlySub)
            {
                PlayerCrewSpeak("dialogcombatnoweapons".ToIdentifier(), delay: 0, minDurationBetweenSimilar: 30);
            }
        }
        
        private void PlayerCrewSpeak(Identifier textIdentifier, float delay, float minDurationBetweenSimilar)
        {
            if (character.IsOnPlayerTeam)
            {
                Speak(textIdentifier, delay, minDurationBetweenSimilar);
            }
        }
        
        private void FriendlyGuardSpeak(Identifier textIdentifier, float delay, float minDurationBetweenSimilar)
        {
            if (character.TeamID == CharacterTeamType.FriendlyNPC && character.IsSecurity)
            {
                Speak(textIdentifier, delay, minDurationBetweenSimilar);
            }
        }
        
        private void Speak(Identifier textIdentifier, float delay, float minDurationBetweenSimilar)
        {
            LocalizedString msg = TextManager.Get(textIdentifier);
            if (!msg.IsNullOrEmpty())
            {
                character.Speak(msg.Value, identifier: textIdentifier, delay: delay, minDurationBetweenSimilar: minDurationBetweenSimilar);
            }
        }

        private void SetAimTimer(float newTimer) => aimTimer = Math.Max(aimTimer, newTimer);
    }
}
