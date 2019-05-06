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
        public override bool KeepDivingGearOn => true;
        public bool useCoolDown = true;

        const float CoolDown = 10.0f;

        public Character Enemy { get; private set; }
        
        private Item _weapon;
        private Item Weapon
        {
            get { return _weapon; }
            set
            {
                _weapon = value;
                _weaponComponent = null;
                reloadWeaponObjective = null;
            }
        }
        private ItemComponent _weaponComponent;
        private ItemComponent WeaponComponent
        {
            get
            {
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

        private readonly AIObjectiveFindSafety findSafety;
        private readonly HashSet<RangedWeapon> rangedWeapons = new HashSet<RangedWeapon>();
        private readonly HashSet<MeleeWeapon> meleeWeapons = new HashSet<MeleeWeapon>();
        private readonly HashSet<Item> adHocWeapons = new HashSet<Item>();

        private AIObjectiveContainItem reloadWeaponObjective;
        private AIObjectiveGoTo retreatObjective;
        private AIObjectiveGoTo followTargetObjective;

        private Hull retreatTarget;
        private float coolDownTimer;

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
            coolDownTimer = CoolDown;
            findSafety = objectiveManager.GetObjective<AIObjectiveFindSafety>();
            if (findSafety != null)
            {
                findSafety.Priority = 0;
                findSafety.unreachable.Clear();
            }
            Mode = mode;
            if (Enemy == null)
            {
                Mode = CombatMode.Retreat;
            }
        }

        protected override void Act(float deltaTime)
        {
            if (useCoolDown)
            {
                coolDownTimer -= deltaTime;
            }
            if (abandon) { return; }
            Arm(deltaTime);
            Move(deltaTime);
        }

        private void Arm(float deltaTime)
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                case CombatMode.Defensive:
                    if (Weapon != null && !character.Inventory.Items.Contains(_weapon) || _weaponComponent != null && !_weaponComponent.HasRequiredContainedItems(false))
                    {
                        Weapon = null;
                    }
                    if (Weapon == null)
                    {
                        Weapon = GetWeapon();
                    }
                    if (Weapon == null)
                    {
                        Mode = CombatMode.Retreat;
                    }
                    if (Equip())
                    {
                        if (Reload(deltaTime))
                        {
                            Attack(deltaTime);
                        }
                    }
                    break;
                case CombatMode.Retreat:
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void Move(float deltaTime)
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                    Engage(deltaTime);
                    break;
                case CombatMode.Defensive:
                case CombatMode.Retreat:
                    Retreat(deltaTime);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private Item GetWeapon()
        {
            rangedWeapons.Clear();
            meleeWeapons.Clear();
            adHocWeapons.Clear();
            Item weapon = null;
            _weaponComponent = null;
            foreach (var item in character.Inventory.Items)
            {
                if (item == null) { continue; }
                foreach (var component in item.Components)
                {
                    if (component is RangedWeapon rw)
                    {
                        if (rw.HasRequiredContainedItems(false))
                        {
                            rangedWeapons.Add(rw);
                        }
                    }
                    else if (component is MeleeWeapon mw)
                    {
                        if (mw.HasRequiredContainedItems(false))
                        {
                            meleeWeapons.Add(mw);
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
                                        if (component.HasRequiredContainedItems(false))
                                        {
                                            adHocWeapons.Add(item);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            var rangedWeapon = rangedWeapons.OrderByDescending(w => w.CombatPriority).FirstOrDefault();
            var meleeWeapon = meleeWeapons.OrderByDescending(w => w.CombatPriority).FirstOrDefault();
            if (rangedWeapon != null)
            {
                weapon = rangedWeapon.Item;
            }
            else if (meleeWeapon != null)
            {
                weapon = meleeWeapon.Item;
            }
            if (weapon == null)
            {
                weapon = adHocWeapons.GetRandom(Rand.RandSync.Server);
            }
            return weapon;
        }

        private void Unequip()
        {
            if (character.SelectedItems.Contains(Weapon))
            {
                if (!Weapon.AllowedSlots.Contains(InvSlotType.Any) || !character.Inventory.TryPutItem(Weapon, character, new List<InvSlotType>() { InvSlotType.Any }))
                {
                    Weapon.Drop(character);
                }
            }
        }

        private bool Equip()
        {
            if (!character.SelectedItems.Contains(Weapon))
            {
                var slots = Weapon.AllowedSlots.FindAll(s => s == InvSlotType.LeftHand || s == InvSlotType.RightHand || s == (InvSlotType.LeftHand | InvSlotType.RightHand));
                if (character.Inventory.TryPutItem(Weapon, character, slots))
                {
                    Weapon.Equip(character);
                }
                else
                {
                    Mode = CombatMode.Retreat;
                    return false;
                }
            }
            return true;
        }

        private void Retreat(float deltaTime)
        {
            followTargetObjective = null;
            if (retreatTarget == null || (retreatObjective != null && !retreatObjective.CanBeCompleted))
            {
                retreatTarget = findSafety.FindBestHull(new List<Hull>() { character.CurrentHull });
            }
            if (retreatTarget != null)
            {
                if (retreatObjective == null || retreatObjective.Target != retreatTarget)
                {
                    retreatObjective = new AIObjectiveGoTo(retreatTarget, character, objectiveManager, false, true, priorityModifier: PriorityModifier);
                }
                retreatObjective.TryComplete(deltaTime);
            }
        }

        private void Engage(float deltaTime)
        {
            retreatTarget = null;
            retreatObjective = null;
            if (followTargetObjective == null)
            {
                followTargetObjective = new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: true, getDivingGearIfNeeded: true, priorityModifier: PriorityModifier)
                {
                    AllowGoingOutside = true,
                    IgnoreIfTargetDead = true,
                    CheckVisibility = true
                };
            }
            if (WeaponComponent is RangedWeapon)
            {
                followTargetObjective.CloseEnough = 3;
            }
            else if (WeaponComponent is MeleeWeapon mw)
            {
                followTargetObjective.CloseEnough = ConvertUnits.ToSimUnits(mw.Range);
            }
            else if (WeaponComponent is RepairTool rt)
            {
                followTargetObjective.CloseEnough = ConvertUnits.ToSimUnits(rt.Range);
            }
            else
            {
                SteeringManager.Reset();
                Mode = CombatMode.Retreat;
            }
            followTargetObjective.TryComplete(deltaTime);
        }

        private bool Reload(float deltaTime)
        {
            if (WeaponComponent != null && WeaponComponent.requiredItems.ContainsKey(RelatedItem.RelationType.Contained))
            {
                var containedItems = Weapon.ContainedItems;
                foreach (RelatedItem requiredItem in WeaponComponent.requiredItems[RelatedItem.RelationType.Contained])
                {
                    Item containedItem = containedItems.FirstOrDefault(it => it.Condition > 0.0f && requiredItem.MatchesItem(it));
                    if (containedItem == null)
                    {
                        if (reloadWeaponObjective == null)
                        {
                            reloadWeaponObjective = new AIObjectiveContainItem(character, requiredItem.Identifiers, Weapon.GetComponent<ItemContainer>(), objectiveManager);
                        }
                    }
                }
            }
            if (reloadWeaponObjective != null)
            {
                if (reloadWeaponObjective.IsCompleted())
                {
                    reloadWeaponObjective = null;
                }
                else if (!reloadWeaponObjective.CanBeCompleted)
                {
                    SteeringManager.Reset();
                    Mode = CombatMode.Retreat;
                }
                else
                {
                    reloadWeaponObjective.TryComplete(deltaTime);
                }
                return false;
            }
            return true;
        }

        private IEnumerable<FarseerPhysics.Dynamics.Body> myBodies;
        private void Attack(float deltaTime)
        {
            float squaredDistance = Vector2.DistanceSquared(character.Position, Enemy.Position);
            character.CursorPosition = Enemy.Position;
            float engageDistance = 500;
            if (squaredDistance > engageDistance * engageDistance) { return; }
            bool canSeeTarget = character.CanSeeCharacter(Enemy);
            if (!canSeeTarget && character.CurrentHull != Enemy.CurrentHull) { return; }
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
                if (!isOperatingButtons && character.SelectedConstruction == null)
                {
                    character.SetInput(InputType.Aim, false, true);
                }
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
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.Position - character.Position) < MathHelper.PiOver4)
                {
                    if (myBodies == null)
                    {
                        myBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody);
                    }
                    var collisionCategories = Physics.CollisionCharacter | Physics.CollisionWall;
                    var pickedBody = Submarine.PickBody(character.SimPosition, Enemy.SimPosition, myBodies, collisionCategories);
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
                        if (target != null && target == Enemy)
                        {
                            character.SetInput(InputType.Shoot, false, true);
                            Weapon.Use(deltaTime, character);
                        }
                    }
                }
            }
        }

        public override bool IsCompleted()
        {
            bool completed = (Enemy != null && (Enemy.Removed || Enemy.IsDead)) || (useCoolDown && coolDownTimer <= 0);
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

        public override bool CanBeCompleted => !abandon && 
            (reloadWeaponObjective == null || reloadWeaponObjective.CanBeCompleted) && 
            (retreatObjective == null || retreatObjective.CanBeCompleted) &&
            (followTargetObjective == null || followTargetObjective.CanBeCompleted);

        public override float GetPriority() => (Enemy != null && (Enemy.Removed || Enemy.IsDead)) ? 0 : Math.Min(100 * PriorityModifier, 100);

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveCombat objective)) return false;
            return objective.Enemy == Enemy;
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
