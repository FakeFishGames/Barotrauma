using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelTrigger
    {
        private PhysicsBody physicsBody;

        /// <summary>
        /// Effects applied to entities that are inside the trigger
        /// </summary>
        private List<StatusEffect> statusEffects = new List<StatusEffect>();

        /// <summary>
        /// Attacks applied to entities that are inside the trigger
        /// </summary>
        private List<Attack> attacks = new List<Attack>();

        private List<Entity> triggerers = new List<Entity>();

        private float cameraShake;

        private Vector2 force;

        public Vector2 WorldPosition
        {
            get { return physicsBody.Position; }
            set { physicsBody.SetTransform(ConvertUnits.ToSimUnits(value), physicsBody.Rotation); }
        }

        public float Rotation
        {
            get { return physicsBody.Rotation; }
            set { physicsBody.SetTransform(physicsBody.Position, value); }
        }

        public PhysicsBody PhysicsBody
        {
            get { return physicsBody; }
        }

        public LevelTrigger(XElement element, Vector2 position, float rotation, float scale = 1.0f)
        {
            physicsBody = new PhysicsBody(element, scale);
            physicsBody.CollisionCategories = Physics.CollisionLevel;
            physicsBody.CollidesWith = Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionProjectile | Physics.CollisionWall;
            physicsBody.FarseerBody.OnCollision += PhysicsBody_OnCollision;
            physicsBody.FarseerBody.OnSeparation += PhysicsBody_OnSeparation;
            physicsBody.FarseerBody.IsSensor = true;
            physicsBody.FarseerBody.IsStatic = true;
            physicsBody.FarseerBody.IsKinematic = true;

            physicsBody.SetTransform(ConvertUnits.ToSimUnits(position), rotation);

            cameraShake = element.GetAttributeFloat("camerashake", 0.0f);

            force = element.GetAttributeVector2("force", Vector2.Zero);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        statusEffects.Add(StatusEffect.Load(subElement));
                        break;
                    case "attack":
                    case "damage":
                        attacks.Add(new Attack(subElement));
                        break;
                }
            }
        }

        private bool PhysicsBody_OnCollision(Fixture fixtureA, Fixture fixtureB, FarseerPhysics.Dynamics.Contacts.Contact contact)
        {
            Entity entity = GetEntity(fixtureB);
            if (entity == null) return false;

            if (!triggerers.Contains(entity))
            {
                triggerers.Add(entity);
            }
            return true;
        }

        private void PhysicsBody_OnSeparation(Fixture fixtureA, Fixture fixtureB)
        {
            Entity entity = GetEntity(fixtureB);
            if (entity == null) return;

            if (triggerers.Contains(entity))
            {
                triggerers.Remove(entity);
            }
        }

        private Entity GetEntity(Fixture fixture)
        {
            if (fixture.Body == null || fixture.Body.UserData == null) return null;

            var entity = fixture.Body.UserData as Entity;
            if (entity != null) return entity;
            
            var limb = fixture.Body.UserData as Limb;
            if (limb != null) return limb.character;

            return null;
        }

        public void Update(float deltaTime)
        {
            triggerers.RemoveAll(t => t.Removed);
            foreach (Entity triggerer in triggerers)
            {
                foreach (StatusEffect effect in statusEffects)
                {
                    if (triggerer is Character)
                    {
                        effect.Apply(effect.type, deltaTime, triggerer, (Character)triggerer);
                    }
                    else if (triggerer is Item)
                    {
                        effect.Apply(effect.type, deltaTime, triggerer, ((Item)triggerer).AllPropertyObjects);
                    }
                }

                IDamageable damageable = triggerer as IDamageable;
                if (damageable != null)
                {
                    foreach (Attack attack in attacks)
                    {
                        attack.DoDamage(null, damageable, WorldPosition, deltaTime, false);
                    }
                }

                if (force != Vector2.Zero)
                {
                    if (triggerer is Character)
                    {
                        ((Character)triggerer).AnimController.Collider.ApplyForce(force * deltaTime);
                    }
                    else if (triggerer is Submarine)
                    {
                        ((Submarine)triggerer).ApplyForce(force * deltaTime);
                    }
                }

                if (triggerer == Character.Controlled || triggerer == Character.Controlled?.Submarine)
                {
                    GameMain.GameScreen.Cam.Shake = Math.Max(GameMain.GameScreen.Cam.Shake, cameraShake);
                }
            }
        }
    }
}
