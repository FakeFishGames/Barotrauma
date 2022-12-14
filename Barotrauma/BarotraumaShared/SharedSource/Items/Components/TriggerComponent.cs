using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class TriggerComponent : ItemComponent 
    {
        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "The maximum amount of force applied to the triggering entitites.", alwaysUseInstanceValues: true)]
        public float Force { get; set; }
        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Determines if the force gets higher the closer the triggerer is to the center of the trigger.", alwaysUseInstanceValues: true)]
        public bool DistanceBasedForce { get; set; }
        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Determines if the force fluctuates over time or if it stays constant.", alwaysUseInstanceValues: true)]
        public bool ForceFluctuation { get; set; }
        [Serialize(1.0f, IsPropertySaveable.Yes, description: "How much the fluctuation affects the force. 1 is the maximum fluctuation, 0 is no fluctuation.", alwaysUseInstanceValues: true)]
        private float ForceFluctuationStrength
        {
            get
            {
                return forceFluctuationStrength;
            }
            set
            {
                forceFluctuationStrength = Math.Clamp(value, 0.0f, 1.0f);
            }
        }
        [Serialize(1.0f, IsPropertySaveable.Yes, description: "How fast (cycles per second) the force fluctuates.", alwaysUseInstanceValues: true)]
        private float ForceFluctuationFrequency
        {
            get
            {
                return forceFluctuationFrequency;
            }
            set
            {
                forceFluctuationFrequency = Math.Max(value, 0.01f);
            }
        }
        [Serialize(0.01f, IsPropertySaveable.Yes, description: "How often (in seconds) the force fluctuation is calculated.", alwaysUseInstanceValues: true)]
        private float ForceFluctuationInterval
        {
            get
            {
                return forceFluctuationInterval;
            }
            set
            {
                forceFluctuationInterval = Math.Max(value, 0.01f);
            }
        }

        public PhysicsBody PhysicsBody { get; private set; }
        private float Radius { get; set; }
        private float RadiusInDisplayUnits { get; set; }
        private bool TriggeredOnce { get; set; }
        private float CurrentForceFluctuation { get; set; } = 1.0f;
        public bool TriggerActive { get; private set; }
        private float ForceFluctuationTimer { get; set; }
        private static float TimeInLevel
        {
            get
            {
                return GameMain.GameSession?.RoundDuration ?? 0.0f;
            }
        } 

        private readonly LevelTrigger.TriggererType triggeredBy;
        private readonly HashSet<Entity> triggerers = new HashSet<Entity>();
        private readonly bool triggerOnce;
        private readonly List<ISerializableEntity> statusEffectTargets = new List<ISerializableEntity>();
        /// <summary>
        /// Effects applied to entities inside the trigger
        /// </summary>
        private readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
        /// <summary>
        /// Attacks applied to entities inside the trigger
        /// </summary>
        private readonly List<Attack> attacks = new List<Attack>();

        private float forceFluctuationStrength;
        private float forceFluctuationFrequency;
        private float forceFluctuationInterval;

        public TriggerComponent(Item item, ContentXElement element) : base(item, element)
        {
            string triggeredByAttribute = element.GetAttributeString("triggeredby", "Character");
            if (!Enum.TryParse(triggeredByAttribute, out triggeredBy))
            {
                DebugConsole.ThrowError($"Error in ForceComponent config: \"{triggeredByAttribute}\" is not a valid triggerer type.");
            }
            triggerOnce = element.GetAttributeBool("triggeronce", false);
            string parentDebugName = $"TriggerComponent in {item.Name}";
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        LevelTrigger.LoadStatusEffect(statusEffects, subElement, parentDebugName);
                        break;
                    case "attack":
                    case "damage":
                        LevelTrigger.LoadAttack(subElement, parentDebugName, triggerOnce, attacks);
                        break;
                }
            }
            IsActive = true;
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            float radiusAttribute = originalElement.GetAttributeFloat("radius", 10.0f);
            Radius = ConvertUnits.ToSimUnits(radiusAttribute * item.Scale);
            PhysicsBody = new PhysicsBody(0.0f, 0.0f, Radius, 1.5f, BodyType.Static, Physics.CollisionWall, LevelTrigger.GetCollisionCategories(triggeredBy))
            {
                UserData = item
            };
            PhysicsBody.SetTransformIgnoreContacts(item.SimPosition, 0.0f);
            PhysicsBody.FarseerBody.SetIsSensor(true);
            PhysicsBody.FarseerBody.OnCollision += OnCollision;
            PhysicsBody.FarseerBody.OnSeparation += OnSeparation;
            RadiusInDisplayUnits = ConvertUnits.ToDisplayUnits(PhysicsBody.radius);
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            PhysicsBody.SetTransformIgnoreContacts(item.SimPosition, 0.0f);
            PhysicsBody.Submarine = item.Submarine;
        }

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (!(LevelTrigger.GetEntity(other) is Entity entity)) { return false; }
            if (!LevelTrigger.IsTriggeredByEntity(entity, triggeredBy, mustBeOnSpecificSub: (true, item.Submarine))) { return false; }
            triggerers.Add(entity);
            return true;
        }

        private void OnSeparation(Fixture sender, Fixture other, Contact contact)
        {
            if (!(LevelTrigger.GetEntity(other) is Entity entity))
            {
                return;
            }
            if (entity is Character character && (!character.Enabled || character.Removed) && triggerers.Contains(entity))
            {
                triggerers.Remove(entity);
                return;
            }
            if (LevelTrigger.CheckContactsForOtherFixtures(PhysicsBody, other, entity))
            {
                return;
            }
            triggerers.Remove(entity);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            LevelTrigger.RemoveInActiveTriggerers(PhysicsBody, triggerers);

            if (triggerOnce)
            {
                if (TriggeredOnce) { return; }
                if (triggerers.Count > 0)
                {
                    TriggeredOnce = true;
                    IsActive = false;
                    triggerers.Clear();
                }
            }

            TriggerActive = triggerers.Any();

            if (ForceFluctuation && TriggerActive && (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer))
            {
                ForceFluctuationTimer += deltaTime;
                if (ForceFluctuationTimer >= ForceFluctuationInterval)
                {
                    float v = MathF.Sin(2 * MathF.PI * ForceFluctuationFrequency * TimeInLevel);
                    float amount = MathUtils.InverseLerp(-1.0f, 1.0f, v);
                    CurrentForceFluctuation = MathHelper.Lerp(1.0f - ForceFluctuationStrength, 1.0f, amount);
                    ForceFluctuationTimer = 0.0f;
                    GameMain.NetworkMember?.CreateEntityEvent(this);
                }
            }

            foreach (Entity triggerer in triggerers)
            {
                LevelTrigger.ApplyStatusEffects(statusEffects, item.WorldPosition, triggerer, deltaTime, statusEffectTargets);

                if (triggerer is IDamageable damageable)
                {
                    LevelTrigger.ApplyAttacks(attacks, damageable, item.WorldPosition, deltaTime);
                }
                else if (triggerer is Submarine submarine)
                {
                    LevelTrigger.ApplyAttacks(attacks, item.WorldPosition, deltaTime);
                }

                if (Math.Abs(Force) < 0.01f)
                {
                    // Just ignore very minimal forces
                    continue;
                }
                else if (triggerer is Character c)
                {
                    ApplyForce(c.AnimController.Collider);
                }
                else if (triggerer is Submarine s)
                {
                    ApplyForce(s.SubBody.Body);
                }
                else if (triggerer is Item i && i.body != null)
                {
                    ApplyForce(i.body);
                }
            }

            item.SendSignal(IsActive ? "1" : "0", "state_out");
        }

        private void ApplyForce(PhysicsBody body)
        {
            Vector2 diff = ConvertUnits.ToDisplayUnits(PhysicsBody.SimPosition - body.SimPosition);
            if (diff.LengthSquared() < 0.0001f) { return; }
            float distanceFactor = DistanceBasedForce ? LevelTrigger.GetDistanceFactor(body, PhysicsBody, RadiusInDisplayUnits) : 1.0f;
            if (distanceFactor <= 0.0f) { return; }
            Vector2 force = distanceFactor * (CurrentForceFluctuation * Force) * Vector2.Normalize(diff);
            if (force.LengthSquared() < 0.01f) { return; }
            body.ApplyForce(force);
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (PhysicsBody != null)
            {
                if (ignoreContacts)
                {
                    PhysicsBody.SetTransformIgnoreContacts(PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);
                }
                else
                {
                    PhysicsBody.SetTransform(PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);
                }
                PhysicsBody.Submarine = item.Submarine;
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            base.ReceiveSignal(signal, connection);
            switch (connection.Name)
            {
                case "set_force":
                    if (!FloatTryParse(signal, out float force)) { break; }
                    Force = force;
                    break;
                case "set_distancebasedforce":
                    if (!bool.TryParse(signal.value, out bool distanceBasedForce)) { break; }
                    DistanceBasedForce = distanceBasedForce;
                    break;
                case "set_forcefluctuation":
                    if (!bool.TryParse(signal.value, out bool forceFluctuation)) { break; }
                    ForceFluctuation = forceFluctuation;
                    break;
                case "set_forcefluctuationstrength":
                    if (!FloatTryParse(signal, out float forceFluctuationStrength)) { break; }
                    ForceFluctuationStrength = forceFluctuationStrength;
                    break;
                case "set_forcefluctuationfrequency":
                    if (!FloatTryParse(signal, out float forceFluctuationFrequency)) { break; }
                    ForceFluctuationFrequency = forceFluctuationFrequency;
                    break;
                case "set_forcefluctuationinterval":
                    if (!FloatTryParse(signal, out float forceFluctuationInterval)) { break; }
                    ForceFluctuationInterval = forceFluctuationInterval;
                    break;
            }

            static bool FloatTryParse(Signal signal, out float value)
            {
                return float.TryParse(signal.value, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }
        }
    }
}
 