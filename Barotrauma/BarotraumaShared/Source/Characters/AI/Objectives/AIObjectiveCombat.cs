using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override string DebugTag => "combat";

        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;

        private readonly CombatMode initialMode;

        private float seekWeaponsTimer;
        const float seekWeaponsInterval = 1;
        private float ignoreWeaponTimer;
        const float ignoredWeaponsClearTime = 10;

        const float coolDown = 10.0f;
        // Won't take the offensive with weapons that have lower priority than this
        const float goodWeaponPriority = 30;

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
                RemoveSubObjective(ref seekAmmunition);
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
                        Weapon.GetComponent<RangedWeapon>() as ItemComponent ??
                        Weapon.GetComponent<MeleeWeapon>() as ItemComponent ??
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

        private AIObjectiveContainItem seekAmmunition;
        private AIObjectiveGoTo retreatObjective;
        private AIObjectiveGoTo followTargetObjective;

        private Hull retreatTarget;
        private float coolDownTimer;
        private IEnumerable<FarseerPhysics.Dynamics.Body> myBodies;
        private float aimTimer;

        public enum CombatMode
        {
            Defensive,
            Offensive,
            Retreat
        }

        public CombatMode Mode { get; private set; }

        public AIObjectiveCombat(Character character, Character enemy, CombatMode mode, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
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
        }

        public override float GetPriority() => (Enemy != null && (Enemy.Removed || Enemy.IsDead)) ? 0 : Math.Min(100 * PriorityModifier, 100);

        public override void OnSelected() => Weapon = null;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            ignoreWeaponTimer -= deltaTime;
            seekWeaponsTimer -= deltaTime;
            if (ignoreWeaponTimer < 0)
            {
                ignoredWeapons.Clear();
                ignoreWeaponTimer = ignoredWeaponsClearTime;
            }
        }

        protected override bool Check()
        {
            bool completed = (Enemy != null && (Enemy.Removed || Enemy.IsDead)) || (initialMode != CombatMode.Offensive && coolDownTimer <= 0);
            if (completed)
            {
                if (objectiveManager.CurrentOrder == this && Enemy != null && Enemy.IsDead)
                {
                    character.Speak(TextManager.Get("DialogTargetDown"), null, 3.0f, "targetdown", 30.0f);
                }
                if (Weapon != null)
                {
                    Unequip();
                }
            }
            return completed;
        }

        protected override void Act(float deltaTime)
        {
            if (initialMode != CombatMode.Offensive)
            {
                coolDownTimer -= deltaTime;
            }
            if (Abandon) { return; }
            if (seekAmmunition == null)
            {
                if (TryArm())
                {
                    OperateWeapon(deltaTime);
                }
                if (!HoldPosition && seekAmmunition == null)
                {
                    Move();
                }
            }
        }

        private void Move()
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                    Engage();
                    break;
                case CombatMode.Defensive:
                case CombatMode.Retreat:
                    Retreat();
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private bool IsLoaded(ItemComponent weapon) => weapon.HasRequiredContainedItems(character, addMessage: false);

        private bool TryArm()
        {
            if (character.LockHands)
            {
                Weapon = null;
                return false;
            }
            if (seekWeaponsTimer < 0)
            {
                seekWeaponsTimer = seekWeaponsInterval;
                // First go through all weapons and try to reload without seeking ammunition
                var allWeapons = GetAllWeapons().ToList();
                while (allWeapons.Any())
                {
                    Weapon = GetWeapon(allWeapons, out _weaponComponent);
                    if (Weapon == null)
                    {
                        // No weapons
                        break;
                    }
                    if (!character.Inventory.Items.Contains(Weapon) || WeaponComponent == null)
                    {
                        // Not in the inventory anymore or cannot find the weapon component
                        allWeapons.Remove(WeaponComponent);
                        Weapon = null;
                        continue;
                    }
                    if (initialMode == CombatMode.Offensive)
                    {
                        // In the offensive mode, let's ignore weapons that cannot be used in the offensive mode
                        if (WeaponComponent.CombatPriority < goodWeaponPriority)
                        {
                            allWeapons.Remove(WeaponComponent);
                            Weapon = null;
                            continue;
                        }
                    }
                    if (IsLoaded(WeaponComponent))
                    {
                        // All good, the weapon is loaded
                        break;
                    }
                    if (Reload(seekAmmo: false))
                    {
                        // All good, reloading successful
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
                    Weapon = GetWeapon(out _weaponComponent);
                    if (Weapon != null)
                    {
                        if (!CheckWeapon(seekAmmo: true))
                        {
                            if (seekAmmunition != null)
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
            }
            else
            {
                if (!CheckWeapon(seekAmmo: false))
                {
                    Weapon = null;
                }
            }
            if (Weapon == null)
            {
                Mode = CombatMode.Retreat;
            }
            else
            {
                Mode = WeaponComponent.CombatPriority >= goodWeaponPriority ? initialMode : CombatMode.Defensive;
            }
            return Weapon != null;

            bool CheckWeapon(bool seekAmmo)
            {
                if (!character.Inventory.Items.Contains(Weapon) || WeaponComponent == null)
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

        private Item GetWeapon(out ItemComponent weaponComponent)
        {
            GetAllWeapons();
            return GetWeapon(weapons, out weaponComponent);
        }

        private Item GetWeapon(IEnumerable<ItemComponent> weaponList, out ItemComponent weaponComponent)
        {
            weaponComponent = weaponList.OrderByDescending(w => CalculateWeaponPriority(w)).FirstOrDefault();
            if (weaponComponent == null) { return null; }
            if (weaponComponent.CombatPriority < 1) { return null; }
            return weaponComponent.Item;
        }

        private float CalculateWeaponPriority(ItemComponent weapon)
        {
            float priority = weapon.CombatPriority;
            // Halve the priority for weapons that don't have proper ammunition loaded.
            if (!weapon.HasRequiredContainedItems(character, addMessage: false))
            {
                priority /= 2;
            }
            return priority;
        }

        private HashSet<ItemComponent> GetAllWeapons()
        {
            weapons.Clear();
            foreach (var item in character.Inventory.Items)
            {
                if (item == null) { continue; }
                if (ignoredWeapons.Contains(item)) { continue; }
                SeekWeapons(item, weapons);
                if (item.OwnInventory != null)
                {
                    item.OwnInventory.Items.ForEach(i => SeekWeapons(i, weapons));
                }
            }
            return weapons;
        }

        private void SeekWeapons(Item item, ICollection<ItemComponent> weaponList)
        {
            if (item == null) { return; }
            foreach (var component in item.Components)
            {
                if (component is RangedWeapon rw)
                {
                    weaponList.Add(rw);
                }
                else if (component is MeleeWeapon mw)
                {
                    weaponList.Add(mw);
                }
                else
                {
                    var effects = component.statusEffectLists;
                    if (effects != null)
                    {
                        foreach (var statusEffects in effects.Values)
                        {
                            foreach (var statusEffect in statusEffects)
                            {
                                if (statusEffect.Afflictions.Any())
                                {
                                    weaponList.Add(component);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void Unequip()
        {
            if (!character.LockHands && character.SelectedItems.Contains(Weapon))
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
                var slots = Weapon.AllowedSlots.FindAll(s => s == InvSlotType.LeftHand || s == InvSlotType.RightHand || s == (InvSlotType.LeftHand | InvSlotType.RightHand));
                if (character.Inventory.TryPutItem(Weapon, character, slots))
                {
                    aimTimer = Rand.Range(0.5f, 1f);
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

        private void Retreat()
        {
            RemoveSubObjective(ref followTargetObjective);
            RemoveSubObjective(ref seekAmmunition);
            if (retreatObjective != null && retreatObjective.Target != retreatTarget)
            {
                retreatObjective = null;
            }
            if (retreatTarget == null || (retreatObjective != null && !retreatObjective.CanBeCompleted))
            {
                retreatTarget = findSafety.FindBestHull(HumanAIController.VisibleHulls);
            }
            if (character.CurrentHull != retreatTarget)
            {
                TryAddSubObjective(ref retreatObjective, () => new AIObjectiveGoTo(retreatTarget, character, objectiveManager, false, true), 
                    onAbandon: () => RemoveSubObjective(ref retreatObjective), 
                    onCompleted: () => RemoveSubObjective(ref retreatObjective));
            }
        }

        private void Engage()
        {
            if (character.LockHands)
            {
                Mode = CombatMode.Retreat;
                SteeringManager.Reset();
                return;
            }

            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveSubObjective(ref seekAmmunition);
            if (followTargetObjective != null && followTargetObjective.Target != Enemy)
            {
                followTargetObjective = null;
            }
            TryAddSubObjective(ref followTargetObjective,
                constructor: () => new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: true, getDivingGearIfNeeded: true)
                {
                    AllowGoingOutside = true,
                    IgnoreIfTargetDead = true
                },
                onAbandon: () =>
                {
                    Mode = CombatMode.Defensive;
                    SteeringManager.Reset();
                    RemoveSubObjective(ref followTargetObjective);
                });
            if (followTargetObjective != null)
            {
                followTargetObjective.CloseEnough =
                    WeaponComponent is RangedWeapon ? 1000 :
                    WeaponComponent is MeleeWeapon mw ? mw.Range :
                    WeaponComponent is RepairTool rt ? rt.Range : 50;
            }
        }

        /// <summary>
        /// Seeks for more ammunition. Creates a new subobjective.
        /// </summary>
        private void SeekAmmunition(string[] ammunitionIdentifiers)
        {
            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveSubObjective(ref followTargetObjective);
            TryAddSubObjective(ref seekAmmunition,
                constructor: () => new AIObjectiveContainItem(character, ammunitionIdentifiers, Weapon.GetComponent<ItemContainer>(), objectiveManager)
                {
                    targetItemCount = Weapon.GetComponent<ItemContainer>().Capacity,
                    checkInventory = false
                },
                onCompleted: () => RemoveSubObjective(ref seekAmmunition),
                onAbandon: () =>
                {
                    SteeringManager.Reset();
                    RemoveSubObjective(ref seekAmmunition);
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
            if (!WeaponComponent.requiredItems.ContainsKey(RelatedItem.RelationType.Contained)) { return false; }
            var containedItems = Weapon.ContainedItems;
            // Drop empty ammo
            foreach (Item containedItem in containedItems)
            {
                if (containedItem == null) { continue; }
                if (containedItem.Condition <= 0)
                {
                    containedItem.Drop(character);
                }
            }
            RelatedItem item = null;
            Item ammunition = null;
            string[] ammunitionIdentifiers = null;
            foreach (RelatedItem requiredItem in WeaponComponent.requiredItems[RelatedItem.RelationType.Contained])
            {
                ammunition = containedItems.FirstOrDefault(it => it.Condition > 0 && requiredItem.MatchesItem(it));
                if (ammunition != null)
                {
                    // Ammunition still remaining
                    return true;
                }
                item = requiredItem;
                ammunitionIdentifiers = requiredItem.Identifiers;
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
                        if (container.Item.ParentInventory == character.Inventory)
                        {
                            character.Inventory.RemoveItem(ammunition);
                            container.Inventory.TryPutItem(ammunition, null);
                        }
                        else
                        {
                            container.Combine(ammunition);
                        }
                    }
                }
            }
            if (WeaponComponent.HasRequiredContainedItems(character, addMessage: false))
            {
                return true;
            }
            else if (ammunition == null && !HoldPosition && initialMode == CombatMode.Offensive && seekAmmo && ammunitionIdentifiers != null)
            {
                SeekAmmunition(ammunitionIdentifiers);
            }
            return false;
        }

        private void Attack(float deltaTime)
        {
            float squaredDistance = Vector2.DistanceSquared(character.Position, Enemy.Position);
            character.CursorPosition = Enemy.Position;
            float engageDistance = 500;
            if (character.CurrentHull != Enemy.CurrentHull && squaredDistance > engageDistance * engageDistance) { return; }
            if (!character.CanSeeCharacter(Enemy)) { return; }
            if (Weapon.RequireAimToUse)
            {
                bool isOperatingButtons = false;
                if (SteeringManager == PathSteering)
                {
                    var door = PathSteering.CurrentPath?.CurrentNode?.ConnectedDoor;
                    if (door != null && !door.IsOpen)
                    {
                        isOperatingButtons = door.HasIntegratedButtons || door.Item.GetConnectedComponents<Controller>(true).Any();
                    }
                }
                if (!isOperatingButtons)
                {
                    character.SetInput(InputType.Aim, false, true);
                }
            }
            bool isFacing = character.AnimController.Dir > 0 && Enemy.WorldPosition.X > character.WorldPosition.X || character.AnimController.Dir < 0 && Enemy.WorldPosition.X < character.WorldPosition.X;
            if (!isFacing)
            {
                aimTimer = Rand.Range(1f, 1.5f);
            }
            if (aimTimer > 0)
            {
                aimTimer -= deltaTime;
                return;
            }
            if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                if (squaredDistance <= meleeWeapon.Range * meleeWeapon.Range)
                {
                    character.SetInput(InputType.Shoot, false, true);
                    Weapon.Use(deltaTime, character);
                }
            }
            else
            {
                if (WeaponComponent is RepairTool repairTool)
                {
                    if (squaredDistance > repairTool.Range * repairTool.Range) { return; }
                }
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.Position - Weapon.Position) < MathHelper.PiOver4)
                {
                    if (myBodies == null)
                    {
                        myBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody);
                    }
                    var collisionCategories = Physics.CollisionCharacter | Physics.CollisionWall;
                    var pickedBody = Submarine.PickBody(Weapon.SimPosition, Enemy.SimPosition, myBodies, collisionCategories);
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
                            character.SetInput(InputType.Shoot, false, true);
                            Weapon.Use(deltaTime, character);
                            float reloadTime = 0;
                            if (WeaponComponent is RangedWeapon rangedWeapon)
                            {
                                reloadTime = rangedWeapon.Reload;
                            }
                            if (WeaponComponent is MeleeWeapon mw)
                            {
                                reloadTime = mw.Reload;
                            }
                            aimTimer = reloadTime * Rand.Range(1f, 1.5f);
                        }
                    }
                }
            }
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
