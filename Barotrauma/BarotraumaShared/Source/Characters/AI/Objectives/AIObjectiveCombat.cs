using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override string DebugTag => "combat";

        private readonly CombatMode initialMode;

        const float coolDown = 10.0f;

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

        private readonly AIObjectiveFindSafety findSafety;
        private readonly HashSet<ItemComponent> weapons = new HashSet<ItemComponent>();

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
                findSafety.unreachable.Clear();
            }
            Mode = mode;
            initialMode = Mode;
            if (Enemy == null)
            {
                Mode = CombatMode.Retreat;
            }
        }

        public override float GetPriority() => (Enemy != null && (Enemy.Removed || Enemy.IsDead)) ? 0 : Math.Min(100 * PriorityModifier, 100);

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveCombat objective)) return false;
            return objective.Enemy == Enemy;
        }

        public override void OnSelected() => Weapon = null;

        public override bool IsCompleted()
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
            if (abandon) { return; }
            TryArm();
            if (seekAmmunition == null || !subObjectives.Contains(seekAmmunition))
            {
                if (!HoldPosition)
                {
                    Move();
                }
                if (WeaponComponent != null)
                {
                    OperateWeapon(deltaTime);
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

        private bool TryArm()
        {
            if (character.LockHands) { return false; }

            if (Weapon != null)
            {
                if (!character.Inventory.Items.Contains(Weapon) || WeaponComponent == null)
                {
                    Weapon = null;
                }
                else if (!WeaponComponent.HasRequiredContainedItems(false))
                {
                    // Seek ammunition only if cannot find a new weapon
                    if (!Reload(!HoldPosition, () => GetWeapon(out _) == null))
                    {
                        if (seekAmmunition != null && subObjectives.Contains(seekAmmunition))
                        {
                            return false;
                        }
                        else
                        {
                            Weapon = null;
                        }
                    }
                }
            }
            if (Weapon == null)
            {
                Weapon = GetWeapon(out _weaponComponent);
            }
            if (Weapon == null)
            {
                Weapon = GetWeapon(out _weaponComponent, ignoreRequiredItems: true);
            }
            Mode = Weapon == null ? CombatMode.Retreat : initialMode;
            return Weapon != null;
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

        private Item GetWeapon(out ItemComponent weaponComponent, bool ignoreRequiredItems = false)
        {
            weapons.Clear();
            _weaponComponent = null;
            foreach (var item in character.Inventory.Items)
            {
                if (item == null) { continue; }
                SeekWeapons(item);
                if (item.OwnInventory != null)
                {
                    item.OwnInventory.Items.ForEach(i => SeekWeapons(i));
                }
            }
            weaponComponent = weapons.OrderByDescending(w => w.CombatPriority).FirstOrDefault();
            if (weaponComponent == null) { return null; }
            if (weaponComponent.CombatPriority < 1) { return null; }
            return weaponComponent.Item;

            void SeekWeapons(Item item)
            {
                if (item == null) { return; }
                foreach (var component in item.Components)
                {
                    if (component is RangedWeapon rw)
                    {
                        if (ignoreRequiredItems || rw.HasRequiredContainedItems(false))
                        {
                            weapons.Add(rw);
                        }
                    }
                    else if (component is MeleeWeapon mw)
                    {
                        if (ignoreRequiredItems || mw.HasRequiredContainedItems(false))
                        {
                            weapons.Add(mw);
                        }
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
                                        if (ignoreRequiredItems || component.HasRequiredContainedItems(false))
                                        {
                                            weapons.Add(component);
                                        }
                                    }
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
            if (!WeaponComponent.HasRequiredContainedItems(false))
            {
                Mode = CombatMode.Retreat;
                return false;
            }
            //if (!character.SelectedItems.Contains(Weapon))
            if (!character.HasEquippedItem(Weapon))
            {
                Weapon.TryInteract(character, forceSelectKey: true);
                var slots = Weapon.AllowedSlots.FindAll(s => s == InvSlotType.LeftHand || s == InvSlotType.RightHand || s == (InvSlotType.LeftHand | InvSlotType.RightHand));
                if (character.Inventory.TryPutItem(Weapon, character, slots))
                {
                    Weapon.Equip(character);
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
                TryAddSubObjective(ref retreatObjective, () => new AIObjectiveGoTo(retreatTarget, character, objectiveManager, false, true));
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
                    Mode = CombatMode.Retreat;
                    SteeringManager.Reset();
                });
            if (followTargetObjective != null && subObjectives.Contains(followTargetObjective))
            {
                followTargetObjective.CloseEnough =
                    WeaponComponent is RangedWeapon ? 300 :
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
                onAbandon: () =>
                {
                    Weapon = null;
                    Mode = CombatMode.Retreat;
                    SteeringManager.Reset();
                });
        }
        
        /// <summary>
        /// Reloads the ammunition found in the inventory.
        /// If seekAmmo is true and the condition is met or not provided, tries to get find the ammo elsewhere.
        /// </summary>
        private bool Reload(bool seekAmmo, Func<bool> condition = null)
        {
            if (WeaponComponent == null) { return false; }
            if (!WeaponComponent.requiredItems.ContainsKey(RelatedItem.RelationType.Contained)) { return false; }
            var containedItems = Weapon.ContainedItems;
            RelatedItem item = null;
            Item ammunition = null;
            string[] ammunitionIdentifiers = null;
            foreach (RelatedItem requiredItem in WeaponComponent.requiredItems[RelatedItem.RelationType.Contained])
            {
                ammunition = containedItems.FirstOrDefault(it => it.Condition > 0.0f && requiredItem.MatchesItem(it));
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
                var container = Weapon.GetComponent<ItemContainer>();
                // Try reload ammunition in inventory
                foreach (string identifier in ammunitionIdentifiers)
                {
                    foreach (var i in character.Inventory.Items)
                    {
                        if (i == null) { continue; }
                        if (i.Prefab.Identifier == identifier || i.HasTag(identifier))
                        {
                            if (i.Condition > 0)
                            {
                                container.Inventory.TryPutItem(ammunition, null);
                            }
                        }
                    }
                }
            }
            if (WeaponComponent.HasRequiredContainedItems(false))
            {
                return true;
            }
            else if (ammunition == null)
            {
                if (seekAmmo && ammunitionIdentifiers != null && (condition == null || condition()))
                {
                    SeekAmmunition(ammunitionIdentifiers);
                }
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
                            aimTimer = Rand.Range(0.25f, 0.5f);
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
