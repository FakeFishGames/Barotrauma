using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class TriggerComponent : ItemComponent 
    {
        [Editable, Serialize(0.0f, true, description: "The maximum amount of force applied to the triggering entitites.", alwaysUseInstanceValues: true)]
        public float Force { get; set; }

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
                if (GameMain.GameSession != null)
                {
                    return (float)(Timing.TotalTime - GameMain.GameSession.RoundStartTime);
                }
                else
                {
                    return 0.0f;
                }
            }
        } 

        private readonly LevelTrigger.TriggererType triggeredBy;
        private readonly HashSet<Entity> triggerers = new HashSet<Entity>();
        private readonly bool triggerOnce;
        private readonly bool distanceBasedForce;
        private readonly bool forceFluctuation;
        private readonly float forceFluctuationStrength;
        private readonly float forceFluctuationFrequency;
        private readonly float forceFluctuationInterval;
        private readonly List<ISerializableEntity> statusEffectTargets = new List<ISerializableEntity>();
        /// <summary>
        /// Effects applied to entities inside the trigger
        /// </summary>
        private readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
        /// <summary>
        /// Attacks applied to entities inside the trigger
        /// </summary>
        private readonly List<Attack> attacks = new List<Attack>();

        public TriggerComponent(Item item, XElement element) : base(item, element)
        {
            string triggeredByAttribute = element.GetAttributeString("triggeredby", "Character");
            if (!Enum.TryParse(triggeredByAttribute, out triggeredBy))
            {
                DebugConsole.ThrowError($"Error in ForceComponent config: \"{triggeredByAttribute}\" is not a valid triggerer type.");
            }
            triggerOnce = element.GetAttributeBool("triggeronce", false);
            distanceBasedForce = element.GetAttributeBool("distancebasedforce", false);
            forceFluctuation = element.GetAttributeBool("forcefluctuation", false);
            forceFluctuationStrength = element.GetAttributeFloat("forcefluctuationstrength", 1.0f);
            forceFluctuationStrength = Math.Clamp(forceFluctuationStrength, 0.0f, 1.0f);
            forceFluctuationFrequency = element.GetAttributeFloat("fluctuationfrequency", 1.0f);
            forceFluctuationFrequency = Math.Max(forceFluctuationFrequency, 0.01f);
            forceFluctuationInterval = element.GetAttributeFloat("fluctuationinterval", 0.01f);
            forceFluctuationInterval = Math.Max(forceFluctuationInterval, 0.01f);

            string parentDebugName = $"TriggerComponent in {item.Name}";
            foreach (XElement subElement in element.Elements())
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
            PhysicsBody = new PhysicsBody(0.0f, 0.0f, Radius, 1.5f)
            {
                BodyType = BodyType.Static,
                CollidesWith = LevelTrigger.GetCollisionCategories(triggeredBy),
                CollisionCategories = Physics.CollisionWall,
                UserData = item
            };
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

            if (forceFluctuation && TriggerActive && (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer))
            {
                ForceFluctuationTimer += deltaTime;
                if (ForceFluctuationTimer >= forceFluctuationInterval)
                {
                    float v = MathF.Sin(2 * MathF.PI * forceFluctuationFrequency * TimeInLevel);
                    float amount = MathUtils.InverseLerp(-1.0f, 1.0f, v);
                    CurrentForceFluctuation = MathHelper.Lerp(1.0f - forceFluctuationStrength, 1.0f, amount);
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

                if (Force < 0.01f)
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
            float distanceFactor = distanceBasedForce ? LevelTrigger.GetDistanceFactor(body, PhysicsBody, RadiusInDisplayUnits) : 1.0f;
            if (distanceFactor <= 0.0f) { return; }
            Vector2 force = distanceFactor * (CurrentForceFluctuation * Force) * Vector2.Normalize(diff);
            if (force.LengthSquared() < 0.01f) { return; }
            body.ApplyForce(force);
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);
            if (PhysicsBody != null)
            {
                PhysicsBody.SetTransform(PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);
                PhysicsBody.Submarine = item.Submarine;
            }
        }
    }
}