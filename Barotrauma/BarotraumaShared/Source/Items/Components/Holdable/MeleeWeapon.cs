using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class MeleeWeapon : Holdable
    {
        private float hitPos;

        private bool hitting;

        private Attack attack;

        private float range;

        private Character user;

        private float reload;

        private float reloadTimer;

        private HashSet<Entity> hitTargets = new HashSet<Entity>();

        public Character User
        {
            get { return user; }
        }

        [Serialize(0.0f, false)]
        public float Range
        {
            get { return ConvertUnits.ToDisplayUnits(range); }
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        [Serialize(0.5f, false)]
        public float Reload
        {
            get { return reload; }
            set { reload = Math.Max(0.0f, value); }
        }

        [Serialize(false, false)]
        public bool AllowHitMultiple
        {
            get;
            set;
        }

        public MeleeWeapon(Item item, XElement element)
            : base(item, element)
        {
            //throwForce = ToolBox.GetAttributeFloat(element, "throwforce", 1.0f);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "attack") continue;
                attack = new Attack(subElement, item.Name + ", MeleeWeapon");
            }
            item.IsShootable = true;
            // TODO: should define this in xml if we have melee weapons that don't require aim to use
            item.RequireAimToUse = true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || reloadTimer > 0.0f) return false;
            if (Item.RequireAimToUse && !character.IsKeyDown(InputType.Aim) || hitting) return false;

            //don't allow hitting if the character is already hitting with another weapon
            for (int i = 0; i < 2; i++ )
            {
                if (character.SelectedItems[i] == null || character.SelectedItems[i] == Item) continue;

                var otherWeapon = character.SelectedItems[i].GetComponent<MeleeWeapon>();
                if (otherWeapon == null) continue;

                if (otherWeapon.hitting) return false;
            }

            SetUser(character);

            if (hitPos < MathHelper.PiOver4) return false;

            reloadTimer = reload;

            item.body.FarseerBody.CollisionCategories = Physics.CollisionProjectile;
            item.body.FarseerBody.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall;
            item.body.FarseerBody.OnCollision += OnCollision;

            foreach (Limb l in character.AnimController.Limbs)
            {
                //item.body.FarseerBody.IgnoreCollisionWith(l.body.FarseerBody);

                if (character.AnimController.InWater) continue;
                if (l.type == LimbType.LeftFoot || l.type == LimbType.LeftThigh || l.type == LimbType.LeftLeg) continue;

                if (l.type == LimbType.Head || l.type == LimbType.Torso)
                {
                    l.body.ApplyLinearImpulse(new Vector2(character.AnimController.Dir * 7.0f, -4.0f));                   
                }
                else
                {
                    l.body.ApplyLinearImpulse(new Vector2(character.AnimController.Dir * 5.0f, -2.0f));
                }                
            }
            
            hitting = true;
            hitTargets.Clear();

            IsActive = true;
            return false;
        }
        
        public override void Drop(Character dropper)
        {
            base.Drop(dropper);

            hitting = false;
            hitPos = 0.0f;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (!item.body.Enabled) return;
            if (!picker.HasSelectedItem(item)) IsActive = false;

            reloadTimer -= deltaTime;
            if (reloadTimer < 0) { reloadTimer = 0; }

            if (!picker.IsKeyDown(InputType.Aim) && !hitting) hitPos = 0.0f;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip();

            AnimController ac = picker.AnimController;

            //TODO: refactor the hitting logic (get rid of the magic numbers, make it possible to use different kinds of animations for different items)
            if (!hitting)
            {
                bool aim = picker.IsKeyDown(InputType.Aim) && reloadTimer <= 0 && (picker.SelectedConstruction == null || picker.SelectedConstruction.GetComponent<Ladder>() != null);
                if (aim)
                {
                    hitPos = MathUtils.WrapAnglePi(Math.Min(hitPos + deltaTime * 5f, MathHelper.PiOver4));
                    ac.HoldItem(deltaTime, item, handlePos, aimPos, Vector2.Zero, false, hitPos, holdAngle + hitPos);
                }
                else
                {
                    hitPos = 0;
                    ac.HoldItem(deltaTime, item, handlePos, holdPos, Vector2.Zero, false, holdAngle);
                }
            }
            else
            {
                hitPos = MathUtils.WrapAnglePi(hitPos - deltaTime * 15f);
                ac.HoldItem(deltaTime, item, handlePos, new Vector2(2, 0), Vector2.Zero, false, hitPos, holdAngle + hitPos); // aimPos not used -> zero (new Vector2(-0.3f, 0.2f)), holdPos new Vector2(0.6f, -0.1f)
                if (hitPos < -MathHelper.PiOver4 * 1.2f)
                {
                    RestoreCollision();
                    hitting = false;
                    hitTargets.Clear();
                    hitPos = 0;
                }
            }
        }

        private void SetUser(Character character)
        {
            if (user == character) return;
            if (user != null && user.Removed) user = null;

            if (user != null)
            {
                foreach (Limb limb in user.AnimController.Limbs)
                {
                    if (limb.body.FarseerBody != null)
                    {
                        if (GameMain.World.BodyList.Contains(limb.body.FarseerBody))
                        {
                            item.body.FarseerBody.RestoreCollisionWith(limb.body.FarseerBody);
                        }
                    }
                }
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                item.body.FarseerBody.IgnoreCollisionWith(limb.body.FarseerBody);
            }

            user = character;
        }

        private void RestoreCollision()
        {
            item.body.FarseerBody.OnCollision -= OnCollision;

            item.body.CollisionCategories = Physics.CollisionItem;
            item.body.CollidesWith = Physics.CollisionWall;
        }


        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (user == null || user.Removed)
            {
                RestoreCollision();
                hitting = false;
                user = null;
            }

            Character targetCharacter = null;
            Limb targetLimb = null;
            Structure targetStructure = null;

            attack?.SetUser(user);

            if (f2.Body.UserData is Limb)
            {
                targetLimb = (Limb)f2.Body.UserData;
                if (targetLimb.IsSevered || targetLimb.character == null) return false;
                targetCharacter = targetLimb.character;
                if (targetCharacter == picker) return false;
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetCharacter)) return false;
                }
                else
                {
                    if (hitTargets.Any(t => t is Character)) return false;
                }
                hitTargets.Add(targetCharacter);
            }
            else if (f2.Body.UserData is Character)
            {
                targetCharacter = (Character)f2.Body.UserData;
                if (targetCharacter == picker) return false;
                targetLimb = targetCharacter.AnimController.GetLimb(LimbType.Torso); //Otherwise armor can be bypassed in strange ways
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetCharacter)) return false;
                }
                else
                {
                    if (hitTargets.Any(t => t is Character)) return false;
                }
                hitTargets.Add(targetCharacter);
            }
            else if (f2.Body.UserData is Structure)
            {
                targetStructure = (Structure)f2.Body.UserData;
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetStructure)) return true;
                }
                else
                {
                    if (hitTargets.Any(t => t is Structure)) return true;
                }
                hitTargets.Add(targetStructure);
            }
            else
            {
                return false;
            }

            if (attack != null)
            {
                if (targetLimb != null)
                {
                    targetLimb.character.LastDamageSource = item;
                    attack.DoDamageToLimb(user, targetLimb, item.WorldPosition, 1.0f);
                }
                else if (targetCharacter != null)
                {
                    targetCharacter.LastDamageSource = item;
                    attack.DoDamage(user, targetCharacter, item.WorldPosition, 1.0f);
                }
                else if (targetStructure != null)
                {
                    attack.DoDamage(user, targetStructure, item.WorldPosition, 1.0f);
                }
                else
                {
                    return false;
                }
            }
            
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) return true;

#if SERVER
            if (GameMain.Server != null && targetCharacter != null) //TODO: Log structure hits
            {

                GameMain.Server.CreateEntityEvent(item, new object[] 
                {
                    Networking.NetEntityEvent.Type.ApplyStatusEffect,                    
                    ActionType.OnUse,
                    null, //itemcomponent
                    targetCharacter.ID, targetLimb
                });

                string logStr = picker?.LogName + " used " + item.Name;
                if (item.ContainedItems != null && item.ContainedItems.Any())
                {
                    logStr += " (" + string.Join(", ", item.ContainedItems.Select(i => i?.Name)) + ")";
                }
                logStr += " on " + targetCharacter.LogName + ".";
                Networking.GameServer.Log(logStr, Networking.ServerLog.MessageType.Attack);
            }
#endif

            if (targetCharacter != null) //TODO: Allow OnUse to happen on structures too maybe??
            {
                ApplyStatusEffects(ActionType.OnUse, 1.0f, targetCharacter, targetLimb, user: user);
            }

            if (DeleteOnUse)
            {
                Entity.Spawner.AddToRemoveQueue(item);
            }

            return true;
        }
    }
}
