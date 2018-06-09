using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelTrigger
    {
        [Flags]
        enum TriggererType
        {
            None = 0,
            Human = 1,
            Creature = 2,
            Character = Human | Creature,
            Submarine = 4,
            Item = 8,
            OtherTrigger = 16
        }

        public Action<LevelTrigger, Entity> OnTriggered;

        private PhysicsBody physicsBody;

        /// <summary>
        /// Effects applied to entities that are inside the trigger
        /// </summary>
        private List<StatusEffect> statusEffects = new List<StatusEffect>();

        /// <summary>
        /// Attacks applied to entities that are inside the trigger
        /// </summary>
        private List<Attack> attacks = new List<Attack>();

        private float cameraShake;
        private Vector2 force;

        private HashSet<Entity> triggerers = new HashSet<Entity>();

        private TriggererType triggeredBy;

        private float triggeredTimer;

        //how far away this trigger can activate other triggers from
        private float triggerOthersDistance;

        private HashSet<string> tags = new HashSet<string>();

        //other triggers have to have at least one of these tags to trigger this one
        private HashSet<string> allowedOtherTriggerTags = new HashSet<string>();

        /// <summary>
        /// How long the trigger stays in the triggered state after triggerers have left
        /// </summary>
        private float stayTriggeredDelay;

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

        public float TriggerOthersDistance
        {
            get { return triggerOthersDistance; }
        }
        
        public bool IsTriggered
        {
            get { return triggerers.Count > 0 || triggeredTimer > 0.0f; }
        }

        public LevelTrigger(XElement element, Vector2 position, float rotation, float scale = 1.0f)
        {
            physicsBody = new PhysicsBody(element, scale)
            {
                CollisionCategories = Physics.CollisionLevel,
                CollidesWith = Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionProjectile | Physics.CollisionWall
            };
            physicsBody.FarseerBody.OnCollision += PhysicsBody_OnCollision;
            physicsBody.FarseerBody.OnSeparation += PhysicsBody_OnSeparation;
            physicsBody.FarseerBody.IsSensor = true;
            physicsBody.FarseerBody.IsStatic = true;
            physicsBody.FarseerBody.IsKinematic = true;

            physicsBody.SetTransform(ConvertUnits.ToSimUnits(position), rotation);

            cameraShake = element.GetAttributeFloat("camerashake", 0.0f);

            stayTriggeredDelay = element.GetAttributeFloat("staytriggereddelay", 0.0f);

            force = element.GetAttributeVector2("force", Vector2.Zero);
            
            string triggeredByStr = element.GetAttributeString("triggeredby", "Character");
            if (!Enum.TryParse(triggeredByStr, out triggeredBy))
            {
                DebugConsole.ThrowError("Error in LevelTrigger config: \"" + triggeredByStr + "\" is not a valid triggerer type.");
            }

            triggerOthersDistance = element.GetAttributeFloat("triggerothersdistance", 0.0f);

            var tagsArray = element.GetAttributeStringArray("tags", new string[0]);
            foreach (string tag in tagsArray)
            {
                tags.Add(tag.ToLower());
            }

            if (triggeredBy.HasFlag(TriggererType.OtherTrigger))
            {
                var otherTagsArray = element.GetAttributeStringArray("allowedothertriggertags", new string[0]);
                foreach (string tag in otherTagsArray)
                {
                    allowedOtherTriggerTags.Add(tag.ToLower());
                }
            }

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

            if (entity is Character character)
            {
                if (character.ConfigPath == Character.HumanConfigFile)
                {
                    if (!triggeredBy.HasFlag(TriggererType.Human)) return false;
                }
                else
                {
                    if (!triggeredBy.HasFlag(TriggererType.Creature)) return false;
                }
            }
            else if (entity is Item)
            {
                if (!triggeredBy.HasFlag(TriggererType.Item)) return false;
            }
            else if (entity is Submarine)
            {
                if (!triggeredBy.HasFlag(TriggererType.Submarine)) return false;
            }

            if (!triggerers.Contains(entity))
            {
                if (!IsTriggered)
                {
                    OnTriggered?.Invoke(this, entity);
                }
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
            if (fixture.Body.UserData is Entity entity) return entity;
            if (fixture.Body.UserData is Limb limb) return limb.character;

            return null;
        }

        /// <summary>
        /// Another trigger was triggered, check if this one should react to it
        /// </summary>
        public void OtherTriggered(LevelObject levelObject, LevelTrigger otherTrigger)
        {
            if (!triggeredBy.HasFlag(TriggererType.OtherTrigger) || stayTriggeredDelay <= 0.0f) return;

            //check if the other trigger has appropriate tags
            if (allowedOtherTriggerTags.Count > 0)
            {
                if (!allowedOtherTriggerTags.Any(t => otherTrigger.tags.Contains(t))) return;
            }

            if (Vector2.DistanceSquared(WorldPosition, otherTrigger.WorldPosition) <= otherTrigger.triggerOthersDistance * otherTrigger.triggerOthersDistance)
            {
                bool wasAlreadyTriggered = IsTriggered;
                triggeredTimer = stayTriggeredDelay;
                if (!wasAlreadyTriggered)
                {
                    OnTriggered?.Invoke(this, null);
                }
            }
        }

        public void Update(float deltaTime)
        {
            triggerers.RemoveWhere(t => t.Removed);

            if (stayTriggeredDelay > 0.0f)
            {
                if (triggerers.Count == 0)
                {
                    triggeredTimer -= deltaTime;
                }
                else
                {
                    triggeredTimer = stayTriggeredDelay;
                }
            }

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

                if (triggerer is IDamageable damageable)
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
