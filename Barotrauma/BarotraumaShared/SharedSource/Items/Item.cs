using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;
using System.Collections.Immutable;
using Barotrauma.Abilities;

#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, IIgnorable, ISerializableEntity, IServerPositionSync, IClientSerializable
    {
        public static List<Item> ItemList = new List<Item>();
        public new ItemPrefab Prefab => base.Prefab as ItemPrefab;

        public static bool ShowLinks = true;

        private readonly HashSet<Identifier> tags;

        private bool isWire, isLogic;

        private Hull currentHull;
        public Hull CurrentHull
        {
            get { return currentHull; }
            set
            {
                currentHull = value;
            }
        }

        public float HullOxygenPercentage
        {
            get { return CurrentHull?.OxygenPercentage ?? 0.0f; }
        }

        private CampaignMode.InteractionType campaignInteractionType = CampaignMode.InteractionType.None;
        public CampaignMode.InteractionType CampaignInteractionType
        {
            get { return campaignInteractionType; }
            set
            {
                if (campaignInteractionType != value)
                {
                    campaignInteractionType = value;
                    AssignCampaignInteractionTypeProjSpecific(campaignInteractionType);
                }
            }
        }

        partial void AssignCampaignInteractionTypeProjSpecific(CampaignMode.InteractionType interactionType);

        public bool Visible = true;

#if CLIENT
        public SpriteEffects SpriteEffects = SpriteEffects.None;
#endif

        //components that determine the functionality of the item
        private readonly Dictionary<Type, ItemComponent> componentsByType = new Dictionary<Type, ItemComponent>();
        private readonly List<ItemComponent> components;
        /// <summary>
        /// Components that are Active or need to be updated for some other reason (status effects, sounds)
        /// </summary>
        private readonly List<ItemComponent> updateableComponents = new List<ItemComponent>();
        private readonly List<IDrawableComponent> drawableComponents;
        private bool hasComponentsToDraw;

        public PhysicsBody body;

        public readonly XElement StaticBodyConfig;

        public List<Fixture> StaticFixtures = new List<Fixture>();

        private bool transformDirty = true;

        private float lastSentCondition;
        private float sendConditionUpdateTimer;
        private bool conditionUpdatePending;

        private float condition;

        private bool inWater;
        private readonly bool hasWaterStatusEffects;

        private Inventory parentInventory;
        private readonly ItemInventory ownInventory;

        private Rectangle defaultRect;
        /// <summary>
        /// Unscaled rect
        /// </summary>
        public Rectangle DefaultRect
        {
            get { return defaultRect; }
            set { defaultRect = value; }
        }

        private readonly Dictionary<string, Connection> connections;

        private readonly List<Repairable> repairables;

        private readonly Quality qualityComponent;

        private ConcurrentQueue<float> impactQueue;

        //a dictionary containing lists of the status effects in all the components of the item
        private readonly bool[] hasStatusEffectsOfType;
        private readonly Dictionary<ActionType, List<StatusEffect>> statusEffectLists;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; protected set; }

        private bool? hasInGameEditableProperties;
        bool HasInGameEditableProperties
        {
            get
            {
                if (hasInGameEditableProperties == null)
                {
                    hasInGameEditableProperties = false;
                    if (SerializableProperties.Values.Any(p => p.Attributes.OfType<InGameEditable>().Any()))
                    {
                        hasInGameEditableProperties = true;
                    }
                    else
                    {
                        foreach (ItemComponent component in components)
                        {
                            if (!component.AllowInGameEditing) { continue; }
                            if (component.SerializableProperties.Values.Any(p => p.Attributes.OfType<InGameEditable>().Any())
                                || component.SerializableProperties.Values.Any(p => p.Attributes.OfType<ConditionallyEditable>().Any(a => a.IsEditable(this))))
                            {
                                hasInGameEditableProperties = true;
                                break;
                            }
                        }
                    }
                }
                return (bool)hasInGameEditableProperties;
            }
        }

        public bool EditableWhenEquipped { get; set; } = false;

        //the inventory in which the item is contained in
        public Inventory ParentInventory
        {
            get
            {
                return parentInventory;
            }
            set
            {
                parentInventory = value;
                if (parentInventory != null) { Container = parentInventory.Owner as Item; }
#if SERVER
                PreviousParentInventory = value;
#endif
            }
        }

        private Item container;
        public Item Container
        {
            get { return container; }
            private set
            {
                if (value != container)
                {
                    container = value;
                    SetActiveSprite();
                }
            }
        }
                
        public override string Name
        {
            get { return base.Prefab.Name.Value; }
        }

        private string description;
        public string Description
        {
            get { return description ?? base.Prefab.Description.Value; }
            set { description = value; }
        }

        [Editable, Serialize(false, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool NonInteractable
        {
            get;
            set;
        }

        /// <summary>
        /// Use <see cref="IsPlayerInteractable"/> to also check <see cref="NonInteractable"/>
        /// </summary>
        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "When enabled, item is interactable only for characters on non-player teams.", alwaysUseInstanceValues: true)]
        public bool NonPlayerTeamInteractable
        {
            get;
            set;
        }

        [ConditionallyEditable(ConditionallyEditable.ConditionType.IsSwappableItem), Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool AllowSwapping
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool PurchasedNewSwap
        {
            get;
            set;
        }

        /// <summary>
        /// Checks both <see cref="NonInteractable"/> and <see cref="NonPlayerTeamInteractable"/>
        /// </summary>
        public bool IsPlayerTeamInteractable
        {
            get
            {
                return !NonInteractable && !NonPlayerTeamInteractable;
            }
        }

        /// <summary>
        /// Returns interactibility based on whether the character is on a player team
        /// </summary>
        public bool IsInteractable(Character character)
        {
#if CLIENT
            if (Screen.Selected is EditorScreen)
            {
                return true;
            }
#endif
            if (HiddenInGame) { return false; }
            if (character != null && character.IsOnPlayerTeam)
            {
                return IsPlayerTeamInteractable;
            }
            else
            {
                return !NonInteractable;
            }
        }

        public float RotationRad { get; private set; } 

        [ConditionallyEditable(ConditionallyEditable.ConditionType.AllowRotating, MinValueFloat = 0.0f, MaxValueFloat = 360.0f, DecimalCount = 1, ValueStep = 1f), Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Rotation
        {
            get
            {
                return MathHelper.ToDegrees(RotationRad);
            }
            set
            {
                if (!Prefab.AllowRotatingInEditor) { return; }
                RotationRad = MathHelper.ToRadians(value);
#if CLIENT
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    SetContainedItemPositions();
                    GetComponent<LightComponent>()?.SetLightSourceTransform();
                }
#endif
            }
        }

        public float ImpactTolerance
        {
            get { return Prefab.ImpactTolerance; }
        }
        
        public float InteractDistance
        {
            get { return Prefab.InteractDistance; }
        }

        public float InteractPriority
        {
            get { return Prefab.InteractPriority; }
        }

        public override Vector2 Position
        {
            get
            {
                return (body == null) ? base.Position : body.Position;
            }
        }

        public override Vector2 SimPosition
        {
            get
            {
                return (body == null) ? ConvertUnits.ToSimUnits(base.Position) : body.SimPosition;
            }
        }

        public Rectangle InteractionRect
        {
            get
            {
                return WorldRect;
            }
        }

        private float scale = 1.0f;
        public override float Scale
        {
            get { return scale; }
            set
            {
                if (scale == value) { return; }
                scale = MathHelper.Clamp(value, 0.01f, 10.0f);

                float relativeScale = scale / base.Prefab.Scale;

                if (!ResizeHorizontal || !ResizeVertical)
                {
                    int newWidth = ResizeHorizontal ? rect.Width : (int)(defaultRect.Width * relativeScale);
                    int newHeight = ResizeVertical ? rect.Height : (int)(defaultRect.Height * relativeScale);
                    Rect = new Rectangle(rect.X, rect.Y, newWidth, newHeight);
                }

                if (components != null)
                {
                    foreach (ItemComponent component in components)
                    {
                        component.OnScaleChanged();
                    }
                }
            }
        }

        public float PositionUpdateInterval
        {
            get;
            set;
        } = float.PositiveInfinity;

        protected Color spriteColor;
        [Editable, Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes)]
        public Color SpriteColor
        {
            get { return spriteColor; }
            set { spriteColor = value; }
        }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes), Editable]
        public Color InventoryIconColor
        {
            get;
            protected set;
        }

        [Editable, Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, description: "Changes the color of the item this item is contained inside. Only has an effect if either of the UseContainedSpriteColor or UseContainedInventoryIconColor property of the container is set to true.")]
        public Color ContainerColor
        {
            get;
            protected set;
        }

        /// <summary>
        /// Can be used by status effects or conditionals to check what item this item is contained inside
        /// </summary>
        public Identifier ContainerIdentifier
        {
            get
            {
                return 
                    Container?.Prefab.Identifier ?? 
                    ParentInventory?.Owner?.ToIdentifier() ?? 
                    Identifier.Empty;
            }
        }


        [Serialize("", IsPropertySaveable.Yes)]

        /// <summary>
        /// Can be used to modify the AITarget's label using status effects
        /// </summary>
        public string SonarLabel
        {
            get { return AiTarget?.SonarLabel?.Value ?? ""; }
            set
            {
                if (AiTarget != null)
                {
                    AiTarget.SonarLabel = !string.IsNullOrEmpty(value) && value.Length > 200 ? value.Substring(200) : value;
                }
            }
        }

        /// <summary>
        /// Can be used by status effects or conditionals to check if the physics body of the item is active
        /// </summary>
        public bool PhysicsBodyActive
        {
            get
            {
                return body != null && body.Enabled;
            }
        }

        [Serialize(0.0f, IsPropertySaveable.No)]
        /// <summary>
        /// Can be used by status effects or conditionals to modify the sound range
        /// </summary>
        public new float SoundRange
        {
            get { return aiTarget == null ? 0.0f : aiTarget.SoundRange; }
            set { if (aiTarget != null) { aiTarget.SoundRange = Math.Max(0.0f, value); } }
        }

        [Serialize(0.0f, IsPropertySaveable.No)]
        /// <summary>
        /// Can be used by status effects or conditionals to modify the sound range
        /// </summary>
        public new float SightRange
        {
            get { return aiTarget == null ? 0.0f : aiTarget.SightRange; }
            set { if (aiTarget != null) { aiTarget.SightRange = Math.Max(0.0f, value); } }
        }

        /// <summary>
        /// Should the item's Use method be called with the "Use" or with the "Shoot" key?
        /// </summary>
        [Serialize(false, IsPropertySaveable.No)]
        public bool IsShootable { get; set; }

        /// <summary>
        /// If true, the user has to hold the "aim" key before use is registered. False by default.
        /// </summary>
        [Serialize(false, IsPropertySaveable.No)]
        public bool RequireAimToUse
        {
            get; set;
        }

        /// <summary>
        /// If true, the user has to hold the "aim" key before secondary use is registered. True by default.
        /// </summary>
        [Serialize(true, IsPropertySaveable.No)]
        public bool RequireAimToSecondaryUse
        {
            get; set;
        }

        public Color Color
        {
            get { return spriteColor; }
        }

        public bool IsFullCondition { get; private set; }
        public float MaxCondition { get; private set; }
        public float ConditionPercentage { get; private set; }

        private float offsetOnSelectedMultiplier = 1.0f;

        [Serialize(1.0f, IsPropertySaveable.No)]
        public float OffsetOnSelectedMultiplier
        {
            get => offsetOnSelectedMultiplier;
            set => offsetOnSelectedMultiplier = value;
        }
        
        private float healthMultiplier = 1.0f;

        [Serialize(1.0f, IsPropertySaveable.Yes, "Multiply the maximum condition by this value")]
        public float HealthMultiplier
        {
            get => healthMultiplier;
            set 
            {
                float prevConditionPercentage = ConditionPercentage;
                healthMultiplier = MathHelper.Clamp(value, 0.0f, float.PositiveInfinity);
                condition = MaxCondition * prevConditionPercentage / 100.0f;
                RecalculateConditionValues();
            }
        }

        private float maxRepairConditionMultiplier = 1.0f;

        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float MaxRepairConditionMultiplier
        {
            get => maxRepairConditionMultiplier;
            set 
            { 
                maxRepairConditionMultiplier = MathHelper.Clamp(value, 0.0f, float.PositiveInfinity);
                RecalculateConditionValues();
            }
        }
        
        //the default value should be Prefab.Health, but because we can't use it in the attribute, 
        //we'll just use NaN (which does nothing) and set the default value in the constructor/load
        [Serialize(float.NaN, IsPropertySaveable.No), Editable]
        public float Condition
        {
            get { return condition; }
            set 
            {
                SetCondition(value, isNetworkEvent: false);
            }
        }

        private double ConditionLastUpdated { get; set; }
        private float LastConditionChange { get; set; }
        /// <summary>
        /// Return true if the condition of this item increased within the last second.
        /// </summary>
        public bool ConditionIncreasedRecently => (Timing.TotalTime < ConditionLastUpdated + 1.0f) && LastConditionChange > 0.0f;

        public float Health
        {
            get { return condition; }
        }

        private bool? indestructible;
        /// <summary>
        /// Per-instance value - if not set, the value of the prefab is used.
        /// </summary>
        public bool Indestructible
        {
            get => indestructible ?? Prefab.Indestructible;
            set => indestructible = value;
        }

        public bool AllowDeconstruct
        {
            get;
            set;
        }

        [Editable, Serialize(false, isSaveable: IsPropertySaveable.Yes, "When enabled will prevent the item from taking damage from all sources")]
        public bool InvulnerableToDamage { get; set; }

        public bool StolenDuringRound;

        private bool spawnedInCurrentOutpost;
        public bool SpawnedInCurrentOutpost
        {
            get { return spawnedInCurrentOutpost; }
            set
            {
                if (!spawnedInCurrentOutpost && value)
                {
                    OriginalOutpost = GameMain.GameSession?.StartLocation?.BaseName ?? "";
                }
                spawnedInCurrentOutpost = value;
            }
        }

        [Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool AllowStealing
        {
            get;
            set;
        }

        private string originalOutpost;
        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public string OriginalOutpost
        {
            get { return originalOutpost; }
            set
            {
                originalOutpost = value;
                if (!string.IsNullOrEmpty(value) && GameMain.GameSession?.LevelData?.Type == LevelData.LevelType.Outpost && GameMain.GameSession?.StartLocation?.BaseName == value)
                {
                    spawnedInCurrentOutpost = true;
                }
            }
        }

        [Editable, Serialize("", IsPropertySaveable.Yes)]
        public string Tags
        {
            get { return string.Join(",", tags); }
            set
            {
                tags.Clear();
                // Always add prefab tags
                base.Prefab.Tags.ForEach(t => tags.Add(t));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string[] splitTags = value.Split(',');
                    foreach (string tag in splitTags)
                    {
                        string[] splitTag = tag.Trim().Split(':');
                        splitTag[0] = splitTag[0].ToLowerInvariant();
                        tags.Add(string.Join(":", splitTag).ToIdentifier());
                    }
                }
            }
        }

        public bool FireProof
        {
            get { return Prefab.FireProof; }
        }

        public bool WaterProof
        {
            get { return Prefab.WaterProof; }
        }

        public bool UseInHealthInterface
        {
            get { return Prefab.UseInHealthInterface; }
        }

        public int Quality
        {
            get
            {
                return qualityComponent?.QualityLevel ?? 0;
            }
            set
            {
                if (qualityComponent != null)
                {
                    qualityComponent.QualityLevel = value;
                }
            }
        }

        public bool InWater
        {
            get 
            {
                //if the item has an active physics body, inWater is updated in the Update method
                if (body != null && body.Enabled) { return inWater; }
                if (hasWaterStatusEffects) { return inWater; }

                //if not, we'll just have to check
                return IsInWater();
            }
        }

        /// <summary>
        /// A list of connections the last signal sent by this item went through
        /// </summary>
        public List<Connection> LastSentSignalRecipients
        {
            get;
            private set;
        } = new List<Connection>(20);

        public ContentPath ConfigFilePath => Prefab.ContentFile.Path;

        //which type of inventory slots (head, torso, any, etc) the item can be placed in
        private readonly HashSet<InvSlotType> allowedSlots = new HashSet<InvSlotType>();
        public IEnumerable<InvSlotType> AllowedSlots
        {
            get
            {
                return allowedSlots;
            }
        }

        public List<Connection> Connections
        {
            get
            {
                ConnectionPanel panel = GetComponent<ConnectionPanel>();
                if (panel == null) return null;
                return panel.Connections;
            }
        }

        public IEnumerable<Item> ContainedItems
        {
            get
            {
                return ownInventory?.AllItems ?? Enumerable.Empty<Item>();
            }
        }

        public ItemInventory OwnInventory
        {
            get { return ownInventory; }
        }

        [Editable, Serialize(false, IsPropertySaveable.Yes, description:
            "Enable if you want to display the item HUD side by side with another item's HUD, when linked together. " +
            "Disclaimer: It's possible or even likely that the views block each other, if they were not designed to be viewed together!")]
        public bool DisplaySideBySideWhenLinked { get; set; }

        public List<Repairable> Repairables
        {
            get { return repairables; }
        }

        public List<ItemComponent> Components
        {
            get { return components; }
        }

        public override bool Linkable
        {
            get { return Prefab.Linkable; }
        }

        /// <summary>
        /// Can be used to move the item from XML (e.g. to correct the positions of items whose sprite origin has been changed)
        /// </summary>
        public float PositionX
        {
            get { return Position.X; }
            private set
            {
                Move(new Vector2(value * Scale, 0.0f));
            }
        }
        /// <summary>
        /// Can be used to move the item from XML (e.g. to correct the positions of items whose sprite origin has been changed)
        /// </summary>
        public float PositionY
        {
            get { return Position.Y; }
            private set
            {
                Move(new Vector2(0.0f, value * Scale));
            }
        }

        public BallastFloraBranch Infector { get; set; }

        public ItemPrefab PendingItemSwap { get; set; }

        public readonly HashSet<ItemPrefab> AvailableSwaps = new HashSet<ItemPrefab>();

        public override string ToString()
        {
            return Name + " (ID: " + ID + ")";
        }

        private readonly List<ISerializableEntity> allPropertyObjects = new List<ISerializableEntity>();
        public IReadOnlyList<ISerializableEntity> AllPropertyObjects
        {
            get { return allPropertyObjects; }
        }
        
        public bool IgnoreByAI(Character character) => HasTag("ignorebyai") || OrderedToBeIgnored && character.IsOnPlayerTeam;
        public bool OrderedToBeIgnored { get; set; }

        public bool HasBallastFloraInHull
        {
            get
            {
                return CurrentHull?.BallastFlora != null;
            }
        }

        public bool IsClaimedByBallastFlora
        {
            get
            {
                if (CurrentHull?.BallastFlora == null) { return false; }
                return CurrentHull.BallastFlora.ClaimedTargets.Contains(this);
            }
        }

        public Item(ItemPrefab itemPrefab, Vector2 position, Submarine submarine, ushort id = Entity.NullEntityID, bool callOnItemLoaded = true)
            : this(new Rectangle(
                (int)(position.X - itemPrefab.Sprite.size.X / 2 * itemPrefab.Scale), 
                (int)(position.Y + itemPrefab.Sprite.size.Y / 2 * itemPrefab.Scale), 
                (int)(itemPrefab.Sprite.size.X * itemPrefab.Scale), 
                (int)(itemPrefab.Sprite.size.Y * itemPrefab.Scale)), 
                itemPrefab, submarine, callOnItemLoaded, id: id)
        {

        }

        /// <summary>
        /// Creates a new item
        /// </summary>
        /// <param name="callOnItemLoaded">Should the OnItemLoaded methods of the ItemComponents be called. Use false if the item needs additional initialization before it can be considered fully loaded (e.g. when loading an item from a sub file or cloning an item).</param>
        public Item(Rectangle newRect, ItemPrefab itemPrefab, Submarine submarine, bool callOnItemLoaded = true, ushort id = Entity.NullEntityID)
            : base(itemPrefab, submarine, id)
        {
            spriteColor = base.Prefab.SpriteColor;

            components          = new List<ItemComponent>();
            drawableComponents  = new List<IDrawableComponent>(); hasComponentsToDraw = false;
            tags                = new HashSet<Identifier>();
            repairables         = new List<Repairable>();

            defaultRect = newRect;
            rect = newRect;

            condition = MaxCondition =  Prefab.Health;
            ConditionPercentage = 100.0f;
           
            lastSentCondition = condition;

            AllowDeconstruct = itemPrefab.AllowDeconstruct;

            allPropertyObjects.Add(this);

            ContentXElement element = itemPrefab.ConfigElement;
            if (element == null) return;

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            if (submarine == null || !submarine.Loading) { FindHull(); }

            SetActiveSprite();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "body":
                        float density = subElement.GetAttributeFloat("density", 10.0f);
                        float minDensity = subElement.GetAttributeFloat("mindensity", density);
                        float maxDensity = subElement.GetAttributeFloat("maxdensity", density);
                        if (minDensity < maxDensity)
                        {
                            var rand = new Random(ID);
                            density = MathHelper.Lerp(minDensity, maxDensity, (float)rand.NextDouble());
                        }

                        string collisionCategoryStr = subElement.GetAttributeString("collisioncategory", null);

                        Category collisionCategory = Physics.CollisionItem;
                        Category collidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform;
                        if ((Prefab.DamagedByProjectiles || Prefab.DamagedByMeleeWeapons) && Condition > 0)
                        {
                            //force collision category to Character to allow projectiles and weapons to hit
                            //(we could also do this by making the projectiles and weapons hit CollisionItem
                            //and check if the collision should be ignored in the OnCollision callback, but
                            //that'd make the hit detection more expensive because every item would be included)
                            collisionCategory = Physics.CollisionCharacter;
                        }
                        if (collisionCategoryStr != null)
                        {                            
                            if (!Physics.TryParseCollisionCategory(collisionCategoryStr, out Category cat))
                            {
                                DebugConsole.ThrowError("Invalid collision category in item \"" + Name+"\" (" + collisionCategoryStr + ")");
                            }
                            else
                            {
                                collisionCategory = cat;
                                if (cat.HasFlag(Physics.CollisionCharacter))
                                {
                                    collisionCategory |= Physics.CollisionProjectile;
                                }
                            }
                        }
                        body = new PhysicsBody(subElement, ConvertUnits.ToSimUnits(Position), Scale, density, collisionCategory, collidesWith, findNewContacts: false);
                        body.FarseerBody.AngularDamping = subElement.GetAttributeFloat("angulardamping", 0.2f);
                        body.FarseerBody.LinearDamping = subElement.GetAttributeFloat("lineardamping", 0.1f);
                        body.UserData = this;
                        break;
                    case "trigger":
                    case "inventoryicon":
                    case "sprite":
                    case "deconstruct":
                    case "brokensprite":
                    case "decorativesprite":
                    case "upgradepreviewsprite":
                    case "price":
                    case "levelcommonness":
                    case "suitabletreatment":
                    case "containedsprite":
                    case "fabricate":
                    case "fabricable":
                    case "fabricableitem":
                    case "upgrade":
                    case "preferredcontainer":
                    case "upgrademodule":
                    case "upgradeoverride":
                    case "minimapicon":
                    case "infectedsprite":
                    case "damagedinfectedsprite":
                    case "swappableitem":
                        break;
                    case "staticbody":
                        StaticBodyConfig = subElement;
                        break;
                    case "aitarget":
                        aiTarget = new AITarget(this, subElement);
                        break;
                    default:
                        ItemComponent ic = ItemComponent.Load(subElement, this);
                        if (ic == null) break;

                        AddComponent(ic);

                        if (ic is IDrawableComponent && ic.Drawable)
                        {
                            drawableComponents.Add(ic as IDrawableComponent);
                            hasComponentsToDraw = true;
                        }
                        if (ic is Repairable) repairables.Add((Repairable)ic);
                        break;
                }
            }

            hasStatusEffectsOfType = new bool[Enum.GetValues(typeof(ActionType)).Length];
            foreach (ItemComponent ic in components)
            {
                if (ic is Pickable pickable)
                {
                    foreach (var allowedSlot in pickable.AllowedSlots)
                    {
                        allowedSlots.Add(allowedSlot);
                    }
                }

                if (ic.statusEffectLists == null) { continue; }

                if (statusEffectLists == null)
                {
                    statusEffectLists = new Dictionary<ActionType, List<StatusEffect>>();
                }

                //go through all the status effects of the component 
                //and add them to the corresponding statuseffect list
                foreach (List<StatusEffect> componentEffectList in ic.statusEffectLists.Values)
                {
                    ActionType actionType = componentEffectList.First().type;
                    if (!statusEffectLists.TryGetValue(actionType, out List<StatusEffect> statusEffectList))
                    {
                        statusEffectList = new List<StatusEffect>();
                        statusEffectLists.Add(actionType, statusEffectList);
                        hasStatusEffectsOfType[(int)actionType] = true;
                    }

                    foreach (StatusEffect effect in componentEffectList)
                    {
                        statusEffectList.Add(effect);
                    }
                }
            }

            hasWaterStatusEffects = hasStatusEffectsOfType[(int)ActionType.InWater] || hasStatusEffectsOfType[(int)ActionType.NotInWater];

            if (body != null)
            {
                body.Submarine = submarine;
            }

            //cache connections into a dictionary for faster lookups
            var connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel != null)
            {
                connections = new Dictionary<string, Connection>();
                foreach (Connection c in connectionPanel.Connections)
                {
                    if (!connections.ContainsKey(c.Name))
                        connections.Add(c.Name, c);
                }
            }

            if (body != null)
            {
                body.FarseerBody.OnCollision += OnCollision;
            }

            var itemContainer = GetComponent<ItemContainer>();
            if (itemContainer != null)
            {
                ownInventory = itemContainer.Inventory;
            }

            qualityComponent = GetComponent<Quality>();

            InitProjSpecific();

            if (callOnItemLoaded)
            {
                foreach (ItemComponent ic in components)
                {
                    ic.OnItemLoaded();
                }
            }

            InsertToList();
            ItemList.Add(this);

            DebugConsole.Log("Created " + Name + " (" + ID + ")");

            if (Components.Any(ic => ic is Wire) && Components.All(ic => ic is Wire || ic is Holdable)) { isWire = true; }
            if (HasTag("logic")) { isLogic = true; }

            ApplyStatusEffects(ActionType.OnSpawn, 1.0f);
            Components.ForEach(c => c.ApplyStatusEffects(ActionType.OnSpawn, 1.0f));
            RecalculateConditionValues();
        }

        partial void InitProjSpecific();

        public bool IsContainerPreferred(ItemContainer container, out bool isPreferencesDefined, out bool isSecondary, bool requireConditionRestriction = false)
            => Prefab.IsContainerPreferred(this, container, out isPreferencesDefined, out isSecondary, requireConditionRestriction);

        public override MapEntity Clone()
        {
            Item clone = new Item(rect, Prefab, Submarine, callOnItemLoaded: false)
            {
                defaultRect = defaultRect
            };
            foreach (KeyValuePair<Identifier, SerializableProperty> property in SerializableProperties)
            {
                if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                clone.SerializableProperties[property.Key].TrySetValue(clone, property.Value.GetValue(this));
            }

            if (components.Count != clone.components.Count)
            {
                string errorMsg = "Error while cloning item \"" + Name + "\" - clone does not have the same number of components. ";
                errorMsg += "Original components: " + string.Join(", ", components.Select(c => c.GetType().ToString()));
                errorMsg += ", cloned components: " + string.Join(", ", clone.components.Select(c => c.GetType().ToString()));
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.Clone:" + Name, GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
            }

            for (int i = 0; i < components.Count && i < clone.components.Count; i++)
            {
                foreach (KeyValuePair<Identifier, SerializableProperty> property in components[i].SerializableProperties)
                {
                    if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                    clone.components[i].SerializableProperties[property.Key].TrySetValue(clone.components[i], property.Value.GetValue(components[i]));
                }

                //clone requireditem identifiers
                foreach (var kvp in components[i].requiredItems)
                {
                    for (int j = 0; j < kvp.Value.Count; j++)
                    {
                        if (!clone.components[i].requiredItems.ContainsKey(kvp.Key) ||
                            clone.components[i].requiredItems[kvp.Key].Count <= j)
                        {
                            continue;
                        }

                        clone.components[i].requiredItems[kvp.Key][j].JoinedIdentifiers = 
                            kvp.Value[j].JoinedIdentifiers;
                    }
                }
            }

            if (FlippedX) clone.FlipX(false);
            if (FlippedY) clone.FlipY(false);
            
            foreach (ItemComponent component in clone.components)
            {
                component.OnItemLoaded();
            }

            foreach (Item containedItem in ContainedItems)
            {
                var containedClone = containedItem.Clone();
                clone.ownInventory.TryPutItem(containedClone as Item, null);
            }
                        
            return clone;
        }

        public void AddComponent(ItemComponent component)
        {
            allPropertyObjects.Add(component);
            components.Add(component);

            if (component.IsActive || component.UpdateWhenInactive || component.Parent != null || (component.IsActiveConditionals != null && component.IsActiveConditionals.Any()))
            {
                updateableComponents.Add(component);
            }

            component.OnActiveStateChanged += (bool isActive) => 
            {
                bool hasSounds = false;
#if CLIENT
                hasSounds = component.HasSounds;
#endif
                //component doesn't need to be updated if it isn't active, doesn't have a parent that could activate it, 
                //nor status effects, sounds or conditionals that would need to run
                if (!isActive && !component.UpdateWhenInactive && 
                    !hasSounds &&
                    component.Parent == null &&
                    (component.IsActiveConditionals == null || !component.IsActiveConditionals.Any()) &&
                    (component.statusEffectLists == null || !component.statusEffectLists.Any()))
                {
                    if (updateableComponents.Contains(component)) { updateableComponents.Remove(component); }
                }
                else
                {
                    if (!updateableComponents.Contains(component)) 
                    { 
                        updateableComponents.Add(component);
                        this.isActive = true;
                    }
                }
            };

            Type type = component.GetType();
            if (!componentsByType.ContainsKey(type))
            {
                componentsByType.Add(type, component);
                Type baseType = type.BaseType;
                while (baseType != null && baseType != typeof(ItemComponent))
                {
                    if (!componentsByType.ContainsKey(baseType))
                    {
                        componentsByType.Add(baseType, component);
                    }
                    baseType = baseType.BaseType;
                }
            }
        }

        public void EnableDrawableComponent(IDrawableComponent drawable)
        {
            if (!drawableComponents.Contains(drawable))
            {
                drawableComponents.Add(drawable);
                hasComponentsToDraw = true;
#if CLIENT
                cachedVisibleExtents = null;
#endif
            }
        }

        public void DisableDrawableComponent(IDrawableComponent drawable)
        {
            if (drawableComponents.Contains(drawable))
            {
                drawableComponents.Remove(drawable);
                hasComponentsToDraw = drawableComponents.Count > 0;
#if CLIENT
                cachedVisibleExtents = null;
#endif
            }
        }

        public int GetComponentIndex(ItemComponent component)
        {
            return components.IndexOf(component);
        }

        public T GetComponent<T>() where T : ItemComponent
        {
            if (componentsByType.TryGetValue(typeof(T), out ItemComponent component))
            {
                return (T)component;
            }
            if (typeof(T) == typeof(ItemComponent))
            {
                return (T)components.FirstOrDefault();
            }            
            return default;
        }

        public IEnumerable<T> GetComponents<T>()
        {
            if (typeof(T) == typeof(ItemComponent))
            {
                return components.Cast<T>();
            }
            if (!componentsByType.ContainsKey(typeof(T))) { return Enumerable.Empty<T>(); }
            return components.Where(c => c is T).Cast<T>();
        }

        public float GetQualityModifier(Quality.StatType statType)
        {
            return GetComponent<Quality>()?.GetValue(statType) ?? 0.0f;
        }

        public void RemoveContained(Item contained)
        {
            ownInventory?.RemoveItem(contained);
            contained.Container = null;            
        }

        public void SetTransform(Vector2 simPosition, float rotation, bool findNewHull = true, bool setPrevTransform = true)
        {
            if (!MathUtils.IsValid(simPosition))
            {
                string errorMsg =
                    "Attempted to move the item " + Name +
                    " to an invalid position (" + simPosition + ")\n" + Environment.StackTrace.CleanupStackTrace();

                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "Item.SetPosition:InvalidPosition" + ID,
                    GameAnalyticsManager.ErrorSeverity.Error,
                    errorMsg);
                return;
            }

            if (body != null)
            {
#if DEBUG
                try
                {
#endif
                    if (body.PhysEnabled)
                    {
                        body.SetTransform(simPosition, rotation, setPrevTransform);
                    }
                    else
                    {
                        body.SetTransformIgnoreContacts(simPosition, rotation, setPrevTransform);
                    }
#if DEBUG
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set item transform", e);
                }
#endif
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(simPosition);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            if (findNewHull) { FindHull(); }
        }

        /// <summary>
        /// Is dropping the item allowed when trying to swap it with the other item
        /// </summary>
        public bool AllowDroppingOnSwapWith(Item otherItem)
        {
            if (!Prefab.AllowDroppingOnSwap || otherItem == null) { return false; }
            if (Prefab.AllowDroppingOnSwapWith.Any())
            {
                foreach (Identifier tagOrIdentifier in Prefab.AllowDroppingOnSwapWith)
                {
                    if (otherItem.Prefab.Identifier == tagOrIdentifier) { return true; }
                    if (otherItem.HasTag(tagOrIdentifier)) { return true; }
                }
                return false;
            }
            else
            {
                return true;
            }
        }

        public void SetActiveSprite()
        {
            SetActiveSpriteProjSpecific();
        }

        partial void SetActiveSpriteProjSpecific();

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (!MathUtils.IsValid(amount))
            {
                DebugConsole.ThrowError($"Attempted to move an item by an invalid amount ({amount})\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            base.Move(amount);

            if (ItemList != null && body != null)
            {
                if (ignoreContacts)
                {
                    body.SetTransformIgnoreContacts(body.SimPosition + ConvertUnits.ToSimUnits(amount), body.Rotation);
                }
                else
                {
                    body.SetTransform(body.SimPosition + ConvertUnits.ToSimUnits(amount), body.Rotation);
                }
            }
            foreach (ItemComponent ic in components)
            {
                ic.Move(amount, ignoreContacts);
            }

            if (body != null && (Submarine == null || !Submarine.Loading)) { FindHull(); }
        }

        public Rectangle TransformTrigger(Rectangle trigger, bool world = false)
        {
            Rectangle baseRect = world ? WorldRect : Rect;

            Rectangle transformedRect =
                new Rectangle(
                    (int)(baseRect.X + trigger.X * Scale),
                    (int)(baseRect.Y + trigger.Y * Scale),
                    (trigger.Width == 0) ? Rect.Width : (int)(trigger.Width * Scale),
                    (trigger.Height == 0) ? Rect.Height : (int)(trigger.Height * Scale));

            if (FlippedX)
            {
                transformedRect.X = baseRect.X + (baseRect.Right - transformedRect.Right);
            }
            if (FlippedY)
            {
                transformedRect.Y = baseRect.Y + ((baseRect.Y - baseRect.Height) - (transformedRect.Y - transformedRect.Height));
            }

            return transformedRect;
        }

        /// <summary>
        /// goes through every item and re-checks which hull they are in
        /// </summary>
        public static void UpdateHulls()
        {
            foreach (Item item in ItemList)
            {
                item.FindHull();
            }
        }
        
        public Hull FindHull()
        {
            if (parentInventory != null && parentInventory.Owner != null)
            {
                if (parentInventory.Owner is Character character)
                {
                    CurrentHull = character.AnimController.CurrentHull;
                }
                else if (parentInventory.Owner is Item item)
                {
                    CurrentHull = item.CurrentHull;
                }

                Submarine = parentInventory.Owner.Submarine;
                if (body != null) { body.Submarine = Submarine; }

                return CurrentHull;
            }

            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull);
            if (body != null && body.Enabled && (body.BodyType == BodyType.Dynamic || Submarine == null))
            {
                Submarine = CurrentHull?.Submarine;
                body.Submarine = Submarine;
            }

            return CurrentHull;
        }

        public Item GetRootContainer()
        {
            if (Container == null) { return null; }
            Item rootContainer = Container;
            while (rootContainer.Container != null)
            {
                rootContainer = rootContainer.Container;
            }
            return rootContainer;
        }
        
        public bool HasAccess(Character character)
        {
            if (character.IsBot && IgnoreByAI(character)) { return false; }
            if (!IsInteractable(character)) { return false; }
            var itemContainer = GetComponent<ItemContainer>();
            if (itemContainer != null && !itemContainer.HasAccess(character)) { return false; }
            if (Container != null && !Container.HasAccess(character)) { return false; }
            return true;
        }

        public bool IsOwnedBy(Entity entity) => FindParentInventory(i => i.Owner == entity) != null;

        public Entity GetRootInventoryOwner()
        {
            if (ParentInventory == null) { return this; }
            if (ParentInventory.Owner is Character) { return ParentInventory.Owner; }
            var rootContainer = GetRootContainer();
            if (rootContainer?.ParentInventory?.Owner is Character) { return rootContainer.ParentInventory.Owner; }
            return rootContainer ?? this;
        }

        public Inventory FindParentInventory(Func<Inventory, bool> predicate)
        {
            if (parentInventory != null)
            {
                if (predicate(parentInventory))
                {
                    return parentInventory;
                }
                if (parentInventory.Owner is Item owner)
                {
                    return owner.FindParentInventory(predicate);
                }
            }
            return null;
        }

        public void SetContainedItemPositions()
        {
            foreach (ItemComponent component in components)
            {
                (component as ItemContainer)?.SetContainedItemPositions();
            }
        }
        
        public void AddTag(string tag)
        {
            AddTag(tag.ToIdentifier());
        }

        public void AddTag(Identifier tag)
        {
            if (tags.Contains(tag)) { return; }
            tags.Add(tag);
        }


        public bool HasTag(string tag)
        {
            return HasTag(tag.ToIdentifier());
        }

        public bool HasTag(Identifier tag)
        {
            if (tag == null) { return true; }
            return tags.Contains(tag) || base.Prefab.Tags.Contains(tag);
        }

        public void ReplaceTag(string tag, string newTag)
        {
            ReplaceTag(tag.ToIdentifier(), newTag.ToIdentifier());
        }

        public void ReplaceTag(Identifier tag, Identifier newTag)
        {
            if (!tags.Contains(tag)) { return; }
            tags.Remove(tag);
            tags.Add(newTag);
        }

        public IEnumerable<Identifier> GetTags()
        {
            return tags;
        }

        public bool HasTag(IEnumerable<Identifier> allowedTags)
        {
            if (allowedTags == null) return true;
            foreach (Identifier tag in allowedTags)
            {
                if (tags.Contains(tag)) return true;
            }
            return false;
        }

        private bool ConditionalMatches(PropertyConditional conditional)
        {
            if (string.IsNullOrEmpty(conditional.TargetItemComponentName))
            {
                if (!conditional.Matches(this)) { return false; }
            }
            else
            {
                foreach (ItemComponent component in components)
                {
                    if (component.Name != conditional.TargetItemComponentName) { continue; }
                    if (!conditional.Matches(component)) { return false; }
                }
            }
            return true;
        }

        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb limb = null, Entity useTarget = null, bool isNetworkEvent = false, Vector2? worldPosition = null)
        {
            if (!hasStatusEffectsOfType[(int)type]) { return; }

            foreach (StatusEffect effect in statusEffectLists[type])
            {
                ApplyStatusEffect(effect, type, deltaTime, character, limb, useTarget, isNetworkEvent, false, worldPosition);
            }
        }
        
        readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        public void ApplyStatusEffect(StatusEffect effect, ActionType type, float deltaTime, Character character = null, Limb limb = null, Entity useTarget = null, bool isNetworkEvent = false, bool checkCondition = true, Vector2? worldPosition = null)
        {
            if (!isNetworkEvent && checkCondition)
            {
                if (condition == 0.0f && !effect.AllowWhenBroken && effect.type != ActionType.OnBroken) { return; }
            }
            if (effect.type != type) { return; }
            
            bool hasTargets = effect.TargetIdentifiers == null;

            targets.Clear();

            if (effect.HasTargetType(StatusEffect.TargetType.Contained))
            {
                foreach (Item containedItem in ContainedItems)
                {
                    if (effect.TargetIdentifiers != null &&
                        !effect.TargetIdentifiers.Contains(((MapEntity)containedItem).Prefab.Identifier) &&
                        !effect.TargetIdentifiers.Any(id => containedItem.HasTag(id)))
                    {
                        continue;
                    }

                    if (effect.TargetSlot > -1)
                    {
                        if (OwnInventory.FindIndex(containedItem) != effect.TargetSlot) { continue; }
                    }

                    hasTargets = true;
                    targets.Add(containedItem);
                }
            }

            if (effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters) || effect.HasTargetType(StatusEffect.TargetType.NearbyItems))
            {
                targets.AddRange(effect.GetNearbyTargets(WorldPosition, targets));
                if (targets.Count > 0)
                {
                    hasTargets = true;
                }
            }

            if (effect.HasTargetType(StatusEffect.TargetType.UseTarget) && useTarget is ISerializableEntity serializableTarget)
            {
                hasTargets = true;
                targets.Add(serializableTarget);
            }

            if (!hasTargets) { return; }

            if (effect.HasTargetType(StatusEffect.TargetType.Hull) && CurrentHull != null)
            {
                targets.Add(CurrentHull);
            }

            if (effect.HasTargetType(StatusEffect.TargetType.This))
            {
                foreach (var pobject in AllPropertyObjects)
                {
                    targets.Add(pobject);
                }
            }

            if (character != null)
            {
                if (effect.HasTargetType(StatusEffect.TargetType.Character))
                {
                    if (type == ActionType.OnContained && ParentInventory is CharacterInventory characterInventory)
                    {
                        targets.Add(characterInventory.Owner as ISerializableEntity);
                    }
                    else
                    {
                        targets.Add(character);
                    }
                }
                if (effect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                {
                    targets.AddRange(character.AnimController.Limbs.ToList());
                }
            }
            if (effect.HasTargetType(StatusEffect.TargetType.Limb))
            {
                targets.Add(limb);
            }

            if (Container != null && effect.HasTargetType(StatusEffect.TargetType.Parent)) { targets.Add(Container); }
            
            effect.Apply(type, deltaTime, this, targets, worldPosition);            
        }


        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = true)
        {
            if (Indestructible || InvulnerableToDamage) { return new AttackResult(); }

            float damageAmount = attack.GetItemDamage(deltaTime);
            Condition -= damageAmount;

            if (damageAmount >= Prefab.OnDamagedThreshold)
            {
                ApplyStatusEffects(ActionType.OnDamaged, 1.0f);
            }

            return new AttackResult(damageAmount, null);
        }

        private void SetCondition(float value, bool isNetworkEvent)
        {
            if (!isNetworkEvent)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            }
            if (!MathUtils.IsValid(value)) { return; }
            if (Indestructible) { return; }
            if (InvulnerableToDamage && value <= condition) { return; }

            float prev = condition;
            bool wasInFullCondition = IsFullCondition;

            condition = MathHelper.Clamp(value, 0.0f, MaxCondition);
            if (MathUtils.NearlyEqual(prev, condition, epsilon: 0.000001f)) { return; }

            RecalculateConditionValues();

            if (condition == 0.0f && prev > 0.0f)
            {
                //Flag connections to be updated as device is broken
                flagChangedConnections(connections);
#if CLIENT
                foreach (ItemComponent ic in components)
                {
                    ic.PlaySound(ActionType.OnBroken);
                }
                if (Screen.Selected == GameMain.SubEditorScreen) { return; }
#endif
                ApplyStatusEffects(ActionType.OnBroken, 1.0f, null);
            }
            else if (condition > 0.0f && prev <= 0.0f)
            {
                //Flag connections to be updated as device is now working again
                flagChangedConnections(connections);
            }

            SetActiveSprite();

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                if (Math.Abs(lastSentCondition - condition) > 1.0f)
                {
                    conditionUpdatePending = true;
                    isActive = true;
                }
                else if (wasInFullCondition != IsFullCondition)
                {
                    conditionUpdatePending = true;
                    isActive = true;
                }
                else if (!MathUtils.NearlyEqual(lastSentCondition, condition) && (condition <= 0.0f || condition >= MaxCondition))
                {
                    sendConditionUpdateTimer = 0.0f;
                    conditionUpdatePending = true;
                    isActive = true;
                }
            }

            LastConditionChange = condition - prev;
            ConditionLastUpdated = Timing.TotalTime;

            static void flagChangedConnections(Dictionary<string, Connection> connections)
            {
                if (connections == null) { return; }
                foreach (Connection c in connections.Values)
                {
                    if (c.IsPower)
                    {
                        Powered.ChangedConnections.Add(c);
                        foreach (Connection conn in c.Recipients)
                        {
                            Powered.ChangedConnections.Add(conn);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recalculates the item's maximum condition, condition percentage and whether it's in full condition. 
        /// You generally never need to call this manually - done automatically when any of the factors that affect the values change.
        /// </summary>
        public void RecalculateConditionValues()
        {
            MaxCondition = Prefab.Health * healthMultiplier * maxRepairConditionMultiplier * (1.0f + GetQualityModifier(Items.Components.Quality.StatType.Condition));
            IsFullCondition = MathUtils.NearlyEqual(Condition, MaxCondition);
            ConditionPercentage = MathUtils.Percentage(Condition, MaxCondition);
        }

        private bool IsInWater()
        {
            if (CurrentHull == null) { return true; }
                        
            float surfaceY = CurrentHull.Surface;
            return CurrentHull.WaterVolume > 0.0f && Position.Y < surfaceY;
        }

        public void SendPendingNetworkUpdates()
        {
            if (!(GameMain.NetworkMember is { IsServer: true })) { return; }
            if (!conditionUpdatePending) { return; }

            CreateStatusEvent();
            lastSentCondition = condition;
            sendConditionUpdateTimer = NetConfig.ItemConditionUpdateInterval;
            conditionUpdatePending = false;
        }

        public void CreateStatusEvent()
        {
            GameMain.NetworkMember.CreateEntityEvent(this, new ItemStatusEventData());
        }

        private bool isActive = true;

        public override void Update(float deltaTime, Camera cam)
        {
            if (impactQueue != null)
            {
                while (impactQueue.TryDequeue(out float impact))
                {
                    HandleCollision(impact);
                }
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer && (!Submarine?.Loading ?? true))
            {
                sendConditionUpdateTimer -= deltaTime;
                if (conditionUpdatePending && sendConditionUpdateTimer <= 0.0f)
                {
                    SendPendingNetworkUpdates();
                }
            }

            if (aiTarget != null)
            {
                aiTarget.Update(deltaTime);
            }

            if (!isActive) { return; }

            ApplyStatusEffects(ActionType.Always, deltaTime, character: (parentInventory as CharacterInventory)?.Owner as Character);
            ApplyStatusEffects(parentInventory == null ? ActionType.OnNotContained : ActionType.OnContained, deltaTime, character: (parentInventory as CharacterInventory)?.Owner as Character);

            for (int i = 0; i < updateableComponents.Count; i++)
            {
                ItemComponent ic = updateableComponents[i];

                if (ic.IsActiveConditionals != null)
                {
                    bool shouldBeActive = true;
                    foreach (var conditional in ic.IsActiveConditionals)
                    {
                        if (!ConditionalMatches(conditional)) 
                        {
                            shouldBeActive = false;
                            break;
                        }
                    }
                    ic.IsActive = shouldBeActive;
                }
#if CLIENT
                if (ic.HasSounds)
                {
                    ic.PlaySound(ActionType.Always);
                    ic.UpdateSounds();
                    if (!ic.WasUsed) { ic.StopSounds(ActionType.OnUse); }
                    if (!ic.WasSecondaryUsed) { ic.StopSounds(ActionType.OnSecondaryUse); }
                }
#endif
                ic.WasUsed = false;
                ic.WasSecondaryUsed = false;

                if (ic.IsActive || ic.UpdateWhenInactive)
                {
                    if (condition <= 0.0f)
                    {
                        ic.UpdateBroken(deltaTime, cam);
                    }
                    else
                    {
                        ic.Update(deltaTime, cam);
#if CLIENT
                        if (ic.IsActive)
                        {
                            if (ic.IsActiveTimer > 0.02f)
                            {
                                ic.PlaySound(ActionType.OnActive);
                            }
                            ic.IsActiveTimer += deltaTime;
                        }
#endif
                    }
                }
            }

            if (Removed) { return; }

            bool needsWaterCheck = hasWaterStatusEffects;
            if (body != null && body.Enabled)
            {
                System.Diagnostics.Debug.Assert(body.FarseerBody.FixtureList != null);

                if (Math.Abs(body.LinearVelocity.X) > 0.01f || Math.Abs(body.LinearVelocity.Y) > 0.01f || transformDirty)
                {
                    UpdateTransform();
                    if (CurrentHull == null && Level.Loaded != null && body.SimPosition.Y < ConvertUnits.ToSimUnits(Level.MaxEntityDepth))
                    {
                        Spawner?.AddItemToRemoveQueue(this);
                        return;
                    }
                }
                needsWaterCheck = true;
                UpdateNetPosition(deltaTime);
                if (inWater)
                {
                    ApplyWaterForces();
                    CurrentHull?.ApplyFlowForces(deltaTime, this);
                }
            }

            if (needsWaterCheck)
            {
                inWater = IsInWater();
                bool waterProof = WaterProof;
                if (inWater)
                {
                    Item container = this.Container;
                    while (!waterProof && container != null)
                    {
                        waterProof = container.WaterProof;
                        container = container.Container;
                    }
                }
                if (hasWaterStatusEffects && condition > 0.0f)
                {
                    ApplyStatusEffects(!waterProof && inWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);
                }
            }
            else
            {
                if (updateableComponents.Count == 0 && !hasStatusEffectsOfType[(int)ActionType.Always] && (body == null || !body.Enabled))
                {
#if CLIENT
                    positionBuffer.Clear();
#endif
                    isActive = false;
                }
            }
        }

                
        public void UpdateTransform()
        {
            if (body == null) { return; }
            Submarine prevSub = Submarine;

            var projectile = GetComponent<Projectile>();
            if (projectile?.StickTarget != null)
            {
                if (projectile?.StickTarget.UserData is Limb limb && limb.character != null)
                {
                    Submarine = body.Submarine = limb.character.Submarine;
                    currentHull = limb.character.CurrentHull;
                }
                else if (projectile.StickTarget.UserData is Structure structure)
                {
                    Submarine = body.Submarine = structure.Submarine;
                    currentHull = Hull.FindHull(WorldPosition, CurrentHull);
                }
                else if (projectile.StickTarget.UserData is Item targetItem)
                {
                    Submarine = body.Submarine = targetItem.Submarine;
                    currentHull = targetItem.CurrentHull;
                }
                else if (projectile.StickTarget.UserData is Submarine)
                {
                    //attached to a sub from the outside -> don't move inside the sub
                    Submarine = body.Submarine = null;
                    currentHull = null;
                }
            }
            else
            {
                FindHull();
            }

            if (Submarine == null && prevSub != null)
            {
                body.SetTransform(body.SimPosition + prevSub.SimPosition, body.Rotation);
            }
            else if (Submarine != null && prevSub == null)
            {
                body.SetTransform(body.SimPosition - Submarine.SimPosition, body.Rotation);
            }
            else if (Submarine != null && prevSub != null && Submarine != prevSub)
            {
                body.SetTransform(body.SimPosition + prevSub.SimPosition - Submarine.SimPosition, body.Rotation);
            }

            if (Submarine != prevSub)
            {
                foreach (Item containedItem in ContainedItems)
                {
                    if (containedItem == null) { continue; }
                    containedItem.Submarine = Submarine;
                }
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            if (Math.Abs(body.LinearVelocity.X) > NetConfig.MaxPhysicsBodyVelocity || 
                Math.Abs(body.LinearVelocity.Y) > NetConfig.MaxPhysicsBodyVelocity)
            {
                body.LinearVelocity = new Vector2(
                    MathHelper.Clamp(body.LinearVelocity.X, -NetConfig.MaxPhysicsBodyVelocity, NetConfig.MaxPhysicsBodyVelocity),
                    MathHelper.Clamp(body.LinearVelocity.Y, -NetConfig.MaxPhysicsBodyVelocity, NetConfig.MaxPhysicsBodyVelocity));
            }

            transformDirty = false;
        }

        /// <summary>
        /// Applies buoyancy, drag and angular drag caused by water
        /// </summary>
        private void ApplyWaterForces()
        {
            if (body.Mass <= 0.0f || body.Density <= 0.0f)
            {
                return;
            }

            float forceFactor = 1.0f;
            if (CurrentHull != null)
            {
                float floor = CurrentHull.Rect.Y - CurrentHull.Rect.Height;
                float waterLevel = floor + CurrentHull.WaterVolume / CurrentHull.Rect.Width;

                //forceFactor is 1.0f if the item is completely submerged, 
                //and goes to 0.0f as the item goes through the surface
                forceFactor = Math.Min((waterLevel - Position.Y) / rect.Height, 1.0f);
                if (forceFactor <= 0.0f) return;
            }

            float volume = body.Mass / body.Density;

            var uplift = -GameMain.World.Gravity * forceFactor * volume;

            Vector2 drag = body.LinearVelocity * volume;

            body.ApplyForce((uplift - drag) * 10.0f);

            //apply simple angular drag
            body.ApplyTorque(body.AngularVelocity * volume * -0.05f);
        }


        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (transformDirty) { return false; }

            var projectile = GetComponent<Projectile>();
            if (projectile != null)
            {
                //ignore character colliders (a projectile only hits limbs)
                if (f2.CollisionCategories == Physics.CollisionCharacter && f2.Body.UserData is Character) { return false; }
                if (projectile.IgnoredBodies != null && projectile.IgnoredBodies.Contains(f2.Body)) { return false; }
                if (projectile.ShouldIgnoreSubmarineCollision(f2, contact)) { return false; }
            }

            contact.GetWorldManifold(out Vector2 normal, out _);
            if (contact.FixtureA.Body == f1.Body) { normal = -normal; }
            float impact = Vector2.Dot(f1.Body.LinearVelocity, -normal);

            impactQueue ??= new ConcurrentQueue<float>();
            impactQueue.Enqueue(impact);

            return true;
        }

        private void HandleCollision(float impact)
        {
            OnCollisionProjSpecific(impact);
            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            if (ImpactTolerance > 0.0f && condition > 0.0f && Math.Abs(impact) > ImpactTolerance)
            {
                ApplyStatusEffects(ActionType.OnImpact, 1.0f);
#if SERVER
                GameMain.Server?.CreateEntityEvent(this, new ApplyStatusEffectEventData(ActionType.OnImpact));
#endif
            }

            foreach (Item contained in ContainedItems)
            {
                if (contained.body != null) { contained.HandleCollision(impact); }
            }
        }

        partial void OnCollisionProjSpecific(float impact);

        public override void FlipX(bool relativeToSub)
        {
            //call the base method even if the item can't flip, to handle repositioning when flipping the whole sub
            base.FlipX(relativeToSub);

            if (!Prefab.CanFlipX) 
            {
                flippedX = false;
                return; 
            }

            if (Prefab.AllowRotatingInEditor)
            {
                RotationRad = MathUtils.WrapAngleTwoPi(-RotationRad);
            }
#if CLIENT
            if (Prefab.CanSpriteFlipX)
            {
                SpriteEffects ^= SpriteEffects.FlipHorizontally;
            }
#endif

            foreach (ItemComponent component in components)
            {
                component.FlipX(relativeToSub);
            }
            SetContainedItemPositions();
        }

        public override void FlipY(bool relativeToSub)
        {
            //call the base method even if the item can't flip, to handle repositioning when flipping the whole sub
            base.FlipY(relativeToSub);

            if (!Prefab.CanFlipY)
            {
                flippedY = false;
                return;
            }

#if CLIENT
            if (Prefab.CanSpriteFlipY)
            {
                SpriteEffects ^= SpriteEffects.FlipVertically;
            }
#endif

            foreach (ItemComponent component in components)
            {
                component.FlipY(relativeToSub);
            }
            SetContainedItemPositions();
        }

        /// <summary>
        /// Note: This function generates garbage and might be a bit too heavy to be used once per frame.
        /// </summary>
        public List<T> GetConnectedComponents<T>(bool recursive = false, bool allowTraversingBackwards = true) where T : ItemComponent
        {
            List<T> connectedComponents = new List<T>();

            if (recursive)
            {
                HashSet<Connection> alreadySearched = new HashSet<Connection>();
                GetConnectedComponentsRecursive(alreadySearched, connectedComponents, allowTraversingBackwards: allowTraversingBackwards);
                return connectedComponents;
            }

            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) { return connectedComponents; }

            foreach (Connection c in connectionPanel.Connections)
            {
                var recipients = c.Recipients;
                foreach (Connection recipient in recipients)
                {
                    var component = recipient.Item.GetComponent<T>();
                    if (component != null && !connectedComponents.Contains(component))
                    {
                        connectedComponents.Add(component);
                    } 
                }
            }

            return connectedComponents;
        }

        private void GetConnectedComponentsRecursive<T>(HashSet<Connection> alreadySearched, List<T> connectedComponents, bool ignoreInactiveRelays = false, bool allowTraversingBackwards = true) where T : ItemComponent
        {
            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) { return; }

            foreach (Connection c in connectionPanel.Connections)
            {
                if (alreadySearched.Contains(c)) { continue; }
                alreadySearched.Add(c);
                GetConnectedComponentsRecursive(c, alreadySearched, connectedComponents, ignoreInactiveRelays, allowTraversingBackwards);
            }
        }

        /// <summary>
        /// Note: This function generates garbage and might be a bit too heavy to be used once per frame.
        /// </summary>
        public List<T> GetConnectedComponentsRecursive<T>(Connection c, bool ignoreInactiveRelays = false, bool allowTraversingBackwards = true) where T : ItemComponent
        {
            List<T> connectedComponents = new List<T>();
            HashSet<Connection> alreadySearched = new HashSet<Connection>();
            GetConnectedComponentsRecursive(c, alreadySearched, connectedComponents, ignoreInactiveRelays, allowTraversingBackwards);

            return connectedComponents;
        }

        public static readonly ImmutableArray<(Identifier Input, Identifier Output)> connectionPairs = new (Identifier, Identifier)[]
        {
            ("power_in".ToIdentifier(), "power_out".ToIdentifier()),
            ("signal_in1".ToIdentifier(), "signal_out1".ToIdentifier()),
            ("signal_in2".ToIdentifier(), "signal_out2".ToIdentifier()),
            ("signal_in3".ToIdentifier(), "signal_out3".ToIdentifier()),
            ("signal_in4".ToIdentifier(), "signal_out4".ToIdentifier()),
            ("signal_in".ToIdentifier(), "signal_out".ToIdentifier()),
            ("signal_in1".ToIdentifier(), "signal_out".ToIdentifier()),
            ("signal_in2".ToIdentifier(), "signal_out".ToIdentifier())
        }.ToImmutableArray();

        private void GetConnectedComponentsRecursive<T>(Connection c, HashSet<Connection> alreadySearched, List<T> connectedComponents, bool ignoreInactiveRelays, bool allowTraversingBackwards = true) where T : ItemComponent
        {
            alreadySearched.Add(c);
                        
            var recipients = c.Recipients;
            foreach (Connection recipient in recipients)
            {
                if (alreadySearched.Contains(recipient)) { continue; }
                var component = recipient.Item.GetComponent<T>();                    
                if (component != null && !connectedComponents.Contains(component))
                {
                    connectedComponents.Add(component);
                }

                //connected to a wifi component -> see which other wifi components it can communicate with
                var wifiComponent = recipient.Item.GetComponent<WifiComponent>();
                if (wifiComponent != null && wifiComponent.CanTransmit())
                {
                    foreach (var wifiReceiver in wifiComponent.GetTransmittersInRange())
                    {
                        var receiverConnections = wifiReceiver.Item.Connections;
                        if (receiverConnections == null) { continue; }
                        foreach (Connection wifiOutput in receiverConnections)
                        {
                            if ((wifiOutput.IsOutput == recipient.IsOutput) || alreadySearched.Contains(wifiOutput)) { continue; }
                            GetConnectedComponentsRecursive(wifiOutput, alreadySearched, connectedComponents, ignoreInactiveRelays, allowTraversingBackwards);
                        }
                    }
                }

                recipient.Item.GetConnectedComponentsRecursive(recipient, alreadySearched, connectedComponents, ignoreInactiveRelays, allowTraversingBackwards);                   
            }

            if (ignoreInactiveRelays)
            {
                var relay = GetComponent<RelayComponent>();
                if (relay != null && !relay.IsOn) { return; }
            }

            foreach ((Identifier input, Identifier output) in connectionPairs)
            {
                void searchFromAToB(Identifier connectionEndA, Identifier connectionEndB)
                {
                    if (connectionEndA == c.Name)
                    {
                        var pairedConnection = c.Item.Connections.FirstOrDefault(c2 => c2.Name == connectionEndB);
                        if (pairedConnection != null)
                        {
                            if (alreadySearched.Contains(pairedConnection)) { return; }
                            GetConnectedComponentsRecursive(pairedConnection, alreadySearched, connectedComponents, ignoreInactiveRelays, allowTraversingBackwards);
                        }
                    }
                }
                searchFromAToB(input, output);
                if (allowTraversingBackwards) { searchFromAToB(output, input); }
            }
        }

        public Controller FindController(ImmutableArray<Identifier>? tags = null)
        {
            //try finding the controller with the simpler non-recursive method first
            var controllers = GetConnectedComponents<Controller>();
            bool needsTag = tags != null && tags.Value.Length > 0;
            if (controllers.None() || (needsTag && controllers.None(c => c.Item.HasTag(tags))))
            {
                controllers = GetConnectedComponents<Controller>(recursive: true);
            }
            if (needsTag)
            {
                controllers.RemoveAll(c => !c.Item.HasTag(tags));
            }
            return controllers.Count < 2 ?
                controllers.FirstOrDefault() :
                controllers.FirstOrDefault(c => c.GetFocusTarget() == this) ?? controllers.FirstOrDefault();
        }

        public bool TryFindController(out Controller controller, ImmutableArray<Identifier>? tags = null)
        {
            controller = FindController(tags: tags);
            return controller != null;
        }

        public void SendSignal(string signal, string connectionName)
        {
            SendSignal(new Signal(signal), connectionName);
        }

        public void SendSignal(Signal signal, string connectionName)
        {
            if (connections == null) { return; }
            if (!connections.TryGetValue(connectionName, out Connection connection)) { return; }

            signal.source ??= this;
            SendSignal(signal, connection);
        }

        private readonly HashSet<(Signal Signal, Connection Connection)> delayedSignals = new HashSet<(Signal Signal, Connection Connection)>();

        public void SendSignal(Signal signal, Connection connection)
        {
            LastSentSignalRecipients.Clear();
            if (connections == null || connection == null) { return; }

            signal.stepsTaken++;

            //if the signal has been passed through this item multiple times already, interrupt it to prevent infinite loops
            if (signal.stepsTaken > 5 && signal.source != null)
            {
                int duplicateRecipients = 0;
                foreach (var recipient in signal.source.LastSentSignalRecipients)
                {
                    if (recipient == connection)
                    {
                        duplicateRecipients++;
                        if (duplicateRecipients > 2) { return; }
                    }
                }
            }

            //use a coroutine to prevent infinite loops by creating a one 
            //frame delay if the "signal chain" gets too long
            if (signal.stepsTaken > 10)
            {
                //if there's an equal signal waiting to be sent
                //to the same connection, don't add a new one
                signal.stepsTaken = 0;
                bool duplicateFound = false;
                foreach (var s in delayedSignals)
                {
                    if (s.Connection == connection && s.Signal.source == signal.source && s.Signal.value == signal.value && s.Signal.sender == signal.sender)
                    {
                        duplicateFound = true;
                        break;
                    }
                }
                if (!duplicateFound)
                {
                    delayedSignals.Add((signal, connection));
                    CoroutineManager.StartCoroutine(DelaySignal(signal, connection));
                }
            }
            else
            {
                if (connection.Effects != null && signal.value != "0" && !string.IsNullOrEmpty(signal.value))
                {
                    foreach (StatusEffect effect in connection.Effects)
                    {
                        if (condition <= 0.0f && effect.type != ActionType.OnBroken) { continue; }
                        ApplyStatusEffect(effect, ActionType.OnUse, (float)Timing.Step);
                    }
                }
                signal.source ??= this;
                connection.SendSignal(signal);
            }
        }

        private IEnumerable<CoroutineStatus> DelaySignal(Signal signal, Connection connection)
        {
            do
            {
                //wait at least one frame
                yield return CoroutineStatus.Running;
            } while (CoroutineManager.DeltaTime <= 0.0f);

            delayedSignals.Remove((signal, connection));
            connection.SendSignal(signal);

            yield return CoroutineStatus.Success;
        }

        public bool IsInsideTrigger(Vector2 worldPosition)
        {
            return IsInsideTrigger(worldPosition, out _);
        }

        public bool IsInsideTrigger(Vector2 worldPosition, out Rectangle transformedTrigger)
        {
            foreach (Rectangle trigger in Prefab.Triggers)
            {
                transformedTrigger = TransformTrigger(trigger, true);
                if (Submarine.RectContains(transformedTrigger, worldPosition)) { return true; }
            }

            transformedTrigger = Rectangle.Empty;
            return false;
        }

        public bool CanClientAccess(Client c)
        {
            return c != null && c.Character != null && c.Character.CanInteractWith(this);
        }

        public bool TryInteract(Character user, bool ignoreRequiredItems = false, bool forceSelectKey = false, bool forceUseKey = false)
        {
            if (CampaignMode.BlocksInteraction(CampaignInteractionType)) 
            { 
                return false; 
            }

            bool picked = false, selected = false;
#if CLIENT
            bool hasRequiredSkills = true;
            Skill requiredSkill = null;
            float skillMultiplier = 1;
#endif
            if (!IsInteractable(user)) { return false; }
            foreach (ItemComponent ic in components)
            {
                bool pickHit = false, selectHit = false;
                if (user.IsKeyDown(InputType.Aim))
                {
                    pickHit = false;
                    selectHit = false;
                }
                else
                {
                    if (forceSelectKey)
                    {
                        if (ic.PickKey == InputType.Select)
                        {
                            pickHit = true;
                        }
                        if (ic.SelectKey == InputType.Select)
                        {
                            selectHit = true;
                        }
                    }
                    else if (forceUseKey)
                    {
                        if (ic.PickKey == InputType.Use)
                        {
                            pickHit = true;
                        }
                        if (ic.SelectKey == InputType.Use)
                        {
                            selectHit = true;
                        }
                    }
                    else
                    {
                        pickHit = user.IsKeyHit(ic.PickKey);
                        selectHit = user.IsKeyHit(ic.SelectKey);

#if CLIENT
                        //if the cursor is on a UI component, disable interaction with the left mouse button
                        //to prevent accidentally selecting items when clicking UI elements
                        if (user == Character.Controlled && GUI.MouseOn != null)
                        {
                            if (GameSettings.CurrentConfig.KeyMap.Bindings[ic.PickKey].MouseButton == 0)
                            {
                                pickHit = false;
                            }

                            if (GameSettings.CurrentConfig.KeyMap.Bindings[ic.SelectKey].MouseButton == 0)
                            {
                                selectHit = false;
                            }
                        }
#endif
                    }
                }
#if CLIENT
                //use the non-mouse interaction key (E on both default and legacy keybinds) in wiring mode
                //LMB is used to manipulate wires, so using E to select connection panels is much easier
                if (Screen.Selected == GameMain.SubEditorScreen && GameMain.SubEditorScreen.WiringMode)
                {
                    pickHit = selectHit = GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Use].MouseButton == MouseButton.None ?
                        user.IsKeyHit(InputType.Use) :
                        user.IsKeyHit(InputType.Select);
                }
#endif
                if (!pickHit && !selectHit) { continue; }
                
                bool showUiMsg = false;
#if CLIENT
                if (!ic.HasRequiredSkills(user, out Skill tempRequiredSkill)) { hasRequiredSkills = false; skillMultiplier = ic.GetSkillMultiplier(); }
                showUiMsg = user == Character.Controlled && Screen.Selected != GameMain.SubEditorScreen;
#endif
                if (!ignoreRequiredItems && !ic.HasRequiredItems(user, showUiMsg)) { continue; }
                if ((ic.CanBePicked && pickHit && ic.Pick(user)) ||
                    (ic.CanBeSelected && selectHit && ic.Select(user)))
                {
                    picked = true;
                    ic.ApplyStatusEffects(ActionType.OnPicked, 1.0f, user);
#if CLIENT
                    if (user == Character.Controlled) { GUI.ForceMouseOn(null); }
                    if (tempRequiredSkill != null) { requiredSkill = tempRequiredSkill; }
#endif
                    if (ic.CanBeSelected && !(ic is Door)) { selected = true; }
                }
            }

            if (!picked) { return false; }

            if (user != null)
            {
                if (user.SelectedConstruction == this)
                {
                    if (user.IsKeyHit(InputType.Select) || forceSelectKey)
                    {
                        user.SelectedConstruction = null;
                    }
                }
                else if (selected)
                {
                    user.SelectedConstruction = this;
                }
            }

#if CLIENT
            if (!hasRequiredSkills && Character.Controlled == user && Screen.Selected != GameMain.SubEditorScreen)
            {
                if (requiredSkill != null)
                {
                    GUI.AddMessage(TextManager.GetWithVariables("InsufficientSkills",
                        ("[requiredskill]", TextManager.Get("SkillName." + requiredSkill.Identifier), FormatCapitals.Yes),
                        ("[requiredlevel]", ((int)(requiredSkill.Level * skillMultiplier)).ToString(), FormatCapitals.No)), GUIStyle.Red);
                }
            }
#endif

            if (Container != null)
            {
                Container.RemoveContained(this);
            }

            return true;         
        }

        public float GetContainedItemConditionPercentage()
        {
            if (ownInventory == null) { return -1; }

            float condition = 0f;
            float maxCondition = 0f;
            foreach (Item item in ContainedItems)
            {
                condition += item.condition;
                maxCondition += item.MaxCondition;
            }
            if (maxCondition > 0.0f)
            {
                return condition / maxCondition;
            }            

            return -1;
        }

        public void Use(float deltaTime, Character character = null, Limb targetLimb = null)
        {
            if (RequireAimToUse && (character == null || !character.IsKeyDown(InputType.Aim)))
            {
                return;
            }

            if (condition == 0.0f) { return; }
        
            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = character == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(character, isControlled)) { continue; }
                if (ic.Use(deltaTime, character))
                {
                    ic.WasUsed = true;

#if CLIENT
                    ic.PlaySound(ActionType.OnUse, character);
#endif
    
                    ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, character, targetLimb);

                    if (ic.DeleteOnUse) { remove = true; }
                }
            }

            if (remove)
            {
                Spawner.AddItemToRemoveQueue(this);
            }
        }

        public void SecondaryUse(float deltaTime, Character character = null)
        {
            if (condition == 0.0f) { return; }

            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = character == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(character, isControlled)) { continue; }
                if (ic.SecondaryUse(deltaTime, character))
                {
                    ic.WasSecondaryUsed = true;

#if CLIENT
                    ic.PlaySound(ActionType.OnSecondaryUse, character);
#endif

                    ic.ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, character);

                    if (ic.DeleteOnUse) { remove = true; }
                }
            }

            if (remove)
            {
                Spawner.AddItemToRemoveQueue(this);
            }
        }

        public void ApplyTreatment(Character user, Character character, Limb targetLimb)
        {
            //can't apply treatment to dead characters
            if (character.IsDead) { return; }
            if (!UseInHealthInterface) { return; }

#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(this, new TreatmentEventData(character, targetLimb));
                return;
            }
#endif

            float applyOnSelfFraction = user?.GetStatValue(StatTypes.ApplyTreatmentsOnSelfFraction) ?? 0.0f;

            bool remove = false;
            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(user, addMessage: user == Character.Controlled)) { continue; }

                bool success = Rand.Range(0.0f, 0.5f) < ic.DegreeOfSuccess(user);
                ActionType actionType = success ? ActionType.OnUse : ActionType.OnFailure;

#if CLIENT
                ic.PlaySound(actionType, user);
#endif
                ic.WasUsed = true;
                ic.ApplyStatusEffects(actionType, 1.0f, character, targetLimb, user: user, applyOnUserFraction: applyOnSelfFraction);

                if (applyOnSelfFraction > 0.0f)
                {
                    //hacky af
                    ic.statusEffectLists.TryGetValue(actionType, out var effectList);
                    if (effectList != null)
                    {
                        effectList.ForEach(e => e.AfflictionMultiplier = applyOnSelfFraction);
                        ic.ApplyStatusEffects(actionType, 1.0f, user, targetLimb == null ? null : user.AnimController.GetLimb(targetLimb.type), user: user);
                        effectList.ForEach(e => e.AfflictionMultiplier = 1.0f);
                    }
                }

                if (GameMain.NetworkMember is { IsServer: true })
                {
                    GameMain.NetworkMember.CreateEntityEvent(this, new ApplyStatusEffectEventData(
                        actionType, ic, character, targetLimb));
                }

                if (ic.DeleteOnUse) { remove = true; }
            }

            if (user != null)
            {
                var abilityItem = new AbilityApplyTreatment(user, character, this);
                user.CheckTalents(AbilityEffectType.OnApplyTreatment, abilityItem);

            }

            if (remove) { Spawner?.AddItemToRemoveQueue(this); }
        }

        public bool Combine(Item item, Character user)
        {
            if (item == this) { return false; }
            bool isCombined = false;
            foreach (ItemComponent ic in components)
            {
                if (ic.Combine(item, user)) { isCombined = true; }
            }
#if CLIENT
            if (isCombined) { GameMain.Client?.CreateEntityEvent(this, new CombineEventData(item)); }
#endif
            return isCombined;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dropper">Character who dropped the item</param>
        /// <param name="createNetworkEvent">Should clients be notified of the item being dropped</param>
        /// <param name="setTransform">Should the transform of the physics body be updated. Only disable this if you're moving the item somewhere else / calling SetTransform manually immediately after dropping!</param>
        public void Drop(Character dropper, bool createNetworkEvent = true, bool setTransform = true)
        {
            if (createNetworkEvent)
            {
                if (parentInventory != null && !parentInventory.Owner.Removed && !Removed &&
                    GameMain.NetworkMember != null && (GameMain.NetworkMember.IsServer || Character.Controlled == dropper))
                {
                    parentInventory.CreateNetworkEvent();
                    //send frequent updates after the item has been dropped
                    PositionUpdateInterval = 0.0f;
                }
            }

            if (body != null)
            {
                isActive = true;
                body.Enabled = true;
                body.PhysEnabled = true;
                body.ResetDynamics();
                if (dropper != null)
                {
                    if (body.Removed)
                    {
                        DebugConsole.ThrowError(
                            "Failed to drop the item \"" + Name + "\" (body has been removed"
                            + (Removed ? ", item has been removed)" : ")"));
                    }
                    else if (setTransform)
                    {
                        body.SetTransform(dropper.SimPosition, 0.0f);
                    }
                }
            }

            foreach (ItemComponent ic in components) { ic.Drop(dropper); }
            
            if (Container != null)
            {
                if (setTransform)
                {
                    SetTransform(Container.SimPosition, 0.0f);
                }
                Container.RemoveContained(this);
                Container = null;
            }
            
            if (parentInventory != null)
            {
                parentInventory.RemoveItem(this);
                parentInventory = null;
            }

            SetContainedItemPositions();
        }

        public void Equip(Character character)
        {
            if (Removed)
            {
                DebugConsole.ThrowError($"Tried to equip a removed item ({Name}).\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            foreach (ItemComponent ic in components) { ic.Equip(character); }
        }

        public void Unequip(Character character)
        {
            foreach (ItemComponent ic in components) { ic.Unequip(character); }
        }

        public List<(object obj, SerializableProperty property)> GetProperties<T>()
        {
            List<(object obj, SerializableProperty property)> allProperties = new List<(object obj, SerializableProperty property)>();

            List<SerializableProperty> itemProperties = SerializableProperty.GetProperties<T>(this);
            foreach (var itemProperty in itemProperties)
            {
                allProperties.Add((this, itemProperty));
            }            
            foreach (ItemComponent ic in components)
            {
                List<SerializableProperty> componentProperties = SerializableProperty.GetProperties<T>(ic);
                foreach (var componentProperty in componentProperties)
                {
                    allProperties.Add((ic, componentProperty));
                }
            }
            return allProperties;
        }

        private void WritePropertyChange(IWriteMessage msg, ChangePropertyEventData extraData, bool inGameEditableOnly)
        {
            //ignoreConditions: true = include all ConditionallyEditable properties at this point,
            //to ensure client/server doesn't get any properties mixed up if there's some conditions that can vary between the server and the clients
            var allProperties = inGameEditableOnly ? GetInGameEditableProperties(ignoreConditions: true) : GetProperties<Editable>();
            SerializableProperty property = extraData.SerializableProperty;
            if (property != null)
            {
                var propertyOwner = allProperties.Find(p => p.property == property);
                if (allProperties.Count > 1)
                {
                    msg.Write((byte)allProperties.FindIndex(p => p.property == property));
                }

                object value = property.GetValue(propertyOwner.obj);
                if (value is string stringVal)
                {
                    msg.Write(stringVal);
                }
                else if (value is Identifier idValue)
                {
                    msg.Write(idValue);
                }
                else if (value is float floatVal)
                {
                    msg.Write(floatVal);
                }
                else if (value is int intVal)
                {
                    msg.Write(intVal);
                }
                else if (value is bool boolVal)
                {
                    msg.Write(boolVal);
                }
                else if (value is Color color)
                {
                    msg.Write(color.R);
                    msg.Write(color.G);
                    msg.Write(color.B);
                    msg.Write(color.A);
                }
                else if (value is Vector2 vector2)
                {
                    msg.Write(vector2.X);
                    msg.Write(vector2.Y);
                }
                else if (value is Vector3 vector3)
                {
                    msg.Write(vector3.X);
                    msg.Write(vector3.Y);
                    msg.Write(vector3.Z);
                }
                else if (value is Vector4 vector4)
                {
                    msg.Write(vector4.X);
                    msg.Write(vector4.Y);
                    msg.Write(vector4.Z);
                    msg.Write(vector4.W);
                }
                else if (value is Point point)
                {
                    msg.Write(point.X);
                    msg.Write(point.Y);
                }
                else if (value is Rectangle rect)
                {
                    msg.Write(rect.X);
                    msg.Write(rect.Y);
                    msg.Write(rect.Width);
                    msg.Write(rect.Height);
                }
                else if (value is Enum)
                {
                    msg.Write((int)value);
                }
                else if (value is string[] a)
                {
                    msg.Write(a.Length);
                    for (int i = 0; i < a.Length; i++)
                    {
                        msg.Write(a[i] ?? "");
                    }
                }
                else
                {
                    throw new NotImplementedException("Serializing item properties of the type \"" + value.GetType() + "\" not supported");
                }
            }
            else
            {
                throw new ArgumentException("Failed to write propery value - property \"" + (property == null ? "null" : property.Name) + "\" is not serializable.");
            }
        }

        private List<(object obj, SerializableProperty property)> GetInGameEditableProperties(bool ignoreConditions = false)
        {
            if (ignoreConditions)
            {
                return GetProperties<ConditionallyEditable>().Union(GetProperties<InGameEditable>()).ToList();
            }
            else
            {
                return GetProperties<ConditionallyEditable>()
                    .Where(ce => ce.property.GetAttribute<ConditionallyEditable>().IsEditable(this))
                    .Union(GetProperties<InGameEditable>()).ToList();
            }
        }

        private void ReadPropertyChange(IReadMessage msg, bool inGameEditableOnly, Client sender = null)
        {
            //ignoreConditions: true = include all ConditionallyEditable properties at this point,
            //to ensure client/server doesn't get any properties mixed up if there's some conditions that can vary between the server and the clients
            var allProperties = inGameEditableOnly ? GetInGameEditableProperties(ignoreConditions: true) : GetProperties<Editable>();
            if (allProperties.Count == 0) { return; }

            int propertyIndex = 0;
            if (allProperties.Count > 1)
            {
                propertyIndex = msg.ReadByte();
            }

            bool allowEditing = true;
            object parentObject = allProperties[propertyIndex].obj;
            SerializableProperty property = allProperties[propertyIndex].property;
            if (inGameEditableOnly && parentObject is ItemComponent ic)
            {
                if (!ic.AllowInGameEditing) { allowEditing = false; }
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                if (!CanClientAccess(sender) || !(property.GetAttribute<ConditionallyEditable>()?.IsEditable(this) ?? true))
                {
                    allowEditing = false;
                }
            }

            Type type = property.PropertyType;
            string logValue = "";
            if (type == typeof(string))
            {
                string val = msg.ReadString();
                logValue = val;
                if (allowEditing) 
                { 
                    property.TrySetValue(parentObject, val);
                }
            }
            else if (type == typeof(Identifier))
            {
                Identifier val = msg.ReadIdentifier();
                logValue = val.Value;
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(float))
            {
                float val = msg.ReadSingle();
                logValue = val.ToString("G", CultureInfo.InvariantCulture);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(int))
            {
                int val = msg.ReadInt32();
                logValue = val.ToString();
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(bool))
            {
                bool val = msg.ReadBoolean();
                logValue = val.ToString();
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Color))
            {
                Color val = new Color(msg.ReadByte(), msg.ReadByte(), msg.ReadByte(), msg.ReadByte());
                logValue = XMLExtensions.ColorToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Vector2))
            {
                Vector2 val = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                logValue = XMLExtensions.Vector2ToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Vector3))
            {
                Vector3 val = new Vector3(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
                logValue = XMLExtensions.Vector3ToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Vector4))
            {
                Vector4 val = new Vector4(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
                logValue = XMLExtensions.Vector4ToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Point))
            {
                Point val = new Point(msg.ReadInt32(), msg.ReadInt32());
                logValue = XMLExtensions.PointToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Rectangle))
            {
                Rectangle val = new Rectangle(msg.ReadInt32(), msg.ReadInt32(), msg.ReadInt32(), msg.ReadInt32());
                logValue = XMLExtensions.RectToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(string[]))
            {
                int arrayLength = msg.ReadInt32();
                string[] val = new string[arrayLength];
                for (int i = 0; i < arrayLength; i++)
                {
                    val[i] = msg.ReadString();
                }
                if (allowEditing)
                {
                    property.TrySetValue(parentObject, val);
                }
            }
            else if (typeof(Enum).IsAssignableFrom(type))
            {
                int intVal = msg.ReadInt32();
                try
                {
                    if (allowEditing) 
                    { 
                        property.TrySetValue(parentObject, Enum.ToObject(type, intVal));
                        logValue = property.GetValue(parentObject).ToString();
                    }
                }
                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Failed to convert the int value \"" + intVal + "\" to " + type, e);
#endif
                    GameAnalyticsManager.AddErrorEventOnce(
                        "Item.ReadPropertyChange:" + Name + ":" + type,
                        GameAnalyticsManager.ErrorSeverity.Warning,
                        "Failed to convert the int value \"" + intVal + "\" to " + type + " (item " + Name + ")");
                }
            }
            else
            {
                return;
            }

#if SERVER
            if (allowEditing)
            {
                //the property change isn't logged until the value stays unchanged for 1 second to prevent log spam when a player adjusts a value
                if (logPropertyChangeCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(logPropertyChangeCoroutine);
                }
                logPropertyChangeCoroutine = CoroutineManager.Invoke(() =>
                {
                    GameServer.Log($"{sender.Character.Name} set the value \"{property.Name}\" of the item \"{Name}\" to \"{logValue}\".", ServerLog.MessageType.ItemInteraction);
                }, delay: 1.0f);
            }
#endif

            if (GameMain.NetworkMember is { IsServer: true })
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new ChangePropertyEventData(property));
            }
        }

        partial void UpdateNetPosition(float deltaTime);

        public static Item Load(ContentXElement element, Submarine submarine, IdRemap idRemap)
        {
            return Load(element, submarine, createNetworkEvent: false, idRemap: idRemap);
        }

        /// <summary>
        /// Instantiate a new item and load its data from the XML element.
        /// </summary>
        /// <param name="element">The element containing the data of the item</param>
        /// <param name="submarine">The submarine to spawn the item in (can be null)</param>
        /// <param name="createNetworkEvent">Should an EntitySpawner event be created to notify clients about the item being created.</param>
        /// <returns></returns>
        public static Item Load(ContentXElement element, Submarine submarine, bool createNetworkEvent, IdRemap idRemap)
        {
            string name = element.GetAttribute("name").Value;
            Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);

            if (string.IsNullOrWhiteSpace(name) && identifier.IsEmpty)
            {
                string errorMessage = "Failed to load an item (both name and identifier were null):\n"+element.ToString();
                DebugConsole.ThrowError(errorMessage);
                GameAnalyticsManager.AddErrorEventOnce("Item.Load:NameAndIdentifierNull", GameAnalyticsManager.ErrorSeverity.Error, errorMessage);
                return null;
            }

            Identifier pendingSwap = element.GetAttributeIdentifier("pendingswap", Identifier.Empty);
            ItemPrefab appliedSwap = null;
            ItemPrefab oldPrefab = null;
            if (!pendingSwap.IsEmpty && Level.Loaded?.Type != LevelData.LevelType.Outpost)
            {
                oldPrefab = ItemPrefab.Find(name, identifier);
                appliedSwap = ItemPrefab.Find(string.Empty, pendingSwap);
                identifier = pendingSwap;
                pendingSwap = Identifier.Empty;
            }

            ItemPrefab prefab = ItemPrefab.Find(name, identifier);
            if (prefab == null) { return null; }

            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            Vector2 centerPos = new Vector2(rect.X + rect.Width / 2, rect.Y - rect.Height / 2);
            if (appliedSwap != null)
            {
                rect.Width = (int)(prefab.Sprite.size.X * prefab.Scale);
                rect.Height = (int)(prefab.Sprite.size.Y * prefab.Scale);
            }
            else if (rect.Width == 0 && rect.Height == 0)
            {
                rect.Width = (int)(prefab.Size.X * prefab.Scale);
                rect.Height = (int)(prefab.Size.Y * prefab.Scale);
            }

            Item item = new Item(rect, prefab, submarine, callOnItemLoaded: false, id: idRemap.GetOffsetId(element))
            {
                Submarine = submarine,
                linkedToID = new List<ushort>(),
                PendingItemSwap = pendingSwap.IsEmpty ? null : MapEntityPrefab.Find(pendingSwap.Value) as ItemPrefab
            };

#if SERVER
            if (createNetworkEvent)
            {
                Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
            }
#endif

            foreach (XAttribute attribute in (appliedSwap?.ConfigElement ?? element).Attributes())
            {
                if (!item.SerializableProperties.TryGetValue(attribute.NameAsIdentifier(), out SerializableProperty property)) { continue; }
                bool shouldBeLoaded = false;
                foreach (var propertyAttribute in property.Attributes.OfType<Serialize>())
                {
                    if (propertyAttribute.IsSaveable == IsPropertySaveable.Yes)
                    {
                        shouldBeLoaded = true;
                        break;
                    }
                }

                if (shouldBeLoaded)
                {
                    object prevValue = property.GetValue(item);
                    property.TrySetValue(item, attribute.Value);
                    //create network events for properties that differ from the prefab values
                    //(e.g. if a character has an item with modified colors in their inventory)
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer && property.Attributes.OfType<Editable>().Any() &&
                        (submarine == null || !submarine.Loading))
                    {
                        if (property.Name == "Tags" ||
                            property.Name == "Condition" ||
                            property.Name == "Description")
                        {
                            //these can be ignored, they're always written in the spawn data
                        }
                        else
                        {
                            if (!(property.GetValue(item)?.Equals(prevValue) ?? true))
                            {
                                GameMain.NetworkMember.CreateEntityEvent(item, new ChangePropertyEventData(property));
                            }
                        }
                    }
                }
            }

            item.ParseLinks(element, idRemap);

            bool thisIsOverride = element.GetAttributeBool("isoverride", false);

            //if we're overriding a non-overridden item in a sub/assembly xml or vice versa, 
            //use the values from the prefab instead of loading them from the sub/assembly xml
            bool usePrefabValues = thisIsOverride != ItemPrefab.Prefabs.IsOverride(prefab) || appliedSwap != null;
            List<ItemComponent> unloadedComponents = new List<ItemComponent>(item.components);
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "upgrade":
                        {
                            var upgradeIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                            UpgradePrefab upgradePrefab = UpgradePrefab.Find(upgradeIdentifier);
                            int level = subElement.GetAttributeInt("level", 1);
                            if (upgradePrefab != null)
                            {
                                item.AddUpgrade(new Upgrade(item, upgradePrefab, level, appliedSwap != null ? null : subElement));
                            }
                            else
                            {
                                DebugConsole.AddWarning($"An upgrade with identifier \"{upgradeIdentifier}\" on {item.Name} was not found. " +
                                                        "It's effect will not be applied and won't be saved after the round ends.");
                            }
                            break;
                        }
                    default:
                        {
                            ItemComponent component = unloadedComponents.Find(x => x.Name == subElement.Name.ToString());
                            if (component == null) { continue; }
                            component.Load(subElement, usePrefabValues, idRemap);
                            unloadedComponents.Remove(component);
                            break;
                        }
                }
            }
            if (usePrefabValues && appliedSwap == null)
            {
                //use prefab scale when overriding a non-overridden item or vice versa
                item.Scale = prefab.ConfigElement.GetAttributeFloat(item.scale, "scale", "Scale");
            }

            item.Upgrades.ForEach(upgrade => upgrade.ApplyUpgrade());

            var availableSwapIds = element.GetAttributeIdentifierArray("availableswaps", Array.Empty<Identifier>());
            foreach (Identifier swapId in availableSwapIds)
            {
                ItemPrefab swapPrefab = ItemPrefab.Find(string.Empty, swapId);
                if (swapPrefab != null)
                {
                    item.AvailableSwaps.Add(swapPrefab);
                }
            }

            float prevRotation = item.Rotation;
            if (element.GetAttributeBool("flippedx", false)) { item.FlipX(false); }
            if (element.GetAttributeBool("flippedy", false)) { item.FlipY(false); }
            item.Rotation = prevRotation;

            if (appliedSwap != null)
            {
                item.SpriteDepth = element.GetAttributeFloat("spritedepth", item.SpriteDepth);
                item.SpriteColor = element.GetAttributeColor("spritecolor", item.SpriteColor);
                item.Rotation = element.GetAttributeFloat("rotation", item.Rotation);
                item.PurchasedNewSwap = element.GetAttributeBool("purchasednewswap", false);

                float scaleRelativeToPrefab = element.GetAttributeFloat(item.scale, "scale", "Scale") / oldPrefab.Scale;
                item.Scale *= scaleRelativeToPrefab;

                if (oldPrefab.SwappableItem != null && prefab.SwappableItem != null)
                {
                    Vector2 oldRelativeOrigin = (oldPrefab.SwappableItem.SwapOrigin - oldPrefab.Size / 2) * element.GetAttributeFloat(item.scale, "scale", "Scale");
                    oldRelativeOrigin.Y = -oldRelativeOrigin.Y;
                    oldRelativeOrigin = MathUtils.RotatePoint(oldRelativeOrigin, -item.RotationRad);
                    Vector2 oldOrigin = centerPos + oldRelativeOrigin;

                    Vector2 relativeOrigin = (prefab.SwappableItem.SwapOrigin - prefab.Size / 2) * item.Scale;
                    relativeOrigin.Y = -relativeOrigin.Y;
                    relativeOrigin = MathUtils.RotatePoint(relativeOrigin, -item.RotationRad);
                    Vector2 origin = new Vector2(rect.X + rect.Width / 2, rect.Y - rect.Height / 2) + relativeOrigin;

                    item.rect.Location -= (origin - oldOrigin).ToPoint();
                }

                if (item.PurchasedNewSwap && !string.IsNullOrEmpty(appliedSwap.SwappableItem?.SpawnWithId))
                {
                    var container = item.GetComponent<ItemContainer>();
                    if (container != null)
                    {
                        container.SpawnWithId = appliedSwap.SwappableItem.SpawnWithId;
                    }
                    /*string[] splitIdentifier = appliedSwap.SwappableItem.SpawnWithId.Split(',');
                    foreach (string id in splitIdentifier)
                    {
                        ItemPrefab itemToSpawn = ItemPrefab.Find(name: null, identifier: id.Trim());
                        if (itemToSpawn == null)
                        {
                            DebugConsole.ThrowError($"Failed to spawn an item inside the purchased {item.Name} (could not find an item with the identifier \"{id}\").");
                        }
                        else
                        {
                            var spawnedItem = new Item(itemToSpawn, Vector2.Zero, null);
                            item.OwnInventory.TryPutItem(spawnedItem, null, spawnedItem.AllowedSlots, createNetworkEvent: false);
                            Spawner?.AddToSpawnQueue(itemToSpawn, item.OwnInventory, spawnIfInventoryFull: false);
                        }
                    }*/
                }
                item.PurchasedNewSwap = false;
            }

            float condition = element.GetAttributeFloat("condition", item.MaxCondition);
            item.condition = MathHelper.Clamp(condition, 0, item.MaxCondition);
            item.lastSentCondition = item.condition;

            item.RecalculateConditionValues();
            item.SetActiveSprite();

            if (submarine?.Info.GameVersion != null)
            {
                SerializableProperty.UpgradeGameVersion(item, item.Prefab.ConfigElement, submarine.Info.GameVersion);
            }

            foreach (ItemComponent component in item.components)
            {
                component.OnItemLoaded();
            }
            
            return item;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Item");

            element.Add(
                new XAttribute("name", Prefab.OriginalName),
                new XAttribute("identifier", Prefab.Identifier),
                new XAttribute("ID", ID));

            if (PendingItemSwap != null)
            {
                element.Add(new XAttribute("pendingswap", PendingItemSwap.Identifier));
            }

            if (Rotation != 0f) { element.Add(new XAttribute("rotation", Rotation)); }

            if (ItemPrefab.Prefabs.IsOverride(Prefab)) { element.Add(new XAttribute("isoverride", "true")); }
            if (FlippedX) { element.Add(new XAttribute("flippedx", true)); }
            if (FlippedY) { element.Add(new XAttribute("flippedy", true)); }

            if (AvailableSwaps.Any())
            {
                element.Add(new XAttribute("availableswaps", string.Join(',', AvailableSwaps.Select(s => s.Identifier))));
            }

            if (condition < MaxCondition)
            {
                element.Add(new XAttribute("condition", condition.ToString("G", CultureInfo.InvariantCulture)));
            }

            if (!MathUtils.NearlyEqual(healthMultiplier, 1.0f))
            {
                element.Add(new XAttribute("healthmultiplier", HealthMultiplier.ToString("G", CultureInfo.InvariantCulture)));
            }

            Item rootContainer = GetRootContainer() ?? this;
            System.Diagnostics.Debug.Assert(Submarine != null || rootContainer.ParentInventory?.Owner is Character);

            Vector2 subPosition = Submarine == null ? Vector2.Zero : Submarine.HiddenSubPosition;

            int width = ResizeHorizontal ? rect.Width : defaultRect.Width;
            int height = ResizeVertical ? rect.Height : defaultRect.Height;
            element.Add(new XAttribute("rect",
                (int)(rect.X - subPosition.X) + "," +
                (int)(rect.Y - subPosition.Y) + "," +
                width + "," + height));
            
            if (linkedTo != null && linkedTo.Count > 0)
            {
                bool isOutpost = Submarine != null && Submarine.Info.IsOutpost;
                var saveableLinked = linkedTo.Where(l => l.ShouldBeSaved && (l.Removed == Removed) && (l.Submarine == null || l.Submarine.Info.IsOutpost == isOutpost));
                element.Add(new XAttribute("linked", string.Join(",", saveableLinked.Select(l => l.ID.ToString()))));
            }

            SerializableProperty.SerializeProperties(this, element);

            foreach (ItemComponent ic in components)
            {
                ic.Save(element);
            }

            foreach (var upgrade in Upgrades)
            {
                upgrade.Save(element);
            }

            parentElement.Add(element);

            return element;
        }

        public virtual void Reset()
        {
            var holdable = GetComponent<Holdable>();
            bool wasAttached = holdable?.Attached ?? false;

            SerializableProperties = SerializableProperty.DeserializeProperties(this, Prefab.ConfigElement);
            Sprite.ReloadXML();
            SpriteDepth = Sprite.Depth;
            condition = MaxCondition;
            components.ForEach(c => c.Reset());
            if (wasAttached)
            {
                holdable.AttachToWall();
            }
        }

        public override void OnMapLoaded()
        {
            FindHull();

            foreach (ItemComponent ic in components)
            {
                ic.OnMapLoaded();
            }
        }
        
        /// <summary>
        /// Remove the item so that it doesn't appear to exist in the game world (stop sounds, remove bodies etc)
        /// but don't reset anything that's required for cloning the item
        /// </summary>
        public override void ShallowRemove()
        {
            base.ShallowRemove();
            
            foreach (ItemComponent ic in components)
            {
                ic.ShallowRemove();
            }
            ItemList.Remove(this);

            if (body != null)
            {
                body.Remove();
                body = null;
            }
        }

        public override void Remove()
        {
            if (Removed)
            {
                DebugConsole.ThrowError("Attempting to remove an already removed item (" + Name + ")\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            DebugConsole.Log("Removing item " + Name + " (ID: " + ID + ")");
            
            base.Remove();

            foreach (Character character in Character.CharacterList)
            {
                if (character.SelectedConstruction == this) { character.SelectedConstruction = null; }
            }

            Door door = GetComponent<Door>();
            Ladder ladder = GetComponent<Ladder>();
            if (door != null || ladder != null)
            {
                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (door != null && wp.ConnectedDoor == door) { wp.ConnectedGap = null; }
                    if (ladder != null && wp.Ladders == ladder) { wp.Ladders = null; }
                }
            }

            connections?.Clear();

            if (parentInventory != null)
            {
                if (parentInventory is CharacterInventory characterInventory)
                {
                    characterInventory.RemoveItem(this, tryEquipFromSameStack: true);
                }
                else
                {
                    parentInventory.RemoveItem(this);
                }
                parentInventory = null;
            }

            foreach (ItemComponent ic in components)
            {
                ic.Remove();
#if CLIENT
                ic.GuiFrame = null;
#endif
            }
            ItemList.Remove(this);

            if (body != null)
            {
                body.Remove();
                body = null;
            }

            CurrentHull = null;

            if (StaticFixtures != null)
            {
                foreach (Fixture fixture in StaticFixtures)
                {
                    //if the world is null, the body has already been removed
                    //happens if the sub the fixture is attached to is removed before the item
                    if (fixture.Body?.World == null) { continue; }
                    fixture.Body.Remove(fixture);
                }
                StaticFixtures.Clear();
            }

            foreach (Item it in ItemList)
            {
                if (it.linkedTo.Contains(this))
                {
                    it.linkedTo.Remove(this);
                }
            }

            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();

        public static void RemoveByPrefab(ItemPrefab prefab)
        {
            if (ItemList == null) { return; }
            List<Item> list = new List<Item>(ItemList);
            foreach (Item item in list)
            {
                if (((MapEntity)item).Prefab == prefab)
                {
                    item.Remove();
                }
            }
        }
    }
    class AbilityApplyTreatment : AbilityObject, IAbilityCharacter, IAbilityItem
    {
        public Character Character { get; set; }
        public Character User { get; set; }
        public Item Item { get; set; }

        public AbilityApplyTreatment(Character user, Character target, Item item)
        {
            Character = target;
            User = user;
            Item = item;
        }
    }
}
