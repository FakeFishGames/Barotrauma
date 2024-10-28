using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Barotrauma.Extensions;

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

        private float radius;
        [Editable, Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Radius
        {
            get => radius;
            
            set
            {
                if (radius == value) { return; }
                radius = value;
                if (PhysicsBody != null) { RefreshPhysicsBodySize(); }
            }
        }

        private float width;
        [Editable, Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Width
        {
            get => width;

            set
            {
                if (width == value) { return; }
                width = value;
                if (PhysicsBody != null) { RefreshPhysicsBodySize(); }
            }
        }

        private float height;
        [Editable, Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Height
        {
            get => height;

            set
            {
                if (height == value) { return; }
                height = value;
                if (PhysicsBody != null) { RefreshPhysicsBodySize(); }
            }
        }

        private float currentRadius, currentWidth, currentHeight;

        private Vector2 bodyOffset;
        [Editable, Serialize("0,0", IsPropertySaveable.Yes)]
        public Vector2 BodyOffset
        {
            get => bodyOffset;

            set
            {
                if (bodyOffset == value) { return; }
                bodyOffset = value;
                if (PhysicsBody != null) { SetPhysicsBodyPosition(); }
            }
        }

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

        [Serialize(false, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool ApplyEffectsToCharactersInsideSub { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool MoveOutsideSub { get; set; }

        public override bool IsActive 
        {
            get => base.IsActive;
            set
            {
                base.IsActive = value;
                if (!IsActive)
                {
                    TriggerActive = false;
                    triggerers.Clear();
                }
            }
        }

        private readonly LevelTrigger.TriggererType triggeredBy;
        private readonly Identifier triggerSpeciesOrGroup;
        private readonly PropertyConditional.LogicalComparison conditionals;
        
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
            string triggeredByString = element.GetAttributeString("triggeredby", "Character");
            if (!Enum.TryParse(triggeredByString, out triggeredBy))
            {
                Identifier speciesOrGroup = triggeredByString.ToIdentifier();
                if (CharacterPrefab.Prefabs.Any(p => p.MatchesSpeciesNameOrGroup(speciesOrGroup)))
                {
                    triggerSpeciesOrGroup = speciesOrGroup;
                    triggeredBy = LevelTrigger.TriggererType.Character;
                }
                else
                {
                    DebugConsole.ThrowError($"Error in ForceComponent config: \"{triggeredByString}\" is not a valid triggerer type.",
                        contentPackage: element.ContentPackage);
                }
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
            conditionals = PropertyConditional.LoadConditionals(element);
            IsActive = true;
        }

        public override void OnItemLoaded()
        {
            RefreshPhysicsBodySize();
        }

        private void RefreshPhysicsBodySize()
        {
            PhysicsBody?.Remove();

            currentWidth = ConvertUnits.ToSimUnits(Width * item.Scale);
            currentHeight = ConvertUnits.ToSimUnits(Height * item.Scale);
            if (currentWidth > 0 && currentHeight > 0)
            {
                PhysicsBody = new PhysicsBody(currentWidth, currentHeight, radius: 0.0f, density: 1.5f, BodyType.Static, Physics.CollisionWall, LevelTrigger.GetCollisionCategories(triggeredBy))
                {
                    UserData = item
                };
            }
            else
            {
                currentRadius = Math.Max(ConvertUnits.ToSimUnits(Radius * item.Scale), 0.01f);
                PhysicsBody = new PhysicsBody(width: 0.0f, height: 0.0f, radius: currentRadius, density: 1.5f, BodyType.Static, Physics.CollisionWall, LevelTrigger.GetCollisionCategories(triggeredBy))
                {
                    UserData = item
                };
            }

            SetPhysicsBodyPosition();
            PhysicsBody.FarseerBody.SetIsSensor(originalElement.GetAttributeBool("sensor", true));
            PhysicsBody.FarseerBody.OnCollision += OnCollision;
            PhysicsBody.FarseerBody.OnSeparation += OnSeparation;
            RadiusInDisplayUnits = ConvertUnits.ToDisplayUnits(PhysicsBody.Radius);
        }

        public void SetPhysicsBodyPosition(bool ignoreContacts = true)
        {
            if (PhysicsBody == null) { return; }

            Vector2 offset = ConvertUnits.ToSimUnits(BodyOffset * item.Scale);
            if (item.FlippedX)
            {
                offset.X = -offset.X;
            }
            if (item.FlippedY)
            {
                offset.Y = -offset.Y;
            }
            if (!MathUtils.NearlyEqual(item.RotationRad, 0))
            {
                Matrix transform = Matrix.CreateRotationZ(-item.RotationRad);
                offset = Vector2.Transform(offset, transform);
            }
            if (ignoreContacts)
            {
                PhysicsBody.SetTransformIgnoreContacts(item.SimPosition + offset, -item.RotationRad);
            }
            else
            {
                PhysicsBody.SetTransform(item.SimPosition + offset, -item.RotationRad);
            }
            PhysicsBody.UpdateDrawPosition();
        }

        public override void FlipX(bool relativeToSub)
        {
            SetPhysicsBodyPosition();
        }
        public override void FlipY(bool relativeToSub)
        {
            SetPhysicsBodyPosition();
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            SetPhysicsBodyPosition(true);
            PhysicsBody.Submarine = item.Submarine;
        }

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (LevelTrigger.GetEntity(other) is not Entity entity) { return false; }
            if (!LevelTrigger.IsTriggeredByEntity(entity, triggeredBy, triggerSpeciesOrGroup, conditionals, mustBeOnSpecificSub: (!MoveOutsideSub, item.Submarine))) { return false; }
            triggerers.Add(entity);
            return true;
        }

        private void OnSeparation(Fixture sender, Fixture other, Contact contact)
        {
            if (LevelTrigger.GetEntity(other) is not Entity entity)
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
            if (item.Submarine != null && MoveOutsideSub)
            {                
                item.SetTransform(ConvertUnits.ToSimUnits(item.WorldPosition), item.Rotation);
                item.CurrentHull = null;
                item.Submarine = null;
                SetPhysicsBodyPosition();
                PhysicsBody.Submarine = item.Submarine;
            }
            else if (item.body is { BodyType: BodyType.Dynamic })
            {
                SetPhysicsBodyPosition();
                PhysicsBody.Submarine = item.Submarine;
            }

            LevelTrigger.RemoveInActiveTriggerers(PhysicsBody, triggerers);

            if (triggerOnce)
            {
                if (TriggeredOnce) { return; }
                if (triggerers.Count > 0)
                {
                    TriggeredOnce = true;
                    IsActive = false;
                }
            }

            TriggerActive = triggerers.Any();
            if (TriggerActive && conditionals != null)
            {
                switch (conditionals.LogicalOperator)
                {
                    case PropertyConditional.LogicalOperatorType.And:
                    {
                        if (triggerers.Any(t => !PropertyConditional.CheckConditionals((ISerializableEntity)t, conditionals.Conditionals, conditionals.LogicalOperator)))
                        {
                            // Some of the conditionals doesn't match
                            IsActive = false;
                        }
                        break;
                    }
                    case PropertyConditional.LogicalOperatorType.Or:
                    {
                        if (triggerers.None(t => !PropertyConditional.CheckConditionals((ISerializableEntity)t, conditionals.Conditionals, conditionals.LogicalOperator)))
                        {
                            // None of the conditionals match
                            IsActive = false;
                        }
                        break;
                    }
                }
            }

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
                LevelTrigger.ApplyStatusEffects(statusEffects, item.WorldPosition, triggerer, deltaTime, statusEffectTargets, targetItem: Item);

                if (triggerer is IDamageable damageable)
                {
                    LevelTrigger.ApplyAttacks(attacks, damageable, item.WorldPosition, deltaTime);
                }
                else if (triggerer is Submarine submarine)
                {
                    LevelTrigger.ApplyAttacks(attacks, item.WorldPosition, deltaTime);
                    foreach (Character c2 in Character.CharacterList)
                    {
                        if (c2.Submarine == submarine)
                        {
                            LevelTrigger.ApplyAttacks(attacks, c2, item.WorldPosition, deltaTime);
                        }
                    }
                }

                if (Math.Abs(Force) < 0.01f)
                {
                    // Just ignore very minimal forces
                    continue;
                }
                else if (triggerer is Character c)
                {
                    if (c.AnimController.Collider.BodyType == BodyType.Dynamic)
                    {
                        if (c.AnimController.Collider.Enabled)
                        {
                            ApplyForce(c.AnimController.Collider);
                        }
                        foreach (var limb in c.AnimController.Limbs)
                        {
                            ApplyForce(limb.body, multiplier: limb.Mass * c.AnimController.Collider.Mass / c.AnimController.Mass);
                        }
                    }
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

        private void ApplyForce(PhysicsBody body, float multiplier = 1.0f)
        {
            Vector2 diff = ConvertUnits.ToDisplayUnits(item.SimPosition - body.SimPosition);
            if (diff.LengthSquared() < 0.0001f) { return; }
            float distanceFactor = DistanceBasedForce ? LevelTrigger.GetDistanceFactor(body, PhysicsBody, RadiusInDisplayUnits) : 1.0f;
            if (distanceFactor <= 0.0f) { return; }
            Vector2 force = distanceFactor * (CurrentForceFluctuation * Force) * Vector2.Normalize(diff) * multiplier;
            if (force.LengthSquared() < 0.01f) { return; }
            if (body.Mass < 1)
            {
                //restrict the force if the body is very light, otherwise it can end up moving at a speed that breaks physics
                force *= body.Mass;
            }
            body.ApplyForce(force);
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (PhysicsBody != null)
            {
                SetPhysicsBodyPosition(ignoreContacts);
                PhysicsBody.Submarine = item.Submarine;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            if (PhysicsBody != null)
            {
                PhysicsBody.Remove();
                PhysicsBody = null;
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
 