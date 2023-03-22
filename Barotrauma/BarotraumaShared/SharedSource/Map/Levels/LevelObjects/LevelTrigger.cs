using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class LevelTrigger
    {
        [Flags]
        public enum TriggererType
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

        /// <summary>
        /// Effects applied to entities that are inside the trigger
        /// </summary>
        private readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
        public IEnumerable<StatusEffect> StatusEffects
        {
            get { return statusEffects; }
        }

        /// <summary>
        /// Attacks applied to entities that are inside the trigger
        /// </summary>
        private readonly List<Attack> attacks = new List<Attack>();

        private readonly float cameraShake;
        private Vector2 unrotatedForce;
        private float forceFluctuationTimer, currentForceFluctuation = 1.0f;

        private readonly HashSet<Entity> triggerers = new HashSet<Entity>();

        private readonly TriggererType triggeredBy;
        
        private readonly float randomTriggerInterval;
        private readonly float randomTriggerProbability;
        private float randomTriggerTimer;

        private float triggeredTimer;
        private readonly HashSet<string> tags = new HashSet<string>();

        //other triggers have to have at least one of these tags to trigger this one
        private readonly HashSet<string> allowedOtherTriggerTags = new HashSet<string>();

        /// <summary>
        /// How long the trigger stays in the triggered state after triggerers have left
        /// </summary>
        private readonly float stayTriggeredDelay;

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
                PhysicsBody?.SetTransform(ConvertUnits.ToSimUnits(value), PhysicsBody.Rotation);
            }
        }

        public float Rotation
        {
            get { return PhysicsBody == null ? 0.0f : PhysicsBody.Rotation; }
            set
            {
                if (PhysicsBody == null) return;
                PhysicsBody.SetTransform(PhysicsBody.Position, value);
                CalculateDirectionalForce();
            }
        }

        public PhysicsBody PhysicsBody { get; private set; }

        public float TriggerOthersDistance { get; private set; }

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
        public float GlobalForceDecreaseInterval
        {
            get;
            private set;
        }

        private readonly TriggerForceMode forceMode;
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

        public Identifier InfectIdentifier
        {
            get;
            set;
        }

        public float InfectionChance
        {
            get;
            set;
        }

        private bool triggeredOnce;
        private readonly bool triggerOnce;
                
        public LevelTrigger(ContentXElement element, Vector2 position, float rotation, float scale = 1.0f, string parentDebugName = "")
        {
            TriggererPosition = new Dictionary<Entity, Vector2>();

            worldPosition = position;
            if (element.Attributes("radius").Any() || element.Attributes("width").Any() || element.Attributes("height").Any())
            {
                PhysicsBody = new PhysicsBody(element, scale)
                {
                    CollisionCategories = Physics.CollisionLevel,
                    CollidesWith = Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionProjectile | Physics.CollisionWall,
                };
                PhysicsBody.FarseerBody.OnCollision += PhysicsBody_OnCollision;
                PhysicsBody.FarseerBody.OnSeparation += PhysicsBody_OnSeparation;
                PhysicsBody.FarseerBody.SetIsSensor(element.GetAttributeBool("sensor", true));
                PhysicsBody.FarseerBody.BodyType = BodyType.Static;

                ColliderRadius = ConvertUnits.ToDisplayUnits(Math.Max(Math.Max(PhysicsBody.Radius, PhysicsBody.Width / 2.0f), PhysicsBody.Height / 2.0f));

                PhysicsBody.SetTransform(ConvertUnits.ToSimUnits(position), rotation);
            }

            cameraShake = element.GetAttributeFloat("camerashake", 0.0f);
            
            InfectIdentifier = element.GetAttributeIdentifier("infectidentifier", Identifier.Empty);
            InfectionChance = element.GetAttributeFloat("infectionchance", 0.05f);

            triggerOnce = element.GetAttributeBool("triggeronce", false);

            stayTriggeredDelay = element.GetAttributeFloat("staytriggereddelay", 0.0f);
            randomTriggerInterval = element.GetAttributeFloat("randomtriggerinterval", 0.0f);
            randomTriggerProbability = element.GetAttributeFloat("randomtriggerprobability", 0.0f);

            UseNetworkSyncing = element.GetAttributeBool("networksyncing", false);

            unrotatedForce = 
                element.GetAttribute("force") != null && element.GetAttribute("force").Value.Contains(',') ?
                element.GetAttributeVector2("force", Vector2.Zero) :
                new Vector2(element.GetAttributeFloat("force", 0.0f), 0.0f);

            ForceFluctuationInterval = element.GetAttributeFloat("forcefluctuationinterval", 0.01f);
            ForceFluctuationStrength = Math.Max(element.GetAttributeFloat("forcefluctuationstrength", 0.0f), 0.0f);
            ForceFalloff = element.GetAttributeBool("forcefalloff", true);
            GlobalForceDecreaseInterval = element.GetAttributeFloat("globalforcedecreaseinterval", 0.0f);

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
            if (PhysicsBody != null)
            {
                PhysicsBody.CollidesWith = GetCollisionCategories(triggeredBy);
            }
            
            TriggerOthersDistance = element.GetAttributeFloat("triggerothersdistance", 0.0f);

            var tagsArray = element.GetAttributeStringArray("tags", Array.Empty<string>());
            foreach (string tag in tagsArray)
            {
                tags.Add(tag.ToLowerInvariant());
            }

            if (triggeredBy.HasFlag(TriggererType.OtherTrigger))
            {
                var otherTagsArray = element.GetAttributeStringArray("allowedothertriggertags", Array.Empty<string>());
                foreach (string tag in otherTagsArray)
                {
                    allowedOtherTriggerTags.Add(tag.ToLowerInvariant());
                }
            }

            string debugName = string.IsNullOrEmpty(parentDebugName) ? "LevelTrigger" : $"LevelTrigger in {parentDebugName}";
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        LoadStatusEffect(statusEffects, subElement, debugName);
                        break;
                    case "attack":
                    case "damage":
                        LoadAttack(subElement, debugName, triggerOnce, attacks);
                        break;
                }
            }

            forceFluctuationTimer = Rand.Range(0.0f, ForceFluctuationInterval);
            randomTriggerTimer = Rand.Range(0.0f, randomTriggerInterval);
        }

        public static Category GetCollisionCategories(TriggererType triggeredBy)
        {
            var collidesWith = Physics.CollisionNone;
            if (triggeredBy.HasFlag(TriggererType.Human) || triggeredBy.HasFlag(TriggererType.Creature)) { collidesWith |= Physics.CollisionCharacter; }
            if (triggeredBy.HasFlag(TriggererType.Item)) { collidesWith |= Physics.CollisionItem | Physics.CollisionProjectile; }
            if (triggeredBy.HasFlag(TriggererType.Submarine)) { collidesWith |= Physics.CollisionWall; }
            return collidesWith;
        }

        private void CalculateDirectionalForce()
        {
            var ca = (float)Math.Cos(-Rotation);
            var sa = (float)Math.Sin(-Rotation);

            Force = new Vector2(
                ca * unrotatedForce.X + sa * unrotatedForce.Y,
                -sa * unrotatedForce.X + ca * unrotatedForce.Y);      
        }

        public static void LoadStatusEffect(List<StatusEffect> statusEffects, ContentXElement element, string parentDebugName)
        {
            statusEffects.Add(StatusEffect.Load(element, parentDebugName));
        }

        public static void LoadAttack(ContentXElement element, string parentDebugName, bool triggerOnce, List<Attack> attacks)
        {
            var attack = new Attack(element, parentDebugName);
            if (!triggerOnce)
            {
                var multipliedAfflictions = attack.GetMultipliedAfflictions((float)Timing.Step);
                attack.Afflictions.Clear();
                foreach (Affliction affliction in multipliedAfflictions)
                {
                    attack.Afflictions.Add(affliction, null);
                }
            }
            attacks.Add(attack);
        }

        private bool PhysicsBody_OnCollision(Fixture fixtureA, Fixture fixtureB, Contact contact)
        {
            Entity entity = GetEntity(fixtureB);
            if (entity == null) { return false; }
            if (!IsTriggeredByEntity(entity, triggeredBy, mustBeOutside: true)) { return false; }
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

        public static bool IsTriggeredByEntity(Entity entity, TriggererType triggeredBy, bool mustBeOutside = false, (bool mustBe, Submarine sub) mustBeOnSpecificSub = default)
        {
            if (entity is Character character)
            {
                if (mustBeOutside && character.CurrentHull != null) { return false; }
                if (mustBeOnSpecificSub.mustBe && character.Submarine != mustBeOnSpecificSub.sub) { return false; }
                if (character.IsHuman)
                {
                    if (!triggeredBy.HasFlag(TriggererType.Human)) { return false; }
                }
                else
                {
                    if (!triggeredBy.HasFlag(TriggererType.Creature)) { return false; }
                }
            }
            else if (entity is Item item)
            {
                if (mustBeOutside && item.CurrentHull != null) { return false; }
                if (mustBeOnSpecificSub.mustBe && item.Submarine != mustBeOnSpecificSub.sub) { return false; }
                if (!triggeredBy.HasFlag(TriggererType.Item)) { return false; }
            }
            else if (entity is Submarine)
            {
                if (!triggeredBy.HasFlag(TriggererType.Submarine)) { return false; }
            }
            return true;
        }

        private void PhysicsBody_OnSeparation(Fixture fixtureA, Fixture fixtureB, Contact contact)
        {
            Entity entity = GetEntity(fixtureB);
            if (entity == null) { return; }

            if (entity is Character character && 
                (!character.Enabled || character.Removed) &&
                triggerers.Contains(entity))
            {
                TriggererPosition.Remove(entity);
                triggerers.Remove(entity);
                return;
            }

            if (CheckContactsForOtherFixtures(PhysicsBody, fixtureB, entity)) { return; }

            if (triggerers.Contains(entity))
            {
                TriggererPosition.Remove(entity);
                triggerers.Remove(entity);
            }
        }

        public static bool CheckContactsForOtherFixtures(PhysicsBody triggerBody, Fixture otherFixture, Entity separatingEntity)
        {
            //check if there are contacts with any other fixture of the trigger
            //(the OnSeparation callback happens when two fixtures separate, 
            //e.g. if a body stops touching the circular fixture at the end of a capsule-shaped body)
            foreach (Fixture fixture in triggerBody.FarseerBody.FixtureList)
            {
                ContactEdge contactEdge = fixture.Body.ContactList;
                while (contactEdge != null)
                {
                    if (contactEdge.Contact != null &&
                        contactEdge.Contact.Enabled &&
                        contactEdge.Contact.IsTouching)
                    {
                        if (contactEdge.Contact.FixtureA != fixture && contactEdge.Contact.FixtureB != fixture)
                        {
                            var otherEntity = GetEntity(contactEdge.Contact.FixtureB == otherFixture ?
                                contactEdge.Contact.FixtureB :
                                contactEdge.Contact.FixtureA);
                            if (otherEntity == separatingEntity) { return true; }
                        }
                    }
                    contactEdge = contactEdge.Next;
                }
            }
            return false;
        }

        /// <summary>
        /// Are there any active contacts between the physics body and the target entity
        /// </summary>
        public static bool CheckContactsForEntity(PhysicsBody triggerBody, Entity targetEntity)
        {
            foreach (Fixture fixture in triggerBody.FarseerBody.FixtureList)
            {
                ContactEdge contactEdge = fixture.Body.ContactList;
                while (contactEdge != null)
                {
                    if (contactEdge.Contact != null &&
                        contactEdge.Contact.Enabled &&
                        contactEdge.Contact.IsTouching)
                    {
                        if ((contactEdge.Contact.FixtureA.Body == triggerBody.FarseerBody && GetEntity(contactEdge.Contact.FixtureB) == targetEntity) ||
                            (contactEdge.Contact.FixtureB.Body == triggerBody.FarseerBody && GetEntity(contactEdge.Contact.FixtureA) == targetEntity))
                        { 
                            return true; 
                        }                        
                    }
                    contactEdge = contactEdge.Next;
                }
            }
            return false;
        }

        public static Entity GetEntity(Fixture fixture)
        {
            if (fixture.Body == null || fixture.Body.UserData == null) { return null; }
            if (fixture.Body.UserData is Entity entity) { return entity; }
            if (fixture.Body.UserData is Limb limb) { return limb.character; }
            if (fixture.Body.UserData is SubmarineBody subBody) { return subBody.Submarine; }
            return null;
        }

        /// <summary>
        /// Another trigger was triggered, check if this one should react to it
        /// </summary>
        public void OtherTriggered(LevelObject levelObject, LevelTrigger otherTrigger)
        {
            if (!triggeredBy.HasFlag(TriggererType.OtherTrigger) || stayTriggeredDelay <= 0.0f) { return; }

            //check if the other trigger has appropriate tags
            if (allowedOtherTriggerTags.Count > 0)
            {
                if (!allowedOtherTriggerTags.Any(t => otherTrigger.tags.Contains(t))) { return; }
            }

            if (Vector2.DistanceSquared(WorldPosition, otherTrigger.WorldPosition) <= otherTrigger.TriggerOthersDistance * otherTrigger.TriggerOthersDistance)
            {
                bool wasAlreadyTriggered = IsTriggered;
                triggeredTimer = stayTriggeredDelay;
                if (!wasAlreadyTriggered)
                {
                    OnTriggered?.Invoke(this, null);
                }
            }
        }

        private readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        public void Update(float deltaTime)
        {
            if (ParentTrigger != null && !ParentTrigger.IsTriggered) { return; }


            bool isNotClient = true;
#if CLIENT
            isNotClient = GameMain.Client == null;
#endif

            if (!UseNetworkSyncing || isNotClient)
            {
                if (GlobalForceDecreaseInterval > 0.0f && Level.Loaded?.LevelObjectManager != null && 
                    Level.Loaded.LevelObjectManager.GlobalForceDecreaseTimer % (GlobalForceDecreaseInterval * 2) < GlobalForceDecreaseInterval)
                {
                    NeedsNetworkSyncing |= currentForceFluctuation > 0.0f;
                    currentForceFluctuation = 0.0f;                    
                }
                else if (ForceFluctuationStrength > 0.0f)
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

            RemoveInActiveTriggerers(PhysicsBody, triggerers);

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

            if (triggerOnce && triggeredOnce)
            {
                return;
            }

            foreach (Entity triggerer in triggerers)
            {
                if (triggerer.Removed) { continue; }

                ApplyStatusEffects(statusEffects, worldPosition, triggerer, deltaTime, targets);

                if (triggerer is IDamageable damageable)
                {
                    ApplyAttacks(attacks, damageable, worldPosition, deltaTime);
                }
                else if (triggerer is Submarine submarine)
                {
                    ApplyAttacks(attacks, worldPosition, deltaTime);
                    if (!InfectIdentifier.IsEmpty)
                    {
                        submarine.AttemptBallastFloraInfection(InfectIdentifier, deltaTime, InfectionChance);
                    }
                }

                if (Force.LengthSquared() > 0.01f)
                {
                    if (triggerer is Character character)
                    {
                        ApplyForce(character.AnimController.Collider);
                        foreach (Limb limb in character.AnimController.Limbs)
                        {
                            if (limb.IsSevered) { continue; }
                            ApplyForce(limb.body);
                        }
                    }
                    else if (triggerer is Submarine submarine)
                    {
                        ApplyForce(submarine.SubBody.Body);
                    }
                }

                if (triggerer == Character.Controlled || triggerer == Character.Controlled?.Submarine)
                {
                    GameMain.GameScreen.Cam.Shake = Math.Max(GameMain.GameScreen.Cam.Shake, cameraShake);
                }
            }

            if (triggerOnce && triggerers.Count > 0)
            {
                PhysicsBody.Enabled = false;
                triggeredOnce = true;                
            }
        }

        private static readonly List<Entity> triggerersToRemove = new List<Entity>();
        public static void RemoveInActiveTriggerers(PhysicsBody physicsBody, HashSet<Entity> triggerers)
        {
            if (physicsBody == null) { return; }

            triggerersToRemove.Clear();
            foreach (var triggerer in triggerers)
            {
                if (triggerer.Removed)
                {
                    triggerersToRemove.Add(triggerer);
                }
                else if (!CheckContactsForEntity(physicsBody, triggerer))
                {
                    triggerersToRemove.Add(triggerer);
                }
            }
            foreach (var triggerer in triggerersToRemove)
            {
                triggerers.Remove(triggerer);
            }
        }

        public static void ApplyStatusEffects(List<StatusEffect> statusEffects, Vector2 worldPosition, Entity triggerer, float deltaTime, List<ISerializableEntity> targets)
        {
            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.type == ActionType.OnBroken) { return; }
                Vector2? position = null;
                if (effect.HasTargetType(StatusEffect.TargetType.This)) { position = worldPosition; }
                if (triggerer is Character character)
                {
                    effect.Apply(effect.type, deltaTime, triggerer, character, position);
                    if (effect.HasTargetType(StatusEffect.TargetType.Contained) && character.Inventory != null)
                    {
                        foreach (Item item in character.Inventory.AllItemsMod)
                        {
                            if (item.ContainedItems == null) { continue; }
                            foreach (Item containedItem in item.ContainedItems)
                            {
                                effect.Apply(effect.type, deltaTime, triggerer, containedItem.AllPropertyObjects, position);
                            }
                        }
                    }
                }
                else if (triggerer is Item item)
                {
                    effect.Apply(effect.type, deltaTime, triggerer, item.AllPropertyObjects, position);
                }
                if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) || effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                {
                    targets.Clear();
                    effect.AddNearbyTargets(worldPosition, targets);
                    effect.Apply(effect.type, deltaTime, triggerer, targets);
                }
            }
        }

        /// <summary>
        /// Applies attacks to a damageable.
        /// </summary>
        public static void ApplyAttacks(List<Attack> attacks, IDamageable damageable, Vector2 worldPosition, float deltaTime)
        {
            foreach (Attack attack in attacks)
            {
                attack.DoDamage(null, damageable, worldPosition, deltaTime, false);
            }
        }

        /// <summary>
        /// Applies attacks to structures.
        /// </summary>
        public static void ApplyAttacks(List<Attack> attacks, Vector2 worldPosition, float deltaTime)
        {
            foreach (Attack attack in attacks)
            {
                float structureDamage = attack.GetStructureDamage(deltaTime);
                if (structureDamage > 0.0f)
                {
                    Explosion.RangedStructureDamage(worldPosition, attack.DamageRange, structureDamage, levelWallDamage: 0.0f, emitWallDamageParticles: attack.EmitStructureDamageParticles);
                }
            }
        }

        private void ApplyForce(PhysicsBody body)
        {
            if (body == null) { return; }

            float distFactor = 1.0f;
            if (ForceFalloff)
            {
                distFactor = GetDistanceFactor(body, PhysicsBody, ColliderRadius);
                if (distFactor < 0.0f) return;
            }

            switch (ForceMode)
            {
                case TriggerForceMode.Force:
                    if (ForceVelocityLimit < 1000.0f)
                        body.ApplyForce(Force * currentForceFluctuation * distFactor, ForceVelocityLimit);
                    else
                        body.ApplyForce(Force * currentForceFluctuation * distFactor);
                    break;
                case TriggerForceMode.Acceleration:
                    if (ForceVelocityLimit < 1000.0f)
                        body.ApplyForce(Force * body.Mass * currentForceFluctuation * distFactor, ForceVelocityLimit);
                    else
                        body.ApplyForce(Force * body.Mass * currentForceFluctuation * distFactor);
                    break;
                case TriggerForceMode.Impulse:
                    if (ForceVelocityLimit < 1000.0f)
                        body.ApplyLinearImpulse(Force * currentForceFluctuation * distFactor, maxVelocity: ForceVelocityLimit);
                    else
                        body.ApplyLinearImpulse(Force * currentForceFluctuation * distFactor);
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

        public static float GetDistanceFactor(PhysicsBody triggererBody, PhysicsBody triggerBody, float colliderRadius)
        {
            return 1.0f - ConvertUnits.ToDisplayUnits(Vector2.Distance(triggererBody.SimPosition, triggerBody.SimPosition)) / colliderRadius;
        }

        public Vector2 GetWaterFlowVelocity(Vector2 viewPosition)
        {
            Vector2 baseVel = GetWaterFlowVelocity();
            if (baseVel.LengthSquared() < 0.1f) return Vector2.Zero;

            float triggerSize = ConvertUnits.ToDisplayUnits(Math.Max(Math.Max(PhysicsBody.Radius, PhysicsBody.Width / 2.0f), PhysicsBody.Height / 2.0f));
            float dist = Vector2.Distance(viewPosition, WorldPosition);
            if (dist > triggerSize) return Vector2.Zero;

            return baseVel * (1.0f - dist / triggerSize);
        }

        public Vector2 GetWaterFlowVelocity()
        {
            if (Force == Vector2.Zero || ForceMode == TriggerForceMode.LimitVelocity) { return Vector2.Zero; }
            
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
