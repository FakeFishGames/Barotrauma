using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override string DebugTag => "combat";
        public override bool KeepDivingGearOn => true;

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
        private AIObjectiveContainItem reloadWeaponObjective;
        private Hull retreatTarget;
        private AIObjectiveGoTo retreatObjective;

        private float coolDownTimer;

        public AIObjectiveCombat(Character character, Character enemy) : base(character, "")
        {
            Enemy = enemy;
            coolDownTimer = CoolDown;
            HumanAIController.ObjectiveManager.GetObjective<AIObjectiveFindSafety>().Priority = 0;
        }

        protected override void Act(float deltaTime)
        {
            coolDownTimer -= deltaTime;
            if (Weapon != null && character.Inventory.Items.Contains(_weapon))
            {
                Weapon = null;
            }
            if (Weapon == null)
            {
                Weapon = GetWeapon();
            }
            if (Weapon == null)
            {
                case CombatMode.Defensive:
                    if (Weapon != null && character.Inventory.Items.Contains(_weapon))
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
                    else if (Equip(deltaTime))
                    {
                        if (Reload(deltaTime))
                        {
                            Attack(deltaTime);
                        }
                    }
                    // When defensive, try to retreat to safety. TODO: in offsensive mode, engage the target
                    Retreat(deltaTime);
                    break;
                case CombatMode.Retreat:
                    Retreat(deltaTime);
                    break;
                case CombatMode.Offensive:
                default:
                    throw new System.NotImplementedException();
            }
            else if (Equip(deltaTime))
            {
                if (Reload(deltaTime))
                {
                    Attack(deltaTime);
                }
            }
            if (!abandon)
            {
                Move(deltaTime);
            }
        }

        private Item GetWeapon()
        {
            _weaponComponent = null;
            var weapon = character.Inventory.FindItemByTag("weapon");
            if (weapon == null)
            {
                foreach (var item in character.Inventory.Items)
                {
                    if (item == null) { continue; }
                    foreach (var component in item.Components)
                    {
                        if (component is MeleeWeapon || component is RangedWeapon)
                        {
                            return item;
                        }
                        var effects = component.statusEffectLists;
                        if (effects != null)
                        {
                            foreach (var statusEffects in effects.Values)
                            {
                                foreach (var statusEffect in statusEffects)
                                {
                                    if (statusEffect.Afflictions.Any())
                                    {
                                        return item;
                                    }
                                }
                            }
                        }
                    }
                }
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

        private bool Equip(float deltaTime)
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
                    //couldn't equip the item, escape
                    Escape(deltaTime);
                    return false;
                }
            }
            return true;
        }

        private void Move(float deltaTime)
        {
            // Retreat to safety
            // TODO: aggressive behaviour, chasing?
            if (retreatTarget == null || (retreatObjective != null && !retreatObjective.CanBeCompleted))
            {
                retreatTarget = HumanAIController.ObjectiveManager.GetObjective<AIObjectiveFindSafety>().FindBestHull();
            }
            if (retreatTarget != null)
            {
                if (retreatObjective == null || retreatObjective.Target != retreatTarget)
                {
                    retreatObjective = new AIObjectiveGoTo(retreatTarget, character, false, true);
                }
                retreatObjective.TryComplete(deltaTime);
            }
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
                            reloadWeaponObjective = new AIObjectiveContainItem(character, requiredItem.Identifiers, Weapon.GetComponent<ItemContainer>());
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
                    Escape(deltaTime);
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
            character.CursorPosition = Enemy.Position;
            if (Weapon.RequireAimToUse)
            {
                character.SetInput(InputType.Aim, false, true);
            }
            if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                if (Vector2.DistanceSquared(character.Position, Enemy.Position) <= meleeWeapon.Range * meleeWeapon.Range)
                {
                    Weapon.Use(deltaTime, character);
                }
            }
            else
            {
                if (WeaponComponent is RepairTool repairTool)
                {
                    if (Vector2.DistanceSquared(character.Position, Enemy.Position) > repairTool.Range * repairTool.Range) { return; }
                }
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.Position - character.Position) < MathHelper.PiOver4)
                {
                    if (myBodies == null)
                    {
                        myBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody);
                    }
                    var pickedBody = Submarine.PickBody(character.SimPosition, Enemy.SimPosition, myBodies);
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
                            Weapon.Use(deltaTime, character);
                        }
                    }
                }
            }
        }

        private void Escape(float deltaTime)
        {
            abandon = true;
            SteeringManager.Reset();
            HumanAIController.ObjectiveManager.GetObjective<AIObjectiveFindSafety>().Priority = 100;
        }

        public override bool IsCompleted()
        {
            bool completed = Enemy == null || Enemy.Removed || Enemy.IsDead || coolDownTimer <= 0;
            if (completed)
            {
                if (Weapon != null)
                {
                    Unequip();
                }
            }
            return completed;
        }

        public override bool CanBeCompleted => !abandon && (reloadWeaponObjective == null || reloadWeaponObjective.CanBeCompleted) && (retreatObjective == null || retreatObjective.CanBeCompleted);
        public override float GetPriority(AIObjectiveManager objectiveManager) => Enemy == null || Enemy.Removed || Enemy.IsDead ? 0 : 100;

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
