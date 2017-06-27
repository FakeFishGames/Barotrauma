using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
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

        [HasDefaultValue(0.0f, false)]
        public float Range
        {
            get { return ConvertUnits.ToDisplayUnits(range); }
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        [HasDefaultValue(0.5f, false)]
        public float Reload
        {
            get { return reload; }
            set { reload = Math.Max(0.0f, value); }
        }

        public MeleeWeapon(Item item, XElement element)
            : base(item, element)
        {
            //throwForce = ToolBox.GetAttributeFloat(element, "throwforce", 1.0f);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "attack") continue;
                attack = new Attack(subElement);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || reloadTimer>0.0f) return false;
            if (!character.IsKeyDown(InputType.Aim) || hitting) return false;

            //don't allow hitting if the character is already hitting with another weapon
            for (int i = 0; i < 2; i++ )
            {
                if (character.SelectedItems[i] == null || character.SelectedItems[i] == Item) continue;

                var otherWeapon = character.SelectedItems[i].GetComponent<MeleeWeapon>();
                if (otherWeapon == null) continue;

                if (otherWeapon.hitting) return false;
            }

            SetUser(character);

            if (hitPos < MathHelper.Pi * 0.69f) return false;

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

            if (!picker.IsKeyDown(InputType.Aim) && !hitting) hitPos = 0.0f;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            AnimController ac = picker.AnimController;

            if (!hitting)
            {
                if (picker.IsKeyDown(InputType.Aim))
                {
                    hitPos = Math.Min(hitPos+deltaTime*5.0f, MathHelper.Pi*0.7f);

                    ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.6f, -0.1f), new Vector2(-0.3f, 0.2f), false, hitPos);
                }
                else
                {
                    ac.HoldItem(deltaTime, item, handlePos, new Vector2(hitPos, 0.0f), aimPos, false, holdAngle);
                }
            }
            else
            {
                //Vector2 diff = Vector2.Normalize(picker.CursorPosition - ac.RefLimb.Position);
                //diff.X = diff.X * ac.Dir;

                hitPos -= deltaTime*15.0f;

                //angl = -hitPos * 2.0f;
                //    System.Diagnostics.Debug.WriteLine("<1.0f "+hitPos);



                ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.6f, -0.1f), new Vector2(-0.3f, 0.2f), false, hitPos);
                //}
                //else
                //{
                //    System.Diagnostics.Debug.WriteLine(">1.0f " + hitPos);
                //    ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.5f, 0.2f), new Vector2(1.0f, 0.2f), false, 0.0f);
                //}

                if (hitPos < -MathHelper.PiOver4*1.2f)
                {
                    RestoreCollision();
                    hitting = false;
                }
            }
        }


        private void SetUser(Character character)
        {
            if (user == character) return;

            if (user != null)
            {
                foreach (Limb limb in user.AnimController.Limbs)
                {
                    try
                    {
                        item.body.FarseerBody.RestoreCollisionWith(limb.body.FarseerBody);
                    }

                    catch
                    {
                        continue;
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

            //foreach (Limb l in picker.AnimController.Limbs)
            //{
            //    item.body.FarseerBody.RestoreCollisionWith(l.body.FarseerBody);
            //}
        }


        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            Character target = null;

            Limb limb = f2.Body.UserData as Limb;
            if (limb != null)
            {
                if (limb.character == picker) return false;
                target = limb.character;
            }
            else
            {
                return false;
            }

            if (target == null)
            {
                target = f2.Body.UserData as Character;
            }

            if (target == null) return false;

            if (attack != null) attack.DoDamage(user, target, item.WorldPosition, 1.0f);

            RestoreCollision();
            hitting = false;

            if (GameMain.Client != null) return true;

            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { Networking.NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, target.ID });

                string logStr = picker?.Name + " used " + item.Name;
                if (item.ContainedItems != null && item.ContainedItems.Length > 0)
                {
                    logStr += "(" + string.Join(", ", item.ContainedItems.Select(i => i?.Name)) + ")";
                }
                logStr += " on " + target + ".";
                Networking.GameServer.Log(logStr, Networking.ServerLog.MessageType.Attack);
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f, limb.character);

            return true;
        }
    }
}
