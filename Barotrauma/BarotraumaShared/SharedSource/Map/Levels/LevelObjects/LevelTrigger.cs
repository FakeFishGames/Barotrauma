using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
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

        public enum TriggerForceMode
        {
            Force, //default, apply a force to the object over time
            Acceleration, //apply an acceleration to the object, ignoring it's mass
            Impulse, //apply an instant force, ignoring deltaTime
            LimitVelocity //clamp the velocity of the triggerer to some value
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
        private Vector2 unrotatedForce;
        private float forceFluctuationTimer, currentForceFluctuation = 1.0f;

        private HashSet<Entity> triggerers = new HashSet<Entity>();

        private TriggererType triggeredBy;
        
        private float randomTriggerInterval;
        private float randomTriggerProbability;
        private float randomTriggerTimer;

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

        public LevelTrigger ParentTrigger;

        public Dictionary<Entity, Vector2> TriggererPosition
        {
            get;
            private set;
        }

        private Vector2 worldPosition;        
        public Vector2 WorldPosition
        {
            get { return worldPosition; }
            set
            {
                worldPosition = value;
                physicsBody?.SetTransform(ConvertUnits.ToSimUnits(value), physicsBody.Rotation);
            }
        }

        public float Rotation
        {
            get { return physicsBody == null ? 0.0f : physicsBody.Rotation; }
            set
            {
                if (physicsBody == null) return;
                physicsBody.SetTransform(physicsBody.Position, value);
                CalculateDirectionalForce();
            }
        }

        public PhysicsBody PhysicsBody
        {
            get { return physicsBody; }
        }

        public float TriggerOthersDistance
        {
            get { return triggerOthersDistance; }
        }

        public IEnumerable<Entity> Triggerers
        {
            get { return triggerers.AsEnumerable(); }
        }

        public bool IsTriggered
        {
            get
            {
                return (triggerers.Count > 0 || triggeredTimer > 0.0f) &&
                    (ParentTrigger == null || ParentTrigger.IsTriggered);
            }
        }

        public Vector2 Force
        {
            get;
            private set;
        }

        /// <summary>
        /// does the force diminish by distance
        /// </summary>
        public bool ForceFalloff
        {
            get;
            private set;
        }
        
        public float ForceFluctuationInterval
        {
            get;
            private set;
        }
        public float ForceFluctuationStrength
        {
            get;
            private set;
        }

        private TriggerForceMode forceMode;
        public TriggerForceMode ForceMode
        {
            get { return forceMode; }
        }

        /// <summary>
        /// Stop applying forces to objects if they're moving faster than this
        /// </summary>
        public float ForceVelocityLimit
        {
            get;
            private set;
        }

        public float ColliderRadius
        {
            get;
            private set;
        }


        public bool UseNetworkSyncing
        {
            get;
            private set;
        }

        public bool NeedsNetworkSyncing
        {
            get;
            set;
        }

        public string InfectIdentifier
        {
            get;
            set;
        }

        public float InfectionChance
        {
            get;
            set;
        }
                
        public LevelTrigger(XElement element, Vector2 position, float rotation, float scale = 1.0f, string parentDebugName = "")
        {
            TriggererPosition = new Dictionary<Entity, Vector2>();

            worldPosition = position;
            if (element.Attributes("radius").Any() || element.Attributes("width").Any() || element.Attributes("height").Any())
            {
                physicsBody = new PhysicsBody(element, scale)
                {
                    CollisionCategories = Physics.CollisionLevel,
                    CollidesWith = Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionProjectile | Physics.CollisionWall
                };
                physicsBody.FarseerBody.OnCollision += PhysicsBody_OnCollision;
                physicsBody.FarseerBody.OnSeparation += PhysicsBody_OnSeparation;
                physicsBody.FarseerBody.SetIsSensor(true);
                physicsBody.FarseerBody.BodyType = BodyType.Static;
                physicsBody.FarseerBody.BodyType = BodyType.Kinematic;

                ColliderRadius = ConvertUnits.ToDisplayUnits(Math.Max(Math.Max(PhysicsBody.radius, PhysicsBody.width / 2.0f), PhysicsBody.height / 2.0f));

                physicsBody.SetTransform(ConvertUnits.ToSimUnits(position), rotation);
            }

            cameraShake = element.GetAttributeFloat("camerashake", 0.0f);
            
            InfectIdentifier = element.GetAttributeString("infectidentifier", null);
            InfectionChance = element.GetAttributeFloat("infectionchance", 0.05f);

            stayTriggeredDelay = element.GetAttributeFloat("staytriggereddelay", 0.0f);
            randomTriggerInterval = element.GetAttributeFloat("randomtriggerinterval", 0.0f);
            randomTriggerProbability = element.GetAttributeFloat("randomtriggerprobability", 0.0f);

            UseNetworkSyncing = element.GetAttributeBool("networksyncing", false);

            unrotatedForce = 
                element.Attribute("force") != null && element.Attribute("force").Value.Contains(',') ?
                element.GetAttributeVector2("force", Vector2.Zero) :
                new Vector2(element.GetAttributeFloat("force", 0.0f), 0.0f);

            ForceFluctuationInterval = element.GetAttributeFloat("forcefluctuationinterval", 0.01f);
            ForceFluctuationStrength = Math.Max(element.GetAttributeFloat("forcefluctuationstrength", 0.0f), 0.0f);
            ForceFalloff = element.GetAttributeBool("forcefalloff", true);

            ForceVelocityLimit = ConvertUnits.ToSimUnits(element.GetAttributeFloat("forcevelocitylimit", float.MaxValue));
            string forceModeStr = element.GetAttributeString("forcemode", "Force");
            if (!Enum.TryParse(forceModeStr, out forceMode))
            {
                DebugConsole.ThrowError("Error in LevelTrigger config: \"" + forceModeStr + "\" is not a valid force mode.");
            }
            CalculateDirectionalForce();

            string triggeredByStr = element.GetAttributeString("triggeredby", "Character");
            if (!Enum.TryParse(triggeredByStr, out triggeredBy))
            {
                DebugConsole.ThrowError("Error in LevelTrigger config: \"" + triggeredByStr + "\" is not a valid triggerer type.");
            }
            UpdateCollisionCategories();
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
                        statusEffects.Add(StatusEffect.Load(subElement, string.IsNullOrEmpty(parentDebugName) ? "LevelTrigger" : "LevelTrigger in "+ parentDebugName));
                        break;
                    case "attack":
                    case "damage":
                        var attack = new Attack(subElement, string.IsNullOrEmpty(parentDebugName) ? "LevelTrigger" : "LevelTrigger in " + parentDebugName);
                        var multipliedAfflictions = attack.GetMultipliedAfflictions((float)Timing.Step);
                        attack.Afflictions.Clear();
                        foreach (Affliction affliction in multipliedAfflictions)
                        {
                            attack.Afflictions.Add(affliction, null);
                        }
                        attacks.Add(attack);
                        break;
                }
            }

            forceFluctuationTimer = Rand.Range(0.0f, ForceFluctuationInterval);
            randomTriggerTimer = Rand.Range(0.0f, randomTriggerInterval);
        }

        private void UpdateCollisionCategories()
        {
            if (physicsBody == null) return;

            var collidesWith = Physics.CollisionNone;
            if (triggeredBy.HasFlag(TriggererType.Character) || triggeredBy.HasFlag(TriggererType.Creature)) collidesWith |= Physics.CollisionCharacter;
            if (triggeredBy.HasFlag(TriggererType.Item)) collidesWith |= Physics.CollisionItem | Physics.CollisionProjectile;
            if (triggeredBy.HasFlag(TriggererType.Submarine)) collidesWith |= Physics.CollisionWall;

            physicsBody.CollidesWith = collidesWith;
        }

        private void CalculateDirectionalForce()
        {
            var ca = (float)Math.Cos(-Rotation);
            var sa = (float)Math.Sin(-Rotation);

            Force = new Vector2(
                ca * unrotatedForce.X + sa * unrotatedForce.Y,
                -sa * unrotatedForce.X + ca * unrotatedForce.Y);      
        }

        private bool PhysicsBody_OnCollision(Fixture fixtureA, Fixture fixtureB, FarseerPhysics.Dynamics.Contacts.Contact contact)
        {
            Entity entity = GetEntity(fixtureB);
            if (entity == null) return false;

            if (entity is Character character)
            {
                if (character.CurrentHull != null) return false;
                if (character.IsHuman)
                {
                    if (!triggeredBy.HasFlag(TriggererType.Human)) return false;
                }
                else
                {
                    if (!triggeredBy.HasFlag(TriggererType.Creature)) return false;
                }
            }
            else if (entity is Item item)
            {
                if (item.CurrentHull != null) return false;
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
                TriggererPosition[entity] = entity.WorldPosition;
                triggerers.Add(entity);
            }
            return true;
        }

        private void PhysicsBody_OnSeparation(Fixture fixtureA, Fixture fixtureB, Contact contact)
        {
            Entity entity = GetEntity(fixtureB);
            if (entity == null) return;

            if (entity is Character character && 
                (!character.Enabled || character.Removed) &&
                triggerers.Contains(entity))
            {
                TriggererPosition.Remove(entity);
                triggerers.Remove(entity);
                return;
            }

            //check if there are contacts with any other fixture of the trigger
            //(the OnSeparation callback happens when two fixtures separate, 
            //e.g. if a body stops touching the circular fixture at the end of a capsule-shaped body)
            ContactEdge contactEdge = fixtureA.Body.ContactList;
            while (contactEdge != null)
            {
                if (contactEdge.Contact != null &&
                    contactEdge.Contact.Enabled &&
                    contactEdge.Contact.IsTouching)
                {
                    if (contactEdge.Contact.FixtureA != fixtureA && contactEdge.Contact.FixtureB != fixtureA)
                    {
                        var otherEntity = GetEntity(contactEdge.Contact.FixtureB == fixtureB ?
                            contactEdge.Contact.FixtureB :
                            contactEdge.Contact.FixtureA);
                        if (otherEntity == entity) { return; }
                    }
                }
                contactEdge = contactEdge.Next;
            }

            if (triggerers.Contains(entity))
            {
                TriggererPosition.Remove(entity);
                triggerers.Remove(entity);
            }
        }

        private Entity GetEntity(Fixture fixture)
        {
            if (fixture.Body == null || fixture.Body.UserData == null) return null;
            if (fixture.Body.UserData is Entity entity) return entity;
            if (fixture.Body.UserData is Limb limb) return limb.character;
            if (fixture.Body.UserData is SubmarineBody subBody) return subBody.Submarine;

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
            if (ParentTrigger != null && !ParentTrigger.IsTriggered) { return; }

            triggerers.RemoveWhere(t => t.Removed);

            if (physicsBody != null)
            {
                //failsafe to ensure triggerers get removed when they're far from the trigger
                float maxExtent = Math.Max(ConvertUnits.ToDisplayUnits(physicsBody.GetMaxExtent() * 5), 5000.0f);
                triggerers.RemoveWhere(t =>
                {
                    return Vector2.Distance(t.WorldPosition, WorldPosition) > maxExtent;
                });
            }            

            bool isNotClient = true;
#if CLIENT
            isNotClient = GameMain.Client == null;
#endif

            if (!UseNetworkSyncing || isNotClient)
            {
                if (ForceFluctuationStrength > 0.0f)
                {
                    //no need for force fluctuation (or network updates) if the trigger limits velocity and there are no triggerers
                    if (forceMode != TriggerForceMode.LimitVelocity || triggerers.Any())
                    {
                        forceFluctuationTimer += deltaTime;
                        if (forceFluctuationTimer > ForceFluctuationInterval)
                        {
                            NeedsNetworkSyncing = true;
                            currentForceFluctuation = Rand.Range(1.0f - ForceFluctuationStrength, 1.0f);
                            forceFluctuationTimer = 0.0f;
                        }
                    }
                }

                if (randomTriggerProbability > 0.0f)
                {
                    randomTriggerTimer += deltaTime;
                    if (randomTriggerTimer > randomTriggerInterval)
                    {
                        if (Rand.Range(0.0f, 1.0f) < randomTriggerProbability)
                        {
                            NeedsNetworkSyncing = true;
                            triggeredTimer = stayTriggeredDelay;
                        }
                        randomTriggerTimer = 0.0f;
                    }
                }
            }
            
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
                else if (triggerer is Submarine submarine)
                {
                    foreach (Attack attack in attacks)
                    {
                        float structureDamage = attack.GetStructureDamage(deltaTime);
                        if (structureDamage > 0.0f)
                        {
                            Explosion.RangedStructureDamage(worldPosition, attack.DamageRange, structureDamage, damageLevelWalls: false);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(InfectIdentifier))
                    {
                        submarine.AttemptBallastFloraInfection(InfectIdentifier, deltaTime, InfectionChance);
                    }
                }

                if (Force.LengthSquared() > 0.01f)
                {
                    if (triggerer is Character character)
                    {
                        ApplyForce(character.AnimController.Collider, deltaTime);
                        foreach (Limb limb in character.AnimController.Limbs)
                        {
                            if (limb.IsSevered) { continue; }
                            ApplyForce(limb.body, deltaTime);
                        }
                    }
                    else if (triggerer is Submarine submarine)
                    {
                        ApplyForce(submarine.SubBody.Body, deltaTime);
                    }
                }

                if (triggerer == Character.Controlled || triggerer == Character.Controlled?.Submarine)
                {
                    GameMain.GameScreen.Cam.Shake = Math.Max(GameMain.GameScreen.Cam.Shake, cameraShake);
                }
            }
        }

        private void ApplyForce(PhysicsBody body, float deltaTime)
        {
            float distFactor = 1.0f;
            if (ForceFalloff)
            {
                distFactor = 1.0f - ConvertUnits.ToDisplayUnits(Vector2.Distance(body.SimPosition, PhysicsBody.SimPosition)) / ColliderRadius;
                if (distFactor < 0.0f) return;
            }

            switch (ForceMode)
            {
                case TriggerForceMode.Force:
                    if (ForceVelocityLimit < 1000.0f)
                        body.ApplyForce(Force * currentForceFluctuation * distFactor, ForceVelocityLimit);
                    else
                        body.ApplyForce(Force * currentForceFluctuation * distFactor, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    break;
                case TriggerForceMode.Acceleration:
                    if (ForceVelocityLimit < 1000.0f)
                        body.ApplyForce(Force * body.Mass * currentForceFluctuation * distFactor, ForceVelocityLimit);
                    else
                        body.ApplyForce(Force * body.Mass * currentForceFluctuation * distFactor, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    break;
                case TriggerForceMode.Impulse:
                    if (ForceVelocityLimit < 1000.0f)
                        body.ApplyLinearImpulse(Force * currentForceFluctuation * distFactor, maxVelocity: ForceVelocityLimit);
                    else
                        body.ApplyLinearImpulse(Force * currentForceFluctuation * distFactor, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    break;
                case TriggerForceMode.LimitVelocity:
                    float maxVel = ForceVelocityLimit * currentForceFluctuation * distFactor;
                    if (body.LinearVelocity.LengthSquared() > maxVel * maxVel)
                    {
                        body.ApplyForce(
                            Vector2.Normalize(-body.LinearVelocity) * 
                            Force.Length() * body.Mass * currentForceFluctuation * distFactor, 
                            maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    }
                    break;
            }
        }

        public Vector2 GetWaterFlowVelocity(Vector2 viewPosition)
        {
            Vector2 baseVel = GetWaterFlowVelocity();
            if (baseVel.LengthSquared() < 0.1f) return Vector2.Zero;

            float triggerSize = ConvertUnits.ToDisplayUnits(Math.Max(Math.Max(PhysicsBody.radius, PhysicsBody.width / 2.0f), PhysicsBody.height / 2.0f));
            float dist = Vector2.Distance(viewPosition, WorldPosition);
            if (dist > triggerSize) return Vector2.Zero;

            return baseVel * (1.0f - dist / triggerSize);
        }

        public Vector2 GetWaterFlowVelocity()
        {
            if (Force == Vector2.Zero) return Vector2.Zero;
            
            Vector2 vel = Force;
            if (ForceMode == TriggerForceMode.Acceleration)
            {
                vel *= 1000.0f;
            }
            else if (ForceMode == TriggerForceMode.Impulse)
            {
                vel /= (float)Timing.Step;
            }
            return vel.ClampLength(ConvertUnits.ToDisplayUnits(ForceVelocityLimit)) * currentForceFluctuation;            
        }

        public void ServerWrite(IWriteMessage msg, Client c)
        {
            if (ForceFluctuationStrength > 0.0f)
            {
                msg.WriteRangedSingle(MathHelper.Clamp(currentForceFluctuation, 0.0f, 1.0f), 0.0f, 1.0f, 8);
            }
            if (stayTriggeredDelay > 0.0f)
            {
                msg.WriteRangedSingle(MathHelper.Clamp(triggeredTimer, 0.0f, stayTriggeredDelay), 0.0f, stayTriggeredDelay, 16);
            }
        }
    }
}
