using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics.Dynamics;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override string DebugTag => "combat";

        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;
        public override bool AllowOutsideSubmarine => true;
        public override bool AllowInAnySub => true;

        private readonly CombatMode initialMode;

        private float checkWeaponsTimer;
        private readonly float checkWeaponsInterval = 1;
        private float ignoreWeaponTimer;
        private readonly float ignoredWeaponsClearTime = 10;

        private readonly float goodWeaponPriority = 30;

        private readonly float arrestHoldFireTime = 8;
        private float holdFireTimer;
        private bool hasAimed;
        private bool isLethalWeapon;

        public Character Enemy { get; private set; }
        public bool HoldPosition { get; set; }

        private Item _weapon;
        private Item Weapon
        {
            get { return _weapon; }
            set
            {
                _weapon = value;
                _weaponComponent = null;
                hasAimed = false;
                RemoveSubObjective(ref seekAmmunitionObjective);
            }
        }
        private ItemComponent _weaponComponent;
        private ItemComponent WeaponComponent
        {
            get
            {
                if (Weapon == null) { return null; }
                if (_weaponComponent == null)
                {
                    _weaponComponent =
                        Weapon.GetComponent<RangedWeapon>() ??
                        Weapon.GetComponent<MeleeWeapon>() ??
                        Weapon.GetComponent<RepairTool>() as ItemComponent;
                }
                return _weaponComponent;
            }
        }

        public override bool ConcurrentObjectives => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;

        private readonly AIObjectiveFindSafety findSafety;
        private readonly HashSet<ItemComponent> weapons = new HashSet<ItemComponent>();
        private readonly HashSet<Item> ignoredWeapons = new HashSet<Item>();

        private AIObjectiveContainItem seekAmmunitionObjective;
        private AIObjectiveGoTo retreatObjective;
        private AIObjectiveGoTo followTargetObjective;
        private AIObjectiveGetItem seekWeaponObjective;

        private Hull retreatTarget;
        private float coolDownTimer;
        private IEnumerable<Body> myBodies;
        private float aimTimer;
        private float reloadTimer;
        private float spreadTimer;

        private bool canSeeTarget;
        private float visibilityCheckTimer;
        private readonly float visibilityCheckInterval = 0.2f;

        private float sqrDistance;
        private readonly float maxDistance = 2000;
        private readonly float distanceCheckInterval = 0.2f;
        private float distanceTimer;

        /// <summary>
        /// Aborts the objective when this condition is true
        /// </summary>
        public Func<bool> abortCondition;

        public bool allowHoldFire;

        /// <summary>
        /// Don't start using a weapon if this condition is true
        /// </summary>
        public Func<bool> holdFireCondition;

        public enum CombatMode
        {
            Defensive,
            Offensive,
            Arrest,
            Retreat,
            None
        }

        public CombatMode Mode { get; private set; }

        private bool IsOffensiveOrArrest => initialMode == CombatMode.Offensive || initialMode == CombatMode.Arrest;
        private bool TargetEliminated => IsEnemyDisabled || Enemy.IsUnconscious;
        private bool IsEnemyDisabled => Enemy == null || Enemy.Removed || Enemy.IsDead;

        private float AimSpeed => HumanAIController.AimSpeed;
        private float AimAccuracy => HumanAIController.AimAccuracy;

        private bool EnemyIsClose() => Enemy != null && character.CurrentHull != null && character.CurrentHull == Enemy.CurrentHull || Vector2.DistanceSquared(character.Position, Enemy.Position) < 500;

        public AIObjectiveCombat(Character character, Character enemy, CombatMode mode, AIObjectiveManager objectiveManager, float priorityModifier = 1, float coolDown = 10.0f) 
            : base(character, objectiveManager, priorityModifier)
        {
            if (mode == CombatMode.None)
            {
#if DEBUG
                DebugConsole.ThrowError("Combat mode == None");
#endif
                return;
            }
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
            spreadTimer = Rand.Range(-10, 10);
            HumanAIController.SortTimer = 0;
        }

        public override float GetPriority()
        {
            if (character.TeamID == CharacterTeamType.FriendlyNPC && Enemy != null)
            {
                if (Enemy.Submarine == null || (Enemy.Submarine.TeamID != character.TeamID && Enemy.Submarine != character.Submarine))
                {
                    Priority = 0;
                    Abandon = true;
                    return Priority;
                }
            }
            float damageFactor = MathUtils.InverseLerp(0.0f, 5.0f, HumanAIController.GetDamageDoneByAttacker(Enemy) / 100.0f);
            Priority = TargetEliminated ? 0 : Math.Min((95 + damageFactor) * PriorityModifier, 100);
            return Priority;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            ignoreWeaponTimer -= deltaTime;
            checkWeaponsTimer -= deltaTime;
            if (reloadTimer > 0)
            {
                reloadTimer -= deltaTime;
            }
            if (ignoreWeaponTimer < 0)
            {
                ignoredWeapons.Clear();
                ignoreWeaponTimer = ignoredWeaponsClearTime;
            }
            if (findSafety != null)
            {
                findSafety.Priority = 0;
            }
            distanceTimer -= deltaTime;
            if (distanceTimer < 0)
            {
                distanceTimer = distanceCheckInterval;
                sqrDistance = Vector2.DistanceSquared(character.WorldPosition, Enemy.WorldPosition);
            }
        }

        protected override bool Check()
        {
            if (IsOffensiveOrArrest && Mode != initialMode)
            {
                Abandon = true;
                return false;
            }
            if (sqrDistance > maxDistance * maxDistance)
            {
                // The target escaped from us.
                return true;
            }
            return IsEnemyDisabled || (!IsOffensiveOrArrest && coolDownTimer <= 0);
        }

        protected override void Act(float deltaTime)
        {
            if (abortCondition != null && abortCondition())
            {
                Abandon = true;
                return;
            }
            if (!IsOffensiveOrArrest)
            {
                coolDownTimer -= deltaTime;
            }
            if (seekAmmunitionObjective == null && seekWeaponObjective == null)
            {
                if (Mode != CombatMode.Retreat && TryArm() && !IsEnemyDisabled)
                {
                    OperateWeapon(deltaTime);
                }
                if (HoldPosition)
                {
                    SteeringManager.Reset();
                }
                else if (seekAmmunitionObjective == null && seekWeaponObjective == null)
                {
                    Move(deltaTime);
                }
                switch (Mode)
                {
                    case CombatMode.Offensive:
                        if (TargetEliminated && objectiveManager.IsCurrentOrder<AIObjectiveFightIntruders>())
                        {
                            character.Speak(TextManager.Get("DialogTargetDown"), null, 3.0f, "targetdown", 30.0f);
                        }
                        break;
                    case CombatMode.Arrest:
                        if (HumanAIController.HasItem(Enemy, "handlocker", out _, requireEquipped: true))
                        {
                            IsCompleted = true;
                        }
                        break;
                }
            }
        }

        private void Move(float deltaTime)
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                case CombatMode.Arrest:
                    Engage();
                    break;
                case CombatMode.Defensive:
                case CombatMode.Retreat:
                    Retreat(deltaTime);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private bool IsLoaded(ItemComponent weapon, bool checkContainedItems = true) => 
            weapon.HasRequiredContainedItems(character, addMessage: false) &&
            (!checkContainedItems || weapon.Item.OwnInventory == null || weapon.Item.OwnInventory.AllItems.Any(i => i.Condition > 0));

        private bool TryArm()
        {
            if (character.LockHands || Enemy == null)
            {
                Weapon = null;
                return false;
            }
            if (checkWeaponsTimer < 0)
            {
                checkWeaponsTimer = checkWeaponsInterval;
                // First go through all weapons and try to reload without seeking ammunition
                var allWeapons = FindWeaponsFromInventory();
                while (allWeapons.Any())
                {
                    Weapon = GetWeapon(allWeapons, out _weaponComponent);
                    if (Weapon == null)
                    {
                        // No weapons
                        break;
                    }
                    if (!character.Inventory.Contains(Weapon) || WeaponComponent == null)
                    {
                        // Not in the inventory anymore or cannot find the weapon component
                        allWeapons.Remove(WeaponComponent);
                        Weapon = null;
                        continue;
                    }
                    if (IsLoaded(WeaponComponent, checkContainedItems: true))
                    {
                        // All good, the weapon is loaded
                        break;
                    }
                    if (Reload(seekAmmo: false))
                    {
                        // All good, we can use the weapon.
                        break;
                    }
                    else
                    {
                        // No ammo.
                        allWeapons.Remove(WeaponComponent);
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
                bool isAllowedToSeekWeapons = !EnemyIsClose() && character.TeamID != CharacterTeamType.FriendlyNPC && IsOffensiveOrArrest;
                if (!isAllowedToSeekWeapons)
                {
                    if (WeaponComponent == null)
                    {
                        Mode = CombatMode.Retreat;
                    }
                }
                else if (seekAmmunitionObjective == null && (WeaponComponent == null || WeaponComponent.CombatPriority < goodWeaponPriority))
                {
                    // Poor weapon equipped -> try to find better.
                    RemoveSubObjective(ref seekAmmunitionObjective);
                    RemoveSubObjective(ref retreatObjective);
                    RemoveSubObjective(ref followTargetObjective);
                    TryAddSubObjective(ref seekWeaponObjective,
                        constructor: () => new AIObjectiveGetItem(character, "weapon", objectiveManager, equip: true, checkInventory: false)
                        {
                            GetItemPriority = i =>
                            {
                                if (Weapon != null && (i == Weapon || i.Prefab.Identifier == Weapon.Prefab.Identifier)) { return 0; }
                                if (i.IsOwnedBy(character)) { return 0; }
                                var mw = i.GetComponent<MeleeWeapon>();
                                var rw = i.GetComponent<RangedWeapon>();
                                float priority = 0;
                                if (mw != null)
                                {
                                    priority = mw.CombatPriority / 100;
                                }
                                else if (rw != null)
                                {
                                    priority = rw.CombatPriority / 100;
                                }
                                if (i.HasTag("stunner"))
                                {
                                    if (Mode == CombatMode.Arrest)
                                    {
                                        priority *= 2;
                                    }
                                    else
                                    {
                                        priority /= 2;
                                    }
                                }
                                return priority;
                            }
                        },
                        onCompleted: () => RemoveSubObjective(ref seekWeaponObjective),
                        onAbandon: () =>
                        {
                            RemoveSubObjective(ref seekWeaponObjective);
                            Mode = CombatMode.Retreat;
                        });
                }
            }
            else
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
                if (!IsLoaded(WeaponComponent))
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

        private Item GetWeapon(IEnumerable<ItemComponent> weaponList, out ItemComponent weaponComponent)
        {
            weaponComponent = null;
            float bestPriority = 0;
            float lethalDmg = -1;
            bool enemyIsClose = EnemyIsClose();
            foreach (var weapon in weaponList)
            {
                float priority = weapon.CombatPriority;
                if (!IsLoaded(weapon))
                {
                    if (weapon is RangedWeapon && enemyIsClose)
                    {
                        // Close to the enemy. Ignore weapons that don't have any ammunition (-> Don't seek ammo).
                        continue;
                    }
                    else
                    {
                        // Halve the priority for weapons that don't have proper ammunition loaded.
                        priority /= 2;
                    }
                }
                if (Enemy.Stun > 1)
                {
                    // Enemy is stunned, reduce the priority of stunner weapons.
                    Attack attack = GetAttackDefinition(weapon);
                    if (attack != null)
                    {
                        lethalDmg = attack.GetTotalDamage();
                        float max = lethalDmg + 1;
                        if (weapon.Item.HasTag("stunner"))
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
                    if (weapon.Item.HasTag("stunner"))
                    {
                        priority *= 2;
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
                else if (weapon is MeleeWeapon && weapon.Item.HasTag("stunner") && !CanMeleeStunnerStun(weapon))
                {
                    Attack attack = GetAttackDefinition(weapon);
                    priority = attack?.GetTotalDamage() ?? priority / 2;
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
                if (weaponComponent.Item.HasTag("stunner"))
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
                if (allowHoldFire && !hasAimed && holdFireTimer <= 0)
                {
                    holdFireTimer = arrestHoldFireTime * Rand.Range(0.75f, 1.25f);
                }
            }
            return weaponComponent.Item;

            static Attack GetAttackDefinition(ItemComponent weapon)
            {
                Attack attack = null;
                if (weapon is MeleeWeapon meleeWeapon)
                {
                    attack = meleeWeapon.Attack;
                }
                else if (weapon is RangedWeapon rangedWeapon)
                {
                    attack = rangedWeapon.FindProjectile(triggerOnUseOnContainers: false)?.Attack;
                }
                return attack;
            }

            static float GetLethalDamage(ItemComponent weapon)
            {
                float lethalDmg = 0;
                Attack attack = GetAttackDefinition(weapon);
                if (attack != null)
                {
                    lethalDmg = attack.GetTotalDamage();
                }
                return lethalDmg;
            }

            float ApproximateStunDamage(ItemComponent weapon, Attack attack)
            {
                // Try to reduce the priority using the actual damage values and status effects.
                // This is an approximation, because we can't check the status effect conditions here.
                // The result might be incorrect if there is a high stun effect that's only applied in certain conditions.
                var statusEffects = attack.StatusEffects.Where(se => !se.HasConditions && se.type == ActionType.OnUse && se.HasRequiredItems(character));
                if (weapon.statusEffectLists != null && weapon.statusEffectLists.TryGetValue(ActionType.OnUse, out List<StatusEffect> hitEffects))
                {
                    statusEffects = statusEffects.Concat(hitEffects);
                }
                float afflictionsStun = attack.Afflictions.Keys.Sum(a => a.Identifier == "stun" ? a.Strength : 0);
                float effectsStun = statusEffects.None() ? 0 : statusEffects.Max(se =>
                {
                    float stunAmount = 0;
                    var stunAffliction = se.Afflictions.Find(a => a.Identifier == "stun");
                    if (stunAffliction != null)
                    {
                        stunAmount = stunAffliction.Strength;
                    }
                    return stunAmount;
                });
                return attack.Stun + afflictionsStun + effectsStun;
            }

            bool CanMeleeStunnerStun(ItemComponent weapon)
            {
                // If there's an item container that takes a battery,
                // assume that it's required for the stun effect
                // as we can't check the status effect conditions here.
                var mobileBatteryTag = "mobilebattery";
                var containers = weapon.Item.Components.Where(ic => ic is ItemContainer container &&
                    container.ContainableItems.Any(containable => containable.Identifiers.Any(id => id.Equals(mobileBatteryTag))));
                // If there's no such container, assume that the melee weapon can stun without a battery.
                return containers.None() || containers.Any(container =>
                    (container as ItemContainer)?.Inventory.AllItems.Any(i => i != null && i.HasTag(mobileBatteryTag) && i.Condition > 0.0f) ?? false);
            }
        }

        private HashSet<ItemComponent> FindWeaponsFromInventory()
        {
            weapons.Clear();
            foreach (var item in character.Inventory.AllItems)
            {
                if (ignoredWeapons.Contains(item)) { continue; }
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
                if (component.CombatPriority > 0)
                {
                    weaponList.Add(component);
                }
            }
        }

        private void Unequip()
        {
            if (!character.LockHands && character.HeldItems.Contains(Weapon))
            {
                if (!Weapon.AllowedSlots.Contains(InvSlotType.Any) || !character.Inventory.TryPutItem(Weapon, character, new List<InvSlotType>() { InvSlotType.Any }))
                {
                    Weapon.Drop(character);
                }
            }
        }

        private bool Equip()
        {
            if (character.LockHands) { return false; }
            if (!WeaponComponent.HasRequiredContainedItems(character, addMessage: false))
            {
                return false;
            }
            if (!character.HasEquippedItem(Weapon))
            {
                Weapon.TryInteract(character, forceSelectKey: true);
                var slots = Weapon.AllowedSlots.Where(s => s == InvSlotType.LeftHand || s == InvSlotType.RightHand || s == (InvSlotType.LeftHand | InvSlotType.RightHand));
                if (character.Inventory.TryPutItem(Weapon, character, slots))
                {
                    aimTimer = Rand.Range(0.2f, 0.4f) / AimSpeed;
                }
                else
                {
                    Weapon = null;
                    Mode = CombatMode.Retreat;
                    return false;
                }
            }
            return true;
        }

        private float findHullTimer;
        private readonly float findHullInterval = 1.0f;

        private void Retreat(float deltaTime)
        {
            RemoveFollowTarget();
            RemoveSubObjective(ref seekAmmunitionObjective);
            if (retreatObjective != null && retreatObjective.Target != retreatTarget)
            {
                RemoveSubObjective(ref retreatObjective);
            }
            if (retreatTarget == null || (retreatObjective != null && !retreatObjective.CanBeCompleted))
            {
                if (findHullTimer > 0)
                {
                    findHullTimer -= deltaTime;
                }
                else
                {
                    retreatTarget = findSafety.FindBestHull(HumanAIController.VisibleHulls, allowChangingTheSubmarine: character.TeamID != CharacterTeamType.FriendlyNPC);
                    findHullTimer = findHullInterval * Rand.Range(0.9f, 1.1f);
                }
            }
            if (retreatTarget != null && character.CurrentHull != retreatTarget)
            {
                TryAddSubObjective(ref retreatObjective, () => new AIObjectiveGoTo(retreatTarget, character, objectiveManager, false, true),
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

        private void Engage()
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
            if (followTargetObjective != null && followTargetObjective.Target != Enemy)
            {
                RemoveFollowTarget();
            }
            TryAddSubObjective(ref followTargetObjective,
                constructor: () => new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: true, getDivingGearIfNeeded: true, closeEnough: 50)
                {
                    IgnoreIfTargetDead = true,
                    DialogueIdentifier = "dialogcannotreachtarget",
                    TargetName = Enemy.DisplayName
                },
                onAbandon: () => Abandon = true);
            if (followTargetObjective == null) { return; }
            if (Mode == CombatMode.Arrest && Enemy.Stun > 2)
            {
                if (HumanAIController.HasItem(character, "handlocker", out _))
                {
                    if (!arrestingRegistered)
                    {
                        arrestingRegistered = true;
                        followTargetObjective.Completed += OnArrestTargetReached;
                    }
                    followTargetObjective.CloseEnough = 100;
                }
                else
                {
                    if (character.TeamID == CharacterTeamType.FriendlyNPC)
                    {
                        ItemPrefab prefab = ItemPrefab.Find(null, "handcuffs");
                        if (prefab != null)
                        {
                            Entity.Spawner.AddToSpawnQueue(prefab, character.Inventory, onSpawned: (Item i) => i.SpawnedInOutpost = true);
                        }
                    }
                    RemoveFollowTarget();
                    SteeringManager.Reset();
                }
            }
            if (followTargetObjective != null)
            {
                followTargetObjective.CloseEnough =
                    WeaponComponent is RangedWeapon ? 1000 :
                    WeaponComponent is MeleeWeapon mw ? mw.Range :
                    WeaponComponent is RepairTool rt ? rt.Range : 50;
            }
        }

        private bool arrestingRegistered;

        private void RemoveFollowTarget()
        {
            if (arrestingRegistered)
            {
                followTargetObjective.Completed -= OnArrestTargetReached;
            }
            RemoveSubObjective(ref followTargetObjective);
            arrestingRegistered = false;
        }

        private void OnArrestTargetReached()
        {
            if (HumanAIController.HasItem(character, "handlocker", out IEnumerable<Item> matchingItems) && Enemy.Stun > 0 && character.CanInteractWith(Enemy))
            {
                var handCuffs = matchingItems.First();
                if (!HumanAIController.TakeItem(handCuffs, Enemy.Inventory, equip: true))
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Failed to handcuff the target.", Color.Red);
#endif
                }
                // Confiscate stolen goods.
                foreach (var item in Enemy.Inventory.AllItemsMod)
                {
                    if (item == handCuffs) { continue; }
                    if (item.StolenDuringRound)
                    {
                        item.Drop(character);
                        character.Inventory.TryPutItem(item, character, CharacterInventory.anySlot);
                    }
                }
                character.Speak(TextManager.Get("DialogTargetArrested"), null, 3.0f, "targetarrested", 30.0f);
                IsCompleted = true;
            }
        }

        /// <summary>
        /// Seeks for more ammunition. Creates a new subobjective.
        /// </summary>
        private void SeekAmmunition(string[] ammunitionIdentifiers)
        {
            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveSubObjective(ref seekWeaponObjective);
            RemoveFollowTarget();
            TryAddSubObjective(ref seekAmmunitionObjective,
                constructor: () => new AIObjectiveContainItem(character, ammunitionIdentifiers, Weapon.GetComponent<ItemContainer>(), objectiveManager)
                {
                    targetItemCount = Weapon.GetComponent<ItemContainer>().Capacity,
                    checkInventory = false
                },
                onCompleted: () => RemoveSubObjective(ref seekAmmunitionObjective),
                onAbandon: () =>
                {
                    SteeringManager.Reset();
                    RemoveSubObjective(ref seekAmmunitionObjective);
                    ignoredWeapons.Add(Weapon);
                    Weapon = null;
                });
        }
        
        /// <summary>
        /// Reloads the ammunition found in the inventory.
        /// If seekAmmo is true, tries to get find the ammo elsewhere.
        /// </summary>
        private bool Reload(bool seekAmmo)
        {
            if (WeaponComponent == null) { return false; }        
            if (Weapon.OwnInventory == null) { return true; }
            // Eject empty ammo
            HumanAIController.UnequipEmptyItems(Weapon);
            RelatedItem item = null;
            Item ammunition = null;
            string[] ammunitionIdentifiers = null;
            if (WeaponComponent.requiredItems.ContainsKey(RelatedItem.RelationType.Contained))
            {
                foreach (RelatedItem requiredItem in WeaponComponent.requiredItems[RelatedItem.RelationType.Contained])
                {
                    ammunition = Weapon.OwnInventory.AllItems.FirstOrDefault(it => it.Condition > 0 && requiredItem.MatchesItem(it));
                    if (ammunition != null)
                    {
                        // Ammunition still remaining
                        return true;
                    }
                    item = requiredItem;
                    ammunitionIdentifiers = requiredItem.Identifiers;
                }
            }
            else if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                ammunitionIdentifiers = meleeWeapon.PreferredContainedItems;
            }

            // No ammo
            if (ammunition == null)
            {
                if (ammunitionIdentifiers != null)
                {
                    // Try reload ammunition from inventory
                    ammunition = character.Inventory.FindItem(i => ammunitionIdentifiers.Any(id => id == i.Prefab.Identifier || i.HasTag(id)) && i.Condition > 0, true);
                    if (ammunition != null)
                    {
                        var container = Weapon.GetComponent<ItemContainer>();
                        if (!container.Inventory.TryPutItem(ammunition, null))
                        {
                            if (ammunition.ParentInventory == character.Inventory)
                            {
                                ammunition.Drop(character);
                            }
                        }
                    }
                }
            }
            if (WeaponComponent.HasRequiredContainedItems(character, addMessage: false))
            {
                return true;
            }
            else if (ammunition == null && !HoldPosition && IsOffensiveOrArrest && seekAmmo && ammunitionIdentifiers != null)
            {
                SeekAmmunition(ammunitionIdentifiers);
            }
            return false;
        }

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
                visibilityCheckTimer = visibilityCheckInterval;
            }
            if (!canSeeTarget)
            {
                aimTimer = Rand.Range(0.2f, 0.4f) / AimSpeed;
                return;
            }
            if (Weapon.RequireAimToUse)
            {
                character.SetInput(InputType.Aim, false, true);
            }
            hasAimed = true;
            if (holdFireTimer > 0)
            {
                holdFireTimer -= deltaTime;
                return;
            }
            if (aimTimer > 0)
            {
                aimTimer -= deltaTime;
                return;
            }
            if (reloadTimer > 0) { return; }
            if (Mode == CombatMode.Arrest && isLethalWeapon && Enemy.Stun > 1) { return; }
            if (holdFireCondition != null && holdFireCondition()) { return; }
            float sqrDist = Vector2.DistanceSquared(character.Position, Enemy.Position);
            if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                bool closeEnough = true;
                float sqrRange = meleeWeapon.Range * meleeWeapon.Range;
                if (character.AnimController.InWater)
                {
                    if (sqrDist > sqrRange) 
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
                        HumanAIController.AnimController.Crouching = true;
                    }
                }
                if (closeEnough)
                {
                    UseWeapon(deltaTime);
                }
                else if (!character.IsFacing(Enemy.WorldPosition))
                {
                    // Don't do the facing check if we are close to the target, because it easily causes the character to get stuck here when it flips around.
                    aimTimer = Rand.Range(1f, 1.5f) / AimSpeed;
                }
            }
            else
            {
                if (WeaponComponent is RepairTool repairTool)
                {
                    if (sqrDist > repairTool.Range * repairTool.Range) { return; }
                }
                float aimFactor = MathHelper.PiOver2 * (1 - AimAccuracy);
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.Position - Weapon.Position) < MathHelper.PiOver4 + aimFactor)
                {
                    if (myBodies == null)
                    {
                        myBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody);
                    }
                    var collisionCategories = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;
                    var pickedBody = Submarine.PickBody(Weapon.SimPosition, Enemy.SimPosition, myBodies, collisionCategories, allowInsideFixture: true);
                    if (pickedBody != null)
                    {
                        Character target = null;
                        if (pickedBody.UserData is Character c)
                        {
                            target = c;
                        }
                        else if (pickedBody.UserData is Limb limb)
                        {
                            target = limb.character;
                        }
                        if (target != null && (target == Enemy || !HumanAIController.IsFriendly(target)))
                        {
                            UseWeapon(deltaTime);
                        }
                    }
                }
            }
        }

        private void UseWeapon(float deltaTime)
        {
            float reloadTime = 0;
            if (WeaponComponent is RangedWeapon rangedWeapon)
            {
                // If the weapon is just equipped, we can't shoot just yet.
                if (rangedWeapon.ReloadTimer <= 0)
                {
                    reloadTime = rangedWeapon.Reload;
                }
            }
            if (WeaponComponent is MeleeWeapon mw)
            {
                if (!((HumanoidAnimController)character.AnimController).Crouching)
                {
                    reloadTime = mw.Reload;
                }
            }
            character.SetInput(InputType.Shoot, false, true);
            Weapon.Use(deltaTime, character);
            reloadTimer = Math.Max(reloadTime, reloadTime * Rand.Range(1f, 1.25f) / AimSpeed);
        }

        protected override void OnCompleted()
        {
            base.OnCompleted();
            if (Weapon != null)
            {
                Unequip();
            }
            SteeringManager.Reset();
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            if (Weapon != null)
            {
                Unequip();
            }
            SteeringManager.Reset();
        }

        public override void Reset()
        {
            base.Reset();
            hasAimed = false;
            isLethalWeapon = false;
            canSeeTarget = false;
            seekWeaponObjective = null;
            seekAmmunitionObjective = null;
            retreatObjective = null;
            followTargetObjective = null;
            retreatTarget = null;
        }

        //private float CalculateEnemyStrength()
        //{
        //    float enemyStrength = 0;
        //    AttackContext currentContext = character.GetAttackContext();
        //    foreach (Limb limb in Enemy.AnimController.Limbs)
        //    {
        //        if (limb.attack == null) continue;
        //        if (!limb.attack.IsValidContext(currentContext)) { continue; }
        //        if (!limb.attack.IsValidTarget(AttackTarget.Character)) { continue; }
        //        enemyStrength += limb.attack.GetTotalDamage(false);
        //    }
        //    return enemyStrength;
        //}
    }
}
