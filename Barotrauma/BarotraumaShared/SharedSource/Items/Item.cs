﻿using Barotrauma.Items.Components;
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
        #region Lists

        /// <summary>
        /// A list of every item that exists somewhere in the world. Note that there can be a huge number of items in the list, 
        /// and you probably shouldn't be enumerating it to find some that match some specific criteria (unless that's done very, very sparsely or during initialization).
        /// </summary>
        public static readonly List<Item> ItemList = new List<Item>();

        private static readonly HashSet<Item> _dangerousItems = new HashSet<Item>();

        public static IReadOnlyCollection<Item> DangerousItems => _dangerousItems;

        private static readonly List<Item> _repairableItems = new List<Item>();

        /// <summary>
        /// Items that have one more more Repairable component
        /// </summary>
        public static IReadOnlyCollection<Item> RepairableItems => _repairableItems;

        private static readonly List<Item> _cleanableItems = new List<Item>();

        /// <summary>
        /// Items that may potentially need to be cleaned up (pickable, not attached to a wall, and not inside a valid container)
        /// </summary>
        public static IReadOnlyCollection<Item> CleanableItems => _cleanableItems;

        private static readonly HashSet<Item> _deconstructItems = new HashSet<Item>();

        /// <summary>
        /// Items that have been marked for deconstruction
        /// </summary>
        public static HashSet<Item> DeconstructItems => _deconstructItems;

        private static readonly List<Item> _sonarVisibleItems = new List<Item>();

        /// <summary>
        /// Items whose <see cref="ItemPrefab.SonarSize"/> is larger than 0
        /// </summary>
        public static IReadOnlyCollection<Item> SonarVisibleItems => _sonarVisibleItems;

        private static readonly List<Item> _turretTargetItems = new List<Item>();

        /// <summary>
        /// Items whose <see cref="ItemPrefab.IsAITurretTarget"/> is true.
        /// </summary>
        public static IReadOnlyCollection<Item> TurretTargetItems => _turretTargetItems;

        private static readonly List<Item> _chairItems = new List<Item>();

        /// <summary>
        /// Items that have the tag <see cref="Tags.ChairItem"/>. Which is an oddly specific thing, but useful as an optimization for NPC AI.
        /// </summary>
        public static IReadOnlyCollection<Item> ChairItems => _chairItems;

        #endregion

        public new ItemPrefab Prefab => base.Prefab as ItemPrefab;

        public override ContentPackage ContentPackage => Prefab?.ContentPackage;

        public static bool ShowLinks = true;

        private HashSet<Identifier> tags;

        private readonly bool isWire, isLogic;

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
        }

        public void AssignCampaignInteractionType(CampaignMode.InteractionType interactionType, IEnumerable<Client> targetClients = null)
        {
            if (campaignInteractionType == interactionType) { return; }                
            campaignInteractionType = interactionType;
            AssignCampaignInteractionTypeProjSpecific(campaignInteractionType, targetClients);                
        }


        partial void AssignCampaignInteractionTypeProjSpecific(CampaignMode.InteractionType interactionType, IEnumerable<Client> targetClients);

        public bool Visible = true;

#if CLIENT
        public SpriteEffects SpriteEffects = SpriteEffects.None;
#endif

        //components that determine the functionality of the item
        private readonly Dictionary<Type, List<ItemComponent>> componentsByType = new Dictionary<Type, List<ItemComponent>>();
        private readonly List<ItemComponent> components;

        /// <summary>
        /// Components that are Active or need to be updated for some other reason (status effects, sounds)
        /// </summary>
        private readonly List<ItemComponent> updateableComponents = new List<ItemComponent>();
        private readonly List<IDrawableComponent> drawableComponents;
        private bool hasComponentsToDraw;

        /// <summary>
        /// Has everything in the item been loaded/instantiated/initialized (basically, can be used to check if the whole constructor/Load method has run).
        /// Most commonly used to avoid creating network events when some value changes if the item is being initialized.
        /// </summary>
        public bool FullyInitialized { get; private set; }

        public PhysicsBody body;
        private readonly float originalWaterDragCoefficient;
        private float? overrideWaterDragCoefficient;
        public float WaterDragCoefficient
        {
            get => overrideWaterDragCoefficient ?? originalWaterDragCoefficient;
            set => overrideWaterDragCoefficient = value;
        }

        /// <summary>
        /// Can be used by StatusEffects to set the type of the body (if the item has one)
        /// </summary>
        public BodyType BodyType
        {
            get { return body?.BodyType ?? BodyType.Dynamic; }
            set 
            {
                if (body != null)
                {
                    body.BodyType = value;
                }
            }
        }

        /// <summary>
        /// Removes the override value -> falls back to using the original value defined in the xml.
        /// </summary>
        public void ResetWaterDragCoefficient() => overrideWaterDragCoefficient = null;

        public readonly XElement StaticBodyConfig;

        public List<Fixture> StaticFixtures = new List<Fixture>();

        private bool transformDirty = true;

        private static readonly List<Item> itemsWithPendingConditionUpdates = new List<Item>();

        private float lastSentCondition;
        private float sendConditionUpdateTimer;

        private float prevCondition;
        private float condition;

        private bool inWater;
        private readonly bool hasInWaterStatusEffects;
        private readonly bool hasNotInWaterStatusEffects;

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
        private readonly bool[] hasStatusEffectsOfType = new bool[Enum.GetValues(typeof(ActionType)).Length];
        private readonly Dictionary<ActionType, List<StatusEffect>> statusEffectLists;

        /// <summary>
        /// Helper variable for handling max condition multipliers from campaign settings
        /// </summary>
        private readonly float conditionMultiplierCampaign = 1.0f;

        public Action OnInteract;

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

        /// <summary>
        /// Which character equipped this item? 
        /// May not be the same character as the one who it's equipped on (you can e.g. equip diving masks on another character).
        /// </summary>
        public Character Equipper;

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
                if (parentInventory != null) 
                { 
                    Container = parentInventory.Owner as Item;
                    RemoveFromDroppedStack(allowClientExecute: false);
                }
#if SERVER
                PreviousParentInventory = value;
#endif
            }
        }

        private Item rootContainer;
        public Item RootContainer 
        {
            get { return rootContainer; }
            private set
            {
                if (value == this)
                {
                    DebugConsole.ThrowError($"Attempted to set the item \"{Prefab.Identifier}\" as it's own root container!\n{Environment.StackTrace.CleanupStackTrace()}");
                    rootContainer = null;
                    return;
                }
                rootContainer = value;
            }
        }

        private bool inWaterProofContainer;

        private Item container;
        public Item Container
        {
            get { return container; }
            private set
            {
                if (value != container)
                {
                    container = value;
                    CheckCleanable();
                    SetActiveSprite();
                    RefreshRootContainer();
                }
            }
        }
                
        /// <summary>
        /// Note that this is not a <see cref="LocalizedString"/> instance, just the current name of the item as a string.
        /// If you e.g. set this as the text in a textbox, it will not update automatically when the language is changed.
        /// If you want that to happen, use <see cref="Prefab.Name"/> instead.
        /// </summary>
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

        private string descriptionTag;

        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true), ConditionallyEditable(ConditionallyEditable.ConditionType.OnlyByStatusEffectsAndNetwork)]
        /// <summary>
        /// Can be used to set a localized description via StatusEffects
        /// </summary>
        public string DescriptionTag
        {
            get { return descriptionTag; }
            set 
            {
                if (value == descriptionTag) { return; }                
                if (value.IsNullOrEmpty())
                {
                    descriptionTag = null;
                    description = null;
                }
                else
                {
                    description = TextManager.Get(value).Value;
                    descriptionTag = value;
                }
                if (FullyInitialized &&
                    SerializableProperties != null &&
                    SerializableProperties.TryGetValue(nameof(DescriptionTag).ToIdentifier(), out SerializableProperty property))
                {
                    GameMain.NetworkMember?.CreateEntityEvent(this, new ChangePropertyEventData(property, this));
                }
            }
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
            if (IsHidden) { return false; }
            if (character != null && character.IsOnPlayerTeam)
            {
                return IsPlayerTeamInteractable;
            }
            else
            {
                return !NonInteractable;
            }
        }

        [ConditionallyEditable(ConditionallyEditable.ConditionType.AllowRotating, DecimalCount = 3, ForceShowPlusMinusButtons = true, ValueStep = 0.1f), Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Rotation
        {
            get
            {
                return MathHelper.ToDegrees(RotationRad);
            }
            set
            {
                if (!Prefab.AllowRotatingInEditor) { return; }
                RotationRad = MathUtils.WrapAnglePi(MathHelper.ToRadians(value));
#if CLIENT
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    SetContainedItemPositions();
                    foreach (var light in GetComponents<LightComponent>())
                    {
                        light.SetLightSourceTransform();
                    }
                    foreach (var turret in GetComponents<Turret>())
                    {
                        turret.UpdateLightComponents();
                    }
                    foreach (var triggerComponent in GetComponents<TriggerComponent>())
                    {
                        triggerComponent.SetPhysicsBodyPosition();
                    }
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
                scale = MathHelper.Clamp(value, Prefab.MinScale, Prefab.MaxScale);

                float relativeScale = scale / base.Prefab.Scale;

                if (!ResizeHorizontal || !ResizeVertical)
                {
                    int newWidth = ResizeHorizontal ? rect.Width : (int)(defaultRect.Width * relativeScale);
                    int newHeight = ResizeVertical ? rect.Height : (int)(defaultRect.Height * relativeScale);
                    Rect = new Rectangle(rect.X, rect.Y, newWidth, newHeight);
                }

                //need to update to get the position of the physics body to match the new center of the item
                if (body != null)
                {
                    if (FullyInitialized)
                    {
                        //fully intialized = scaling after the item has been created
                        //if this happens in the editor, refresh the transform to get the rect to match the position of the physics body
                        if (Screen.Selected is { IsEditor: true }) { UpdateTransform(); }                           
                    }
                    else
                    {
                        //scaling during loading -> move the body to the new center of the rect
                        body.SetTransformIgnoreContacts(ConvertUnits.ToSimUnits(base.Position), body.Rotation);
                    }
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

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes), ConditionallyEditable(ConditionallyEditable.ConditionType.Pickable)]
        public Color InventoryIconColor
        {
            get;
            protected set;
        }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, description: "Changes the color of the item this item is contained inside. Only has an effect if either of the UseContainedSpriteColor or UseContainedInventoryIconColor property of the container is set to true."), 
            ConditionallyEditable(ConditionallyEditable.ConditionType.Pickable)]
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

        /// <summary>
        /// Can be used by status effects or conditionals to check whether the item is contained inside something
        /// </summary>
        public bool IsContained
        {
            get
            {
                return parentInventory != null;
            }
        }

        /// <summary>
        /// Can be used by status effects or conditionals to the speed of the item
        /// </summary>
        public float Speed
        {
            get
            {
                if (body != null && body.PhysEnabled)
                {
                    return body.LinearVelocity.Length();
                }
                else if (ParentInventory?.Owner is Character character)
                {
                    return character.AnimController.MainLimb.LinearVelocity.Length();
                }
                else if (container != null)
                {
                    return container.Speed;
                }
                return 0.0f;
            }
        }

        public Color? HighlightColor;

        /// <summary>
        /// Can be used to modify the AITarget's label using status effects
        /// </summary>
        [Serialize("", IsPropertySaveable.Yes)]
        public string SonarLabel
        {
            get { return AiTarget?.SonarLabel?.Value ?? ""; }
            set
            {
                if (AiTarget != null)
                {
                    string trimmedStr = !string.IsNullOrEmpty(value) && value.Length > 250 ? value.Substring(250) : value;
                    AiTarget.SonarLabel = TextManager.Get(trimmedStr).Fallback(trimmedStr);
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

        /// <summary>
        /// Can be used by status effects or conditionals to modify the sound range
        /// </summary>
        [Serialize(0.0f, IsPropertySaveable.No)]
        public new float SoundRange
        {
            get { return aiTarget == null ? 0.0f : aiTarget.SoundRange; }
            set { if (aiTarget != null) { aiTarget.SoundRange = Math.Max(0.0f, value); } }
        }

        /// <summary>
        /// Can be used by status effects or conditionals to modify the sight range
        /// </summary>
        [Serialize(0.0f, IsPropertySaveable.No)]
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

        /// <summary>
        /// Can be set by status effects to prevent bots from cleaning up the item
        /// </summary>
        public bool DontCleanUp
        {
            get; set;
        }

        /// <summary>
        /// Have the <see cref="ActionType.OnInserted"/> effects of the item already triggered when it was placed inside it's current container?
        /// Used to prevent the effects from executing again when e.g. an existing character (who's inventory items' effects already triggered on some earlier round) spawns mid-round.
        /// </summary>
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool OnInsertedEffectsApplied
        {
            get; set;
        }

        /// <summary>
        /// Were the <see cref="ActionType.OnInserted"/> effects already been applied when the item first spawned (loaded from a save)?
        /// Needed for communicating to the clients whether they should trigger when the item spawns.
        /// </summary>
        public bool OnInsertedEffectsAppliedOnPreviousRound;

        public Color Color
        {
            get { return spriteColor; }
        }

        public bool IsFullCondition { get; private set; }
        public float MaxCondition { get; private set; }
        public float ConditionPercentage { get; private set; }

        /// <summary>
        /// Condition percentage disregarding MaxRepairConditionMultiplier (i.e. this can go above 100% if the item is repaired beyond the normal maximum)
        /// </summary>
        public float ConditionPercentageRelativeToDefaultMaxCondition
        {
            get
            {
                float defaultMaxCondition = MaxCondition / MaxRepairConditionMultiplier;
                return MathUtils.Percentage(Condition, defaultMaxCondition);
            }
        }

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
                RecalculateConditionValues();
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

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool HasBeenInstantiatedOnce { get; set; }
        
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

        private bool? isDangerous;
        /// <summary>
        /// Bots avoid rooms with dangerous items in them. Normally this value is <see cref="ItemPrefab.IsDangerous">defined in the prefab</see>, 
        /// but this property can be used to override the prefab value.
        /// </summary>
        public bool IsDangerous
        {
            get { return isDangerous ?? Prefab.IsDangerous; }
            set
            { 
                isDangerous = value; 
                if (!value)
                {
                    _dangerousItems.Remove(this);
                }
                else
                {
                    _dangerousItems.Add(this);
                }
            }
        }

        [Editable, Serialize(false, isSaveable: IsPropertySaveable.Yes, "When enabled will prevent the item from taking damage from all sources")]
        public bool InvulnerableToDamage { get; set; }

        /// <summary>
        /// Should bots automatically unequip the item? Normally always true, but disabled on items that have been configured to be equipped by default in an item set or the character's job items.
        /// Note that the value is not saved: if the NPC becomes persistent (e.g. Artie Dolittle hired to the crew) they will no longer keep holding the item.
        /// </summary>
        public bool UnequipAutomatically = true;

        /// <summary>
        /// Was the item stolen during the current round. Note that it's possible for the items to be found in the player's inventory even though they weren't actually stolen.
        /// For example, a guard can place handcuffs there. So use <see cref="Illegitimate"/> for checking if the item is illegitimately held.
        /// </summary>
        public bool StolenDuringRound;
        
        /// <summary>
        /// Item shouldn't be in the player's inventory. If the guards find it, they will consider it as a theft.
        /// </summary>
        public bool Illegitimate => !AllowStealing && SpawnedInCurrentOutpost;

        private bool spawnedInCurrentOutpost;
        public bool SpawnedInCurrentOutpost
        {
            get { return spawnedInCurrentOutpost; }
            set
            {
                if (!spawnedInCurrentOutpost && value)
                {
                    OriginalOutpost = GameMain.GameSession?.LevelData?.Seed;
                }
                spawnedInCurrentOutpost = value;
            }
        }

        private bool allowStealing;
        [Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true, 
            description: $"Determined by where/how the item originally spawned. If ItemPrefab.AllowStealing is true, stealing the item is always allowed.")]
        public bool AllowStealing 
        {
            get { return allowStealing || Prefab.AllowStealingAlways;  }
            set { allowStealing = value; }
        }

        public bool IsSalvageMissionItem;

        private string originalOutpost;
        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public string OriginalOutpost
        {
            get { return originalOutpost; }
            set
            {
                originalOutpost = value;
                if (!string.IsNullOrEmpty(value) && 
                    GameMain.GameSession?.LevelData?.Type == LevelData.LevelType.Outpost &&
                    GameMain.GameSession?.LevelData?.Seed == value)
                {
                    spawnedInCurrentOutpost = true;
                }
            }
        }

        [Editable, Serialize("", IsPropertySaveable.Yes)]
        public string Tags
        {
            get => tags.ConvertToString();
            set
            {
                tags.Clear();
                // Always add prefab tags
                base.Prefab.Tags.ForEach(t => tags.Add(t));
                // Then add new tags
                tags = tags.Union(value.ToIdentifiers()).ToHashSet();
            }
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool FireProof
        {
            get; private set;
        }

        private bool waterProof;
        [Serialize(false, IsPropertySaveable.No)]
        public bool WaterProof
        {
            get { return waterProof; }
            private set
            {
                if (waterProof == value) { return; }
                waterProof = value;
                foreach (Item containedItem in ContainedItems)
                {
                    containedItem.RefreshInWaterProofContainer();
                }
            }
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
                if (hasInWaterStatusEffects) { return inWater; }

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
                if (OwnInventories.Length < 2)
                {
                    if (OwnInventory == null) { yield break; }

                    foreach (var item in OwnInventory.AllItems)
                    {
                        yield return item;
                    }
                }
                else
                {
                    foreach (var inventory in OwnInventories)
                    {
                        foreach (var item in inventory.AllItems)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        public ItemInventory OwnInventory
        {
            get { return ownInventory; }
        }

        public readonly ImmutableArray<ItemInventory> OwnInventories = ImmutableArray<ItemInventory>.Empty;

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

        public float WorldPositionX => WorldPosition.X;
        public float WorldPositionY => WorldPosition.Y;

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

        public bool IgnoreByAI(Character character) => HasTag(Barotrauma.Tags.IgnoredByAI) || OrderedToBeIgnored && character.IsOnPlayerTeam;
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

        public bool InPlayerSubmarine => Submarine?.Info is { IsPlayer: true };
        public bool InBeaconStation => Submarine?.Info is { Type: SubmarineType.BeaconStation };

        public bool IsLadder { get; }

        /// <summary>
        /// Secondary items can be selected at the same time with a primary item (e.g. a ladder or a chair can be selected at the same time with some device).
        /// </summary>
        public bool IsSecondaryItem { get; }

        private ItemStatManager statManager;
        public ItemStatManager StatManager
        {
            get
            {
               statManager ??= new ItemStatManager(this);
               return statManager;
            }
        }

        /// <summary>
        /// Timing.TotalTimeUnpaused when some character was last eating the item
        /// </summary>
        public float LastEatenTime { get; set; }

        public Action<Character> OnDeselect;

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

            condition = MaxCondition = prevCondition = Prefab.Health;
            ConditionPercentage = 100.0f;
           
            lastSentCondition = condition;

            AllowDeconstruct = itemPrefab.AllowDeconstruct;

            allPropertyObjects.Add(this);

            ContentXElement element = itemPrefab.ConfigElement;
            if (element == null) return;

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            if (submarine == null || !submarine.Loading) { FindHull(); }

            SetActiveSprite();

            ContentXElement bodyElement = null;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "body":
                        bodyElement = subElement;
                        float density = subElement.GetAttributeFloat("density", Physics.NeutralDensity);
                        float minDensity = subElement.GetAttributeFloat("mindensity", density);
                        float maxDensity = subElement.GetAttributeFloat("maxdensity", density);
                        if (minDensity < maxDensity)
                        {
                            var rand = new Random(ID);
                            density = MathHelper.Lerp(minDensity, maxDensity, (float)rand.NextDouble());
                        }

                        string collisionCategoryStr = subElement.GetAttributeString("collisioncategory", null);

                        Category collisionCategory = Physics.CollisionItem;
                        Category collidesWith = Physics.DefaultItemCollidesWith;
                        if ((Prefab.DamagedByProjectiles || Prefab.DamagedByMeleeWeapons || Prefab.DamagedByRepairTools) && Condition > 0)
                        {
                            //force collision category to Character to allow projectiles and weapons to hit
                            //(we could also do this by making the projectiles and weapons hit CollisionItem
                            //and check if the collision should be ignored in the OnCollision callback, but
                            //that'd make the hit detection more expensive because every item would be included)
                            collisionCategory = Physics.CollisionCharacter;
                            collidesWith |= Physics.CollisionProjectile;
                        }
                        if (collisionCategoryStr != null)
                        {                            
                            if (!Physics.TryParseCollisionCategory(collisionCategoryStr, out Category cat))
                            {
                                DebugConsole.ThrowError("Invalid collision category in item \"" + Name + "\" (" + collisionCategoryStr + ")",
                                    contentPackage: element.ContentPackage);
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
                    case "skillrequirementhint":
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
                        break;
                }
            }

            foreach (ItemComponent ic in components)
            {
                if (ic is Pickable pickable)
                {
                    foreach (var allowedSlot in pickable.AllowedSlots)
                    {
                        allowedSlots.Add(allowedSlot);
                    }
                }

                if (ic is Repairable repairable) { repairables.Add(repairable); }

                if (ic is IDrawableComponent && ic.Drawable)
                {
                    drawableComponents.Add(ic as IDrawableComponent);
                    hasComponentsToDraw = true;
                }

                if (ic.statusEffectLists == null) { continue; }
                if (ic.InheritStatusEffects)
                {
                    // Inherited status effects are added when the ItemComponent is initialized at ItemComponent.cs:332.
                    // Don't create duplicate effects here.
                    continue;
                }

                statusEffectLists ??= new Dictionary<ActionType, List<StatusEffect>>();

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

            hasInWaterStatusEffects = hasStatusEffectsOfType[(int)ActionType.InWater];
            hasNotInWaterStatusEffects = hasStatusEffectsOfType[(int)ActionType.NotInWater];

            if (body != null)
            {
                body.Submarine = submarine;
                originalWaterDragCoefficient = bodyElement.GetAttributeFloat("waterdragcoefficient", 5.0f);
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

            OwnInventories = GetComponents<ItemContainer>().Select(ic => ic.Inventory).ToImmutableArray();

            qualityComponent = GetComponent<Quality>();

            IsLadder = GetComponent<Ladder>() != null;
            IsSecondaryItem = IsLadder || GetComponent<Controller>() is { IsSecondaryItem: true };

            InitProjSpecific();

            if (callOnItemLoaded)
            {
                foreach (ItemComponent ic in components)
                {
                    ic.OnItemLoaded();
                }
            }

            var holdables = components.Where(c => c is Holdable);
            if (holdables.Count() > 1)
            {
                DebugConsole.AddWarning($"Item {Prefab.Identifier} has multiple {nameof(Holdable)} components ({string.Join(", ", holdables.Select(h => h.GetType().Name))}).",
                    Prefab.ContentPackage);
            }

            InsertToList();
            ItemList.Add(this);
            if (Prefab.IsDangerous) { _dangerousItems.Add(this); }
            if (Repairables.Any()) { _repairableItems.Add(this); }
            if (Prefab.SonarSize > 0.0f) { _sonarVisibleItems.Add(this); }
            if (Prefab.IsAITurretTarget) { _turretTargetItems.Add(this); }
            if (Prefab.Tags.Contains(Barotrauma.Tags.ChairItem)) { _chairItems.Add(this); }
            CheckCleanable();

            DebugConsole.Log("Created " + Name + " (" + ID + ")");

            if (Components.Any(ic => ic is Wire) && Components.All(ic => ic is Wire || ic is Holdable)) { isWire = true; }
            if (HasTag(Barotrauma.Tags.LogicItem)) { isLogic = true; }

            ApplyStatusEffects(ActionType.OnSpawn, 1.0f);

            // Set max condition multipliers from campaign settings for RecalculateConditionValues()
            if (GameMain.GameSession?.Campaign is CampaignMode campaign)
            {
                if (HasTag(Barotrauma.Tags.OxygenSource))
                {
                    conditionMultiplierCampaign *= campaign.Settings.OxygenMultiplier;
                }
                if (HasTag(Barotrauma.Tags.ReactorFuel))
                {
                    conditionMultiplierCampaign *= campaign.Settings.FuelMultiplier;
                }
            }
            condition *= conditionMultiplierCampaign;

            RecalculateConditionValues();

            if (callOnItemLoaded)
            {
                FullyInitialized = true;
            }
#if CLIENT
            Submarine.ForceVisibilityRecheck();
#endif
            HasBeenInstantiatedOnce = true; // Enable executing certain things only once
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
                if (property.Value.Attributes.OfType<Serialize>().None()) { continue; }
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
                //order the properties to get them to be applied in a consistent order (may matter for properties that are interconnected somehow, like IsOn/IsActive)
                foreach (KeyValuePair<Identifier, SerializableProperty> property in components[i].SerializableProperties.OrderBy(s => s.Key))
                {
                    if (property.Value.Attributes.OfType<Serialize>().None()) { continue; }
                    clone.components[i].SerializableProperties[property.Key].TrySetValue(clone.components[i], property.Value.GetValue(components[i]));
                }

                //clone requireditem identifiers
                foreach (var kvp in components[i].RequiredItems)
                {
                    for (int j = 0; j < kvp.Value.Count; j++)
                    {
                        if (!clone.components[i].RequiredItems.ContainsKey(kvp.Key) ||
                            clone.components[i].RequiredItems[kvp.Key].Count <= j)
                        {
                            continue;
                        }

                        clone.components[i].RequiredItems[kvp.Key][j].JoinedIdentifiers = 
                            kvp.Value[j].JoinedIdentifiers;
                    }
                }
            }

            if (FlippedX) { clone.FlipX(false); }
            if (FlippedY) { clone.FlipY(false); }

            // Flipping an item tampers with its rotation, so restore it
            clone.Rotation = Rotation;

            foreach (ItemComponent component in clone.components)
            {
                component.OnItemLoaded();
            }

            Dictionary<ushort, Item> clonedContainedItems = new();

            for (int i = 0; i < components.Count && i < clone.components.Count; i++)
            {
                ItemComponent component = components[i],
                              cloneComp = clone.components[i];

                if (component is not ItemContainer origInv ||
                    cloneComp is not ItemContainer cloneInv)
                {
                    continue;
                }

                foreach (var containedItem in origInv.Inventory.AllItems)
                {
                    var containedClone = (Item)containedItem.Clone();

                    cloneInv.Inventory.TryPutItem(containedClone, null);
                    clonedContainedItems.Add(containedItem.ID, containedClone);
                }
            }

            for (int i = 0; i < components.Count && i < clone.components.Count; i++)
            {
                ItemComponent component = components[i],
                              cloneComp = clone.components[i];
                if (component.GetType() == cloneComp.GetType())
                {
                    cloneComp.Clone(component);
                }
                if (component is CircuitBox origBox && cloneComp is CircuitBox cloneBox)
                {
                    cloneBox.CloneFrom(origBox, clonedContainedItems);
                }
            }

            clone.FullyInitialized = true;
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
                bool needsSoundUpdate = false;
#if CLIENT
                needsSoundUpdate = component.NeedsSoundUpdate();
#endif
                //component doesn't need to be updated if it isn't active, doesn't have a parent that could activate it, 
                //nor sounds or conditionals that would need to run
                if (!isActive && !component.UpdateWhenInactive && 
                    !needsSoundUpdate &&
                    component.Parent == null &&
                    (component.IsActiveConditionals == null || !component.IsActiveConditionals.Any()))
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
            CacheComponent(type);
            Type baseType = type.BaseType;
            while (baseType != null)
            {
                CacheComponent(baseType);
                baseType = baseType.BaseType;
            }
            
            void CacheComponent(Type t)
            {
                if (!componentsByType.TryGetValue(t, out List<ItemComponent> cachedComponents))
                {
                    cachedComponents = new List<ItemComponent>();
                    componentsByType.Add(t, cachedComponents);
                }
                if (!cachedComponents.Contains(component))
                {
                    cachedComponents.Add(component);
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
                Submarine.ForceVisibilityRecheck();
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
            if (componentsByType.TryGetValue(typeof(T), out List<ItemComponent> matchingComponents))
            {
                return (T)matchingComponents.First();
            }
            return null;
        }

        public IEnumerable<T> GetComponents<T>()
        {
            if (typeof(T) == typeof(ItemComponent))
            {
                return components.Cast<T>();
            }
            if (componentsByType.TryGetValue(typeof(T), out List<ItemComponent> matchingComponents))
            {
                return matchingComponents.Cast<T>();
            }
            return Enumerable.Empty<T>();
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
                    body.SetTransformIgnoreContacts(simPosition, rotation, setPrevTransform);
#if DEBUG
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set item transform", e);
                }
#endif
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(simPosition);

            rect.X = (int)MathF.Round(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)MathF.Round(displayPos.Y + rect.Height / 2.0f);

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

        /// <summary>
        /// Recheck if the item needs to be included in the list of cleanable items
        /// </summary>
        public void CheckCleanable()
        {
            var pickable = GetComponent<Pickable>();
            if (pickable != null && !pickable.IsAttached &&
                Prefab.PreferredContainers.Any() &&
                (container == null || container.HasTag(Barotrauma.Tags.AllowCleanup)))
            {
                if (!_cleanableItems.Contains(this))
                {
                    _cleanableItems.Add(this);
                }
            }
            else
            {
                _cleanableItems.Remove(this);
            }
        }

        public override void Move(Vector2 amount, bool ignoreContacts = true)
        {
            if (!MathUtils.IsValid(amount))
            {
                DebugConsole.ThrowError($"Attempted to move an item by an invalid amount ({amount})\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            base.Move(amount, ignoreContacts);

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

        public override Quad2D GetTransformedQuad()
            => Quad2D.FromSubmarineRectangle(rect).Rotated(-RotationRad);

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

        private void RefreshRootContainer()
        {
            Item newRootContainer = null;
            inWaterProofContainer = false;
            if (Container != null)
            {
                Item rootContainer = Container;
                inWaterProofContainer |= Container.WaterProof;

                while (rootContainer.Container != null)
                {
                    rootContainer = rootContainer.Container;
                    if (rootContainer == this)
                    {
                        DebugConsole.ThrowError($"Invalid container hierarchy: \"{Prefab.Identifier}\" was contained inside itself!\n{Environment.StackTrace.CleanupStackTrace()}");
                        rootContainer = null;
                        break;
                    }
                    inWaterProofContainer |= rootContainer.WaterProof;
                }
                newRootContainer = rootContainer;
            }
            if (newRootContainer != RootContainer)
            {
                RootContainer = newRootContainer;
                isActive = true;
                foreach (Item containedItem in ContainedItems)
                {
                    containedItem.RefreshRootContainer();
                }
            }
        }

        private void RefreshInWaterProofContainer()
        {
            inWaterProofContainer = false;
            if (container == null) { return; }
            if (container.WaterProof || container.inWaterProofContainer)
            {
                inWaterProofContainer = true;
            }
            foreach (Item containedItem in ContainedItems)
            {
                containedItem.RefreshInWaterProofContainer();
            }
        }

        /// <summary>
        /// Used by the AI to check whether they can (in principle) and are allowed (in practice) to interact with an object or not.
        /// Unlike CanInteractWith(), this method doesn't check the distance, the triggers, or anything like that.
        /// </summary>
        public bool HasAccess(Character character)
        {
            if (character.IsBot && IgnoreByAI(character)) { return false; }
            if (!IsInteractable(character)) { return false; }
            var itemContainer = GetComponent<ItemContainer>();
            if (itemContainer != null && !itemContainer.HasAccess(character)) { return false; }
            if (Container != null && !Container.HasAccess(character)) { return false; }
            if (GetComponent<Pickable>() is { CanBePicked: false }) { return false; }
            return true;
        }

        public bool IsOwnedBy(Entity entity) => FindParentInventory(i => i.Owner == entity) != null;

        public Entity GetRootInventoryOwner()
        {
            if (ParentInventory == null) { return this; }
            if (ParentInventory.Owner is Character) { return ParentInventory.Owner; }
            if (RootContainer?.ParentInventory?.Owner is Character) { return RootContainer.ParentInventory.Owner; }
            return RootContainer ?? this;
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
            foreach (var ownInventory in OwnInventories)
            {
                ownInventory.Container.SetContainedItemPositions();
            }
        }
        
        public void AddTag(string tag)
        {
            AddTag(tag.ToIdentifier());
        }

        public void AddTag(Identifier tag)
        {
            tags.Add(tag);
        }
        
        public void RemoveTag(Identifier tag)
        {
            if (!tags.Contains(tag)) { return; }
            tags.Remove(tag);
        }

        public bool HasTag(Identifier tag)
        {
            return tags.Contains(tag) || base.Prefab.Tags.Contains(tag);
        }

        public bool HasIdentifierOrTags(IEnumerable<Identifier> identifiersOrTags)
        {
            if (identifiersOrTags.Contains(Prefab.Identifier)) { return true; }
            return HasTag(identifiersOrTags);
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

        public IReadOnlyCollection<Identifier> GetTags()
        {
            return tags;
        }

        public bool HasTag(IEnumerable<Identifier> allowedTags)
        {
            foreach (Identifier tag in allowedTags)
            {
                if (HasTag(tag)) { return true; }
            }
            return false;
        }

        public bool ConditionalMatches(PropertyConditional conditional)
        {
            return ConditionalMatches(conditional, checkContainer: true);
        }

        public bool ConditionalMatches(PropertyConditional conditional, bool checkContainer)
        {
            if (checkContainer)
            {
                if (conditional.TargetContainer)
                {
                    if (conditional.TargetGrandParent)
                    {
                        return container?.container != null && container.container.ConditionalMatches(conditional, checkContainer: false);
                    }
                    return container != null && container.ConditionalMatches(conditional, checkContainer: false);
                }
            }
            if (string.IsNullOrEmpty(conditional.TargetItemComponent))
            {
                return conditional.Matches(this);
            }
            else
            {
                switch (conditional.ItemComponentComparison)
                {
                    case PropertyConditional.LogicalOperatorType.Or:
                    {
                        foreach (ItemComponent c in components)
                        {
                            if (MatchesComponent(c, conditional) && conditional.Matches(c))
                            {
                                // Some conditional matched, which is enough.
                                return true;
                            }
                        }
                        // Didn't find any matching components.
                        return false;
                    }
                    case PropertyConditional.LogicalOperatorType.And:
                    {
                        bool matchingComponentFound = false;
                        foreach (ItemComponent c in components)
                        {
                            if (!MatchesComponent(c, conditional)) { continue; }
                            matchingComponentFound = true;
                            if (!conditional.Matches(c))
                            {
                                // Some conditional didn't match -> fail.
                                return false;
                            }
                        }
                        // Found at least one matching component and no mismatches.
                        return matchingComponentFound;
                    }
                    default:
                        throw new NotSupportedException();
                }
                static bool MatchesComponent(ItemComponent comp, PropertyConditional cond) => comp.Name == cond.TargetItemComponent;
            }
        }

        /// <summary>
        /// Executes all StatusEffects of the specified type. Note that condition checks are ignored here: that should be handled by the code calling the method.
        /// </summary>
        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb limb = null, Entity useTarget = null, bool isNetworkEvent = false, Vector2? worldPosition = null)
        {
            if (!hasStatusEffectsOfType[(int)type]) { return; }

            foreach (StatusEffect effect in statusEffectLists[type])
            {
                ApplyStatusEffect(effect, type, deltaTime, character, limb, useTarget, isNetworkEvent, checkCondition: false, worldPosition);
            }
        }
        
        readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        public void ApplyStatusEffect(StatusEffect effect, ActionType type, float deltaTime, Character character = null, Limb limb = null, Entity useTarget = null, bool isNetworkEvent = false, bool checkCondition = true, Vector2? worldPosition = null)
        {
            if (effect.ShouldWaitForInterval(this, deltaTime)) { return; }
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
                        if (!OwnInventory.GetItemsAt(effect.TargetSlot).Contains(containedItem)) { continue; }
                    }

                    hasTargets = true;
                    targets.AddRange(containedItem.AllPropertyObjects);
                }
            }

            if (effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters) || effect.HasTargetType(StatusEffect.TargetType.NearbyItems))
            {
                effect.AddNearbyTargets(WorldPosition, targets);
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

            if (effect.HasTargetType(StatusEffect.TargetType.LinkedEntities))
            {
                foreach (var linkedEntity in linkedTo)
                {
                    if (linkedEntity is Item linkedItem)
                    {
                        targets.AddRange(linkedItem.AllPropertyObjects);
                    }
                    else if (linkedEntity is ISerializableEntity serializableEntity)
                    {
                        targets.Add(serializableEntity);
                    }
                }
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
                    targets.AddRange(character.AnimController.Limbs);
                }
                if (effect.HasTargetType(StatusEffect.TargetType.Limb) && limb == null && effect.targetLimbs != null)
                {
                    foreach (var characterLimb in character.AnimController.Limbs)
                    {
                        if (effect.targetLimbs.Contains(characterLimb.type)) { targets.Add(characterLimb); }
                    }
                }
            }
            if (effect.HasTargetType(StatusEffect.TargetType.Limb) && limb != null)
            {
                targets.Add(limb);
            }

            if (Container != null && effect.HasTargetType(StatusEffect.TargetType.Parent)) { targets.AddRange(Container.AllPropertyObjects); }
            
            effect.Apply(type, deltaTime, this, targets, worldPosition);            
        }


        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack,  Vector2 impulseDirection, float deltaTime, bool playSound = true)
        {
            if (Indestructible || InvulnerableToDamage) { return new AttackResult(); }

            float damageAmount = attack.GetItemDamage(deltaTime, Prefab.ItemDamageMultiplier);
            Condition -= damageAmount;

            if (damageAmount >= Prefab.OnDamagedThreshold)
            {
                ApplyStatusEffects(ActionType.OnDamaged, 1.0f);
            }

            return new AttackResult(damageAmount, null);
        }

        private void SetCondition(float value, bool isNetworkEvent, bool executeEffects = true)
        {
            if (!isNetworkEvent)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            }
            if (!MathUtils.IsValid(value)) { return; }
            if (Indestructible) { return; }
            if (InvulnerableToDamage && value <= condition) { return; }

            bool wasInFullCondition = IsFullCondition;

            float diff = value - condition;
            if (GetComponent<Door>() is Door door && door.IsStuck && diff < 0)
            {
                float dmg = -diff;
                // When the door is fully welded shut, reduce the welded state instead of the condition.
                float prevStuck = door.Stuck;
                door.Stuck -= dmg;
                if (door.IsStuck) { return; }
                // Reduce the damage by the amount we just adjusted the welded state by.
                float damageReduction = dmg - prevStuck;
                if (damageReduction < 0) { return; }
                value -= damageReduction;
            }

            condition = MathHelper.Clamp(value, 0.0f, MaxCondition);

            if (MathUtils.NearlyEqual(prevCondition, value, epsilon: 0.000001f)) { return; }

            RecalculateConditionValues();

            bool wasPreviousConditionChanged = false;
            if (condition == 0.0f && prevCondition > 0.0f)
            {
                //Flag connections to be updated as device is broken
                flagChangedConnections(connections);
#if CLIENT
                if (executeEffects)
                {                
                    foreach (ItemComponent ic in components)
                    {
                        ic.PlaySound(ActionType.OnBroken);
                        ic.StopSounds(ActionType.OnActive);
                    }
                }
                if (Screen.Selected == GameMain.SubEditorScreen) { return; }
#endif
                // Have to set the previous condition here or OnBroken status effects that reduce the condition will keep triggering the status effects, resulting in a stack overflow.
                SetPreviousCondition();
                if (executeEffects)
                {
                    ApplyStatusEffects(ActionType.OnBroken, 1.0f, null);
                }
            }
            else if (condition > 0.0f && prevCondition <= 0.0f)
            {
                //Flag connections to be updated as device is now working again
                flagChangedConnections(connections);
            }

            SetActiveSprite();

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                bool needsConditionUpdate = false;
                if (!MathUtils.NearlyEqual(lastSentCondition, condition) && (condition <= 0.0f || condition >= MaxCondition))
                {
                    //send the update immediately if the condition changed to max or min
                    sendConditionUpdateTimer = 0.0f;
                    needsConditionUpdate = true;
                }
                else if (Math.Abs(lastSentCondition - condition) > 1.0f || wasInFullCondition != IsFullCondition)
                {
                    needsConditionUpdate = true;
                }
                if (needsConditionUpdate && !itemsWithPendingConditionUpdates.Contains(this))
                {
                    itemsWithPendingConditionUpdates.Add(this);
                }
            }

            if (!wasPreviousConditionChanged)
            {
                SetPreviousCondition();
            }

            void SetPreviousCondition()
            {
                LastConditionChange = condition - prevCondition;
                ConditionLastUpdated = Timing.TotalTime;
                prevCondition = condition;
                wasPreviousConditionChanged = true;
            }

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
            MaxCondition = Prefab.Health * healthMultiplier * conditionMultiplierCampaign * maxRepairConditionMultiplier * (1.0f + GetQualityModifier(Items.Components.Quality.StatType.Condition));
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
            if (!itemsWithPendingConditionUpdates.Contains(this)) { return; }
            SendPendingNetworkUpdatesInternal();
            itemsWithPendingConditionUpdates.Remove(this);
        }

        private void SendPendingNetworkUpdatesInternal()
        {
            CreateStatusEvent(loadingRound: false);
            lastSentCondition = condition;
            sendConditionUpdateTimer = NetConfig.ItemConditionUpdateInterval;
        }

        public void CreateStatusEvent(bool loadingRound)
        {
            //A little hacky: clients aren't allowed to apply OnFire effects themselves, which means effects that rely on the "onfire" status tag
            //won't work properly. But let's notify clients of the item being on fire when it breaks, so they can e.g. make tanks explode.

            //An alternative could be to allow clients to run OnFire effects, but I suspect it could lead to desyncs if/when there's minor
            //discrepancies in the progress of the fires (which is most likely why running them was disabled on clients).
            if (GameMain.NetworkMember is { IsServer: true }  &&
                condition <= 0.0f &&
                StatusEffect.DurationList.Any(d => d.Targets.Contains(this) && d.Parent.HasTag(Barotrauma.Tags.OnFireStatusEffectTag)))
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new ApplyStatusEffectEventData(ActionType.OnFire));                
            }
            GameMain.NetworkMember.CreateEntityEvent(this, new ItemStatusEventData(loadingRound));
        }

        public static void UpdatePendingConditionUpdates(float deltaTime)
        {
            if (GameMain.NetworkMember is not { IsServer: true }) { return; }
            for (int i = 0; i < itemsWithPendingConditionUpdates.Count; i++)
            {
                var item = itemsWithPendingConditionUpdates[i];
                if (item == null || item.Removed)
                {
                    itemsWithPendingConditionUpdates.RemoveAt(i--);
                    continue;
                }
                if (item.Submarine is { Loading: true }) { continue; }

                item.sendConditionUpdateTimer -= deltaTime;
                if (item.sendConditionUpdateTimer <= 0.0f)
                {
                    item.SendPendingNetworkUpdatesInternal();
                    itemsWithPendingConditionUpdates.RemoveAt(i--);
                }
            }
        }

        private bool isActive = true;
        public bool IsInRemoveQueue;

        public override void Update(float deltaTime, Camera cam)
        {
            if (!isActive || IsLayerHidden || IsInRemoveQueue) { return; }

            if (impactQueue != null)
            {
                while (impactQueue.TryDequeue(out float impact))
                {
                    HandleCollision(impact);
                }
            }
            if (isDroppedStackOwner && body != null)
            {
                foreach (var item in droppedStack)
                {
                    if (item != this) 
                    {
                        item.body.Enabled = false;
                        item.body.SetTransformIgnoreContacts(this.SimPosition, body.Rotation); 
                    }
                }
            }

            if (aiTarget != null && aiTarget.NeedsUpdate)
            {
                aiTarget.Update(deltaTime);
            }

            var containedEffectType = parentInventory == null ? ActionType.OnNotContained : ActionType.OnContained;

            ApplyStatusEffects(ActionType.Always, deltaTime, character: (parentInventory as CharacterInventory)?.Owner as Character);
            ApplyStatusEffects(containedEffectType, deltaTime, character: (parentInventory as CharacterInventory)?.Owner as Character);

            for (int i = 0; i < updateableComponents.Count; i++)
            {
                ItemComponent ic = updateableComponents[i];

                bool isParentInActive = ic.InheritParentIsActive && ic.Parent is { IsActive: false };

                if (ic.IsActiveConditionals != null && !isParentInActive)
                {
                    if (ic.IsActiveConditionalComparison == PropertyConditional.LogicalOperatorType.And)
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
                    else
                    {
                        bool shouldBeActive = false;
                        foreach (var conditional in ic.IsActiveConditionals)
                        {
                            if (ConditionalMatches(conditional))
                            {
                                shouldBeActive = true;
                                break;
                            }
                        }
                        ic.IsActive = shouldBeActive;
                    }
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

            bool needsWaterCheck = hasInWaterStatusEffects || hasNotInWaterStatusEffects;
            if (body != null && body.Enabled)
            {
                System.Diagnostics.Debug.Assert(body.FarseerBody.FixtureList != null);

                if (Math.Abs(body.LinearVelocity.X) > 0.01f || Math.Abs(body.LinearVelocity.Y) > 0.01f || transformDirty)
                {
                    if (body.CollisionCategories != Category.None)
                    {
                        UpdateTransform();
                    }
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
                bool wasInWater = inWater;
                inWater = !inWaterProofContainer && IsInWater();
                if (inWater)
                {
                    //the item has gone through the surface of the water
                    if (!wasInWater && CurrentHull != null && body != null && body.LinearVelocity.Y < -1.0f)
                    {
                        Splash();
                        if (GetComponent<Projectile>() is not { IsActive: true })
                        {
                            //slow the item down (not physically accurate, but looks good enough)
                            body.LinearVelocity *= 0.2f;
                        }                   
                    }
                }
                if ((hasInWaterStatusEffects || hasNotInWaterStatusEffects) && condition > 0.0f)
                {
                    ApplyStatusEffects(inWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);
                }
                if (inWaterProofContainer && !hasNotInWaterStatusEffects)
                {
                    needsWaterCheck = false;
                }
            }

            if (!needsWaterCheck &&
                updateableComponents.Count == 0 && 
                (aiTarget == null || !aiTarget.NeedsUpdate) && 
                !hasStatusEffectsOfType[(int)ActionType.Always] && 
                !hasStatusEffectsOfType[(int)containedEffectType] && 
                (body == null || !body.Enabled))
            {
#if CLIENT
                positionBuffer.Clear();
#endif
                isActive = false;
            }
            
        }

        partial void Splash();

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
                body.SetTransformIgnoreContacts(body.SimPosition + prevSub.SimPosition, body.Rotation);
            }
            else if (Submarine != null && prevSub == null)
            {
                body.SetTransformIgnoreContacts(body.SimPosition - Submarine.SimPosition, body.Rotation);
            }
            else if (Submarine != null && prevSub != null && Submarine != prevSub)
            {
                body.SetTransformIgnoreContacts(body.SimPosition + prevSub.SimPosition - Submarine.SimPosition, body.Rotation);
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
            if (body.Mass <= 0.0f || body.Density <= 0.0f || body.BodyType != BodyType.Dynamic)
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
                if (forceFactor <= 0.0f) { return; }
            }

            bool moving = body.LinearVelocity.LengthSquared() > 0.001f;
            float volume = body.Mass / body.Density;
            if (moving)
            {
                //measure velocity from the velocity of the front of the item and apply the drag to the other end to get the drag to turn the item the "pointy end first"

                //a more "proper" (but more expensive) way to do this would be to e.g. calculate the drag separately for each edge of the fixture
                //but since we define the "front" as the "pointy end", we can cheat a bit by using that, and actually even make the drag appear more realistic in some cases
                //(e.g. a bullet with a rectangular fixture would be just as "aerodynamic" travelling backwards, but with this method we get it to turn the correct way)
                Vector2 localFront = body.GetLocalFront();
                Vector2 frontVel = body.FarseerBody.GetLinearVelocityFromLocalPoint(localFront);

                float speed = frontVel.Length();
                float drag = speed * speed * WaterDragCoefficient * volume * Physics.NeutralDensity;
                //very small drag on active projectiles to prevent affecting their trajectories much
                if (body.FarseerBody.IsBullet) { drag *= 0.1f; }
                Vector2 dragVec = -frontVel / speed * drag;

                //apply the force slightly towards the back of the item to make it turn the front first
                Vector2 back = body.FarseerBody.GetWorldPoint(-localFront * 0.01f);
                body.ApplyForce(dragVec, back);
            }

            //no need to apply buoyancy if the item is still and not light enough to float
            if (moving || body.Density <= 10.0f)
            {
                Vector2 buoyancy = -GameMain.World.Gravity * body.FarseerBody.GravityScale * forceFactor * volume * Physics.NeutralDensity;
                body.ApplyForce(buoyancy);
            }

            //apply simple angular drag
            if (Math.Abs(body.AngularVelocity) > 0.0001f)
            {
                body.ApplyTorque(body.AngularVelocity * volume * -0.1f);
            }
        }


        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (transformDirty) { return false; }

            var projectile = GetComponent<Projectile>();
            if (projectile != null)
            {
                // Ignore characters so that the impact sound only plays when the item hits a a wall or a door.
                // Projectile collisions are handled in Projectile.OnProjectileCollision(), so it should be safe to do this.
                if (f2.CollisionCategories == Physics.CollisionCharacter) { return false; }
                if (projectile.IgnoredBodies != null && projectile.IgnoredBodies.Contains(f2.Body)) { return false; }
                if (projectile.ShouldIgnoreSubmarineCollision(f2, contact)) { return false; }
            }

            if (GameMain.GameSession == null || GameMain.GameSession.RoundDuration > 1.0f)
            {
                contact.GetWorldManifold(out Vector2 normal, out _);
                if (contact.FixtureA.Body == f1.Body) { normal = -normal; }
                float impact = Vector2.Dot(f1.Body.LinearVelocity, -normal);
                impactQueue ??= new ConcurrentQueue<float>();
                impactQueue.Enqueue(impact);
            }

            isActive = true;

            return true;
        }

        private void HandleCollision(float impact)
        {
            OnCollisionProjSpecific(impact);
            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            if (ImpactTolerance > 0.0f && Math.Abs(impact) > ImpactTolerance && hasStatusEffectsOfType[(int)ActionType.OnImpact])
            {
                foreach (StatusEffect effect in statusEffectLists[ActionType.OnImpact])
                {
                    ApplyStatusEffect(effect, ActionType.OnImpact, deltaTime: 1.0f);
                }
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

        public override void FlipX(bool relativeToSub, bool force = false)
        {
            //call the base method even if the item can't flip, to handle repositioning when flipping the whole sub
            base.FlipX(relativeToSub);

            if (!Prefab.CanFlipX && !force) 
            {
                FlippedX = false;
                return; 
            }

            if (Prefab.AllowRotatingInEditor)
            {
                RotationRad = MathUtils.WrapAnglePi(-RotationRad);
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

        public override void FlipY(bool relativeToSub, bool force = false)
        {
            //call the base method even if the item can't flip, to handle repositioning when flipping the whole sub
            base.FlipY(relativeToSub);

            if (!Prefab.CanFlipY && !force)
            {
                FlippedY = false;
                return;
            }

            if (Prefab.AllowRotatingInEditor)
            {
                RotationRad = MathUtils.WrapAngleTwoPi(-RotationRad);
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
        /// Simpler, non-recursive version of <see cref="GetConnectedComponents{T}"/> for getting a directly connected component of the specified type wired to the connection panel.
        /// </summary>
        public T GetDirectlyConnectedComponent<T>(Func<Connection, bool> connectionFilter = null) where T : ItemComponent
        {
            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) { return null; }
            foreach (Connection c in connectionPanel.Connections)
            {
                if (connectionFilter != null && !connectionFilter(c)) { continue; }
                foreach (Connection recipient in c.Recipients)
                {
                    var component = recipient.Item.GetComponent<T>();
                    if (component != null)
                    {
                        return component;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Searches through the connection panel and looks for connections of specific type.
        /// Note: This function generates garbage and is too heavy to be used on each frame.
        /// </summary>
        public List<T> GetConnectedComponents<T>(bool recursive = false, bool allowTraversingBackwards = true, Func<Connection, bool> connectionFilter = null) where T : ItemComponent
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
                if (connectionFilter != null && !connectionFilter(c)) { continue; }
                foreach (Connection recipient in c.Recipients)
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
                if (!alreadySearched.Add(c)) { continue; }
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
            static IEnumerable<Connection> GetRecipients(Connection c)
            {
                foreach (Connection recipient in c.Recipients)
                {
                    yield return recipient;
                }
                //check circuit box inputs/outputs this connection is connected to
                foreach (var circuitBoxConnection in c.CircuitBoxConnections)
                {
                    yield return circuitBoxConnection.Connection;
                }
            }

            foreach (Connection recipient in GetRecipients(c))
            {
                if (alreadySearched.Contains(recipient)) { continue; }
                var component = recipient.Item.GetComponent<T>();                    
                if (component != null && !connectedComponents.Contains(component))
                {
                    connectedComponents.Add(component);
                }

                var circuitBox = recipient.Item.GetComponent<CircuitBox>();
                if (circuitBox != null)
                {
                    //if this is a circuit box, check what the connection is connected to inside the box
                    var potentialCbConnection = circuitBox.FindInputOutputConnection(recipient);
                    if (potentialCbConnection.TryUnwrap(out var cbConnection))
                    {
                        if (cbConnection is CircuitBoxInputConnection inputConnection)
                        {
                            foreach (var connectedTo in inputConnection.ExternallyConnectedTo)
                            {
                                if (alreadySearched.Contains(connectedTo.Connection)) { continue; }
                                CheckRecipient(connectedTo.Connection);
                            }
                        }
                        else
                        {
                            foreach (var connectedFrom in cbConnection.ExternallyConnectedFrom)
                            {
                                if (alreadySearched.Contains(connectedFrom.Connection) || !allowTraversingBackwards) { continue; }
                                CheckRecipient(connectedFrom.Connection);
                            }
                        }
                    }
                }
                CheckRecipient(recipient);

                void CheckRecipient(Connection recipient)
                {
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
            var campaignInteractionType = CampaignInteractionType;
#if SERVER
            var ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == user);
            if (ownerClient != null) 
            { 
                if (!campaignInteractionTypePerClient.TryGetValue(ownerClient, out campaignInteractionType))
                {
                    campaignInteractionType = CampaignMode.InteractionType.None;
                }
            }
#endif
            if (CampaignMode.BlocksInteraction(campaignInteractionType))
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
                if (ic is not Ladder && user.IsKeyDown(InputType.Aim))
                {
                    // Don't allow selecting items while aiming.
                    // This was added in cdc68f30. I can't remember what was the reason for it, but it might be related to accidental shots?
                    // However, we shouldn't disallow picking the ladders, because doing that would make the bot get stuck while trying to get on to ladders.
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
                    if (ic.CanBeSelected && ic is not Door) { selected = true; }
                }
            }
            if (ParentInventory?.Owner == user && 
                GetComponent<ItemContainer>() != null)
            {
                //can't select ItemContainers in the character's inventory
                //(the inventory is drawn by hovering the cursor over the inventory slot, not as a hovering interface on the screen)
                selected = false;
            }

            if (!picked) { return false; }

            OnInteract?.Invoke();

            if (user != null)
            {
                if (user.SelectedItem == this)
                {
                    if (user.IsKeyHit(InputType.Select) || forceSelectKey)
                    {
                        user.SelectedItem = null;
                    }
                }
                else if (user.SelectedSecondaryItem == this)
                {
                    if (user.IsKeyHit(InputType.Select) || forceSelectKey)
                    {
                        user.SelectedSecondaryItem = null;
                    }
                }
                else if (selected)
                {
                    if (IsSecondaryItem)
                    {
                        user.SelectedSecondaryItem = this;
                    }
                    else
                    {
                        user.SelectedItem = this;
                    }
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

        /// <param name="userForOnUsedEvent">User to pass to the OnUsed event. May need to be different than the user in cases like loaders using ammo boxes:
        /// the box is technically being used by the loader, and doesn't allow a character to use it, but we may still need to know which character caused
        /// the box to be used.</param>
        public void Use(float deltaTime, Character user = null, Limb targetLimb = null, Entity useTarget = null, Character userForOnUsedEvent = null)
        {
            if (RequireAimToUse && (user == null || !user.IsKeyDown(InputType.Aim)))
            {
                return;
            }

            if (condition <= 0.0f) { return; }
        
            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = user == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(user, isControlled)) { continue; }
                if (ic.Use(deltaTime, user))
                {
                    ic.WasUsed = true;
#if CLIENT
                    ic.PlaySound(ActionType.OnUse, user); 
#endif
                    ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, user, targetLimb, useTarget: useTarget, user: user);
                    ic.OnUsed.Invoke(new ItemComponent.ItemUseInfo(this, user ?? userForOnUsedEvent));
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
            if (condition <= 0.0f) { return; }

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
                    ic.ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, character: character, user: character, useTarget: character);

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

            if (Prefab.ContentPackage == ContentPackageManager.VanillaCorePackage &&
                /* we don't need info of every item, we can get a good sample size just by logging 5% */
                Rand.Range(0.0f, 1.0f) < 0.05f)
            {
                GameAnalyticsManager.AddDesignEvent("ApplyTreatment:" + Prefab.Identifier);
            }
#if CLIENT
            if (user == Character.Controlled)
            {
                if (HealingCooldown.IsOnCooldown) { return; }

                HealingCooldown.PutOnCooldown();
            }

            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(this, new TreatmentEventData(character, targetLimb));
                return;
            }
#endif
            bool remove = false;
            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(user, addMessage: user == Character.Controlled)) { continue; }

                bool success = Rand.Range(0.0f, 0.5f) < ic.DegreeOfSuccess(user);
                ActionType conditionalActionType = success ? ActionType.OnSuccess : ActionType.OnFailure;

#if CLIENT
                ic.PlaySound(conditionalActionType, user);
                ic.PlaySound(ActionType.OnUse, user);
#endif
                ic.WasUsed = true;

                ic.ApplyStatusEffects(conditionalActionType, 1.0f, character, targetLimb, useTarget: character, user: user);
                ic.ApplyStatusEffects(ActionType.OnUse, 1.0f, character, targetLimb, useTarget: character, user: user);

                if (GameMain.NetworkMember is { IsServer: true })
                {
                    GameMain.NetworkMember.CreateEntityEvent(this, new ApplyStatusEffectEventData(conditionalActionType, ic, character, targetLimb, useTarget: character));
                    GameMain.NetworkMember.CreateEntityEvent(this, new ApplyStatusEffectEventData(ActionType.OnUse, ic, character, targetLimb, useTarget: character));
                }

                if (ic.DeleteOnUse) { remove = true; }
            }

            if (user != null)
            {
                var abilityItem = new AbilityApplyTreatment(user, character, this, targetLimb);
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
                        body.SetTransformIgnoreContacts(dropper.SimPosition, 0.0f);
                    }
                }
            }

            foreach (ItemComponent ic in components) { ic.Drop(dropper, setTransform); }
            
            if (Container != null)
            {
                if (setTransform)
                {
                    SetTransform(Container.SimPosition, 0.0f);
                }
                Container.RemoveContained(this);
                Container = null;
            }
            
            if (ParentInventory != null)
            {
                ParentInventory.RemoveItem(this);
                ParentInventory = null;
            }

            //force updating the item's transform when it drops (making sure the current hull and submarine refresh)
            transformDirty = true;

            SetContainedItemPositions();
#if CLIENT
            Submarine.ForceVisibilityRecheck();
#endif
        }


        private List<Item> droppedStack;
        public IEnumerable<Item> DroppedStack => droppedStack ?? Enumerable.Empty<Item>();

        private bool isDroppedStackOwner;

        /// <summary>
        /// "Merges" the set of items so they behave as one physical object and can be picked up by clicking once.
        /// The items need to be instances of the same prefab and have a physics body.
        /// </summary>
        public void CreateDroppedStack(IEnumerable<Item> items, bool allowClientExecute)
        {
            if (!allowClientExecute && GameMain.NetworkMember is { IsClient: true }) { return; }

            int itemCount = items.Count();

            if (itemCount == 1) { return; }

            if (items.DistinctBy(it => it.Prefab).Count() > 1)
            {
                DebugConsole.ThrowError($"Attempted to create a dropped stack of multiple different items ({string.Join(", ", items.DistinctBy(it => it.Prefab))})\n{Environment.StackTrace}");
                return;
            }
            if (items.Any(it => it.body == null))
            {
                DebugConsole.ThrowError($"Attempted to create a dropped stack for an item with no body ({items.First().Prefab.Identifier})\n{Environment.StackTrace}");
                return;
            }
            if (items.None())
            {
                DebugConsole.ThrowError($"Attempted to create a dropped stack of an empty list of items.\n{Environment.StackTrace}");
                return;
            }

            int maxStackSize = items.First().Prefab.MaxStackSize;
            if (itemCount > maxStackSize)
            {
                for (int i = 0; i < MathF.Ceiling(itemCount / maxStackSize); i++)
                {
                    int startIndex = i * maxStackSize;
                    items.ElementAt(startIndex).CreateDroppedStack(items.Skip(startIndex).Take(maxStackSize), allowClientExecute);
                }
            }
            else
            {
                droppedStack ??= new List<Item>();
                foreach (Item item in items)
                {
                    if (!droppedStack.Contains(item))
                    {
                        droppedStack.Add(item);
                    }
                }
                SetDroppedStackItemStates();
#if SERVER
                if (GameMain.NetworkMember is { IsServer: true } server)
                {
                    server.CreateEntityEvent(this, new DroppedStackEventData(droppedStack));
                }
#endif
            }
        }

        private void RemoveFromDroppedStack(bool allowClientExecute)
        {
            if (!allowClientExecute && GameMain.NetworkMember is { IsClient: true }) { return; }            
            if (droppedStack == null) { return; }

            body.Enabled = ParentInventory == null;
            isDroppedStackOwner = false;
            droppedStack.Remove(this);
            SetDroppedStackItemStates();
            droppedStack = null;
#if SERVER
            if (GameMain.NetworkMember is { IsServer: true } server && !Removed)
            {
                server.CreateEntityEvent(this, new DroppedStackEventData(Enumerable.Empty<Item>()));
            }
#endif
        }

        private void SetDroppedStackItemStates()
        {
            if (droppedStack == null) { return; }
            bool isFirst = true;
            foreach (Item item in droppedStack)
            {
                item.droppedStack = droppedStack;
                item.isDroppedStackOwner = isFirst;
                if (item.body != null)
                {
                    item.body.Enabled = item.body.PhysEnabled = isFirst;
                    if (isFirst)
                    {
                        item.isActive = true;
                        item.body.ResetDynamics();
                    }
                }
                isFirst = false;
            }
        }


        /// <summary>
        /// Returns this item and all the other items in the stack (either in the same inventory slot, or the same dropped stack).
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Item> GetStackedItems()
        {
            yield return this;
            foreach (var stackedItem in DroppedStack)
            {
                if (stackedItem == this) { continue; }
                yield return stackedItem;
            }
            if (ParentInventory != null)
            {
                int slotIndex = ParentInventory.FindIndex(this);
                foreach (var stackedItem in ParentInventory.GetItemsAt(slotIndex))
                {
                    if (stackedItem == this) { continue; }
                    yield return stackedItem;
                }
            }
        }

        public void Equip(Character character)
        {
            if (Removed)
            {
                DebugConsole.ThrowError($"Tried to equip a removed item ({Name}).\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            foreach (ItemComponent ic in components) { ic.Equip(character); }

            CharacterHUD.RecreateHudTextsIfControlling(character);
        }

        public void Unequip(Character character)
        {
            foreach (ItemComponent ic in components) { ic.Unequip(character); }
            CharacterHUD.RecreateHudTextsIfControlling(character);
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
            ISerializableEntity entity = extraData.Entity;

            msg.WriteVariableUInt32((uint)allProperties.Count);

            if (property != null)
            {
                if (allProperties.Count > 1)
                {
                    int propertyIndex = allProperties.FindIndex(p => p.property == property && p.obj == entity);
                    if (propertyIndex < 0)
                    {
                        throw new Exception($"Could not find the property \"{property.Name}\" in \"{entity.Name ?? "null"}\"");
                    }
                    msg.WriteVariableUInt32((uint)propertyIndex);
                }

                object value = property.GetValue(entity);
                if (value is string stringVal)
                {
                    msg.WriteString(stringVal);
                }
                else if (value is Identifier idValue)
                {
                    msg.WriteIdentifier(idValue);
                }
                else if (value is float floatVal)
                {
                    msg.WriteSingle(floatVal);
                }
                else if (value is int intVal)
                {
                    msg.WriteInt32(intVal);
                }
                else if (value is bool boolVal)
                {
                    msg.WriteBoolean(boolVal);
                }
                else if (value is Color color)
                {
                    msg.WriteByte(color.R);
                    msg.WriteByte(color.G);
                    msg.WriteByte(color.B);
                    msg.WriteByte(color.A);
                }
                else if (value is Vector2 vector2)
                {
                    msg.WriteSingle(vector2.X);
                    msg.WriteSingle(vector2.Y);
                }
                else if (value is Vector3 vector3)
                {
                    msg.WriteSingle(vector3.X);
                    msg.WriteSingle(vector3.Y);
                    msg.WriteSingle(vector3.Z);
                }
                else if (value is Vector4 vector4)
                {
                    msg.WriteSingle(vector4.X);
                    msg.WriteSingle(vector4.Y);
                    msg.WriteSingle(vector4.Z);
                    msg.WriteSingle(vector4.W);
                }
                else if (value is Point point)
                {
                    msg.WriteInt32(point.X);
                    msg.WriteInt32(point.Y);
                }
                else if (value is Rectangle rect)
                {
                    msg.WriteInt32(rect.X);
                    msg.WriteInt32(rect.Y);
                    msg.WriteInt32(rect.Width);
                    msg.WriteInt32(rect.Height);
                }
                else if (value is Enum)
                {
                    msg.WriteInt32((int)value);
                }
                else if (value is string[] a)
                {
                    msg.WriteInt32(a.Length);
                    for (int i = 0; i < a.Length; i++)
                    {
                        msg.WriteString(a[i] ?? "");
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

            int propertyCount = (int)msg.ReadVariableUInt32();
            if (propertyCount != allProperties.Count)
            {
                throw new Exception($"Error in {nameof(ReadPropertyChange)}. The number of properties on the item \"{Prefab.Identifier}\" does not match between the server and the client. Server: {propertyCount}, client: {allProperties.Count}.");
            }

            int propertyIndex = 0;
            if (allProperties.Count > 1)
            {
                propertyIndex = (int)msg.ReadVariableUInt32();
            }

            if (propertyIndex >= allProperties.Count || propertyIndex < 0)
            {
                throw new Exception($"Error in {nameof(ReadPropertyChange)}. Property index out of bounds (item: {Prefab.Identifier}, index: {propertyIndex}, property count: {allProperties.Count}, in-game editable only: {inGameEditableOnly})");
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
                bool conditionAllowsEditing = true;
                if (property.GetAttribute<ConditionallyEditable>() is { } condition)
                {
                    conditionAllowsEditing = condition.IsEditable(this);
                }

                bool canAccess = false;
                if (Container?.GetComponent<CircuitBox>() is { } cb &&
                    Container.CanClientAccess(sender))
                {
                    //items inside circuit boxes are inaccessible by "normal" means,
                    //but the properties can still be edited through the circuit box UI
                    canAccess = !cb.IsLocked();
                }
                else
                {
                    canAccess = CanClientAccess(sender);
                }

                if (!canAccess || !conditionAllowsEditing)
                {
                    allowEditing = false;
                }
            }

            Type type = property.PropertyType;
            string logValue = "";
            if (type == typeof(string))
            {
                string val = msg.ReadString();
                var editableAttribute = property.GetAttribute<Editable>();
                if (editableAttribute != null && editableAttribute.MaxLength > 0 &&
                    val.Length > editableAttribute.MaxLength)
                {
                    val = val.Substring(0, editableAttribute.MaxLength);
                }
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
                    GameServer.Log($"{GameServer.CharacterLogName(sender.Character)} set the value \"{property.Name}\" of the item \"{Name}\" to \"{logValue}\".", ServerLog.MessageType.ItemInteraction);
                }, delay: 1.0f);
            }
#endif

            if (GameMain.NetworkMember is { IsServer: true } && parentObject is ISerializableEntity entity)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new ChangePropertyEventData(property, entity));
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
                                GameMain.NetworkMember.CreateEntityEvent(item, new ChangePropertyEventData(property, item));
                            }
                        }
                    }
                }
            }

            //store this at this point so we can tell the clients whether the effects had already been applied when the item was first loaded,
            //(in which case a client should not execute them when they spawn the item)
            item.OnInsertedEffectsAppliedOnPreviousRound = item.OnInsertedEffectsApplied;

            item.ParseLinks(element, idRemap);

            bool thisIsOverride = element.GetAttributeBool("isoverride", false);

            //if we're overriding a non-overridden item in a sub/assembly xml or vice versa, 
            //use the values from the prefab instead of loading them from the sub/assembly xml
            bool isItemSwap = appliedSwap != null;
            bool usePrefabValues = thisIsOverride != ItemPrefab.Prefabs.IsOverride(prefab) || isItemSwap;
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
                                item.AddUpgrade(new Upgrade(item, upgradePrefab, level, isItemSwap ? null : subElement));
                            }
                            else
                            {
                                DebugConsole.AddWarning($"An upgrade with identifier \"{upgradeIdentifier}\" on {item.Name} was not found. " +
                                                        "It's effect will not be applied and won't be saved after the round ends.");
                            }
                            break;
                        }
                    case "itemstats":
                        {
                            item.StatManager.Load(subElement);
                            break;
                        }
                    default:
                        {
                            ItemComponent component = unloadedComponents.Find(x => x.Name == subElement.Name.ToString());
                            if (component == null) { continue; }
                            component.Load(subElement, usePrefabValues, idRemap, isItemSwap);
                            unloadedComponents.Remove(component);
                            break;
                        }
                }
            }
            if (usePrefabValues && !isItemSwap)
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

            if (element.GetAttributeBool("markedfordeconstruction", false)) { _deconstructItems.Add(item); }

            float prevRotation = item.Rotation;
            if (element.GetAttributeBool("flippedx", false)) { item.FlipX(relativeToSub: false, force: true); }
            if (element.GetAttributeBool("flippedy", false)) { item.FlipY(relativeToSub: false, force: true); }
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

            Version savedVersion = submarine?.Info.GameVersion;
            if (element.Document?.Root != null && element.Document.Root.Name.ToString().Equals("gamesession", StringComparison.OrdinalIgnoreCase))
            {
                //character inventories are loaded from the game session file - use the version number of the saved game session instead of the sub
                //(the sub may have already been saved and up-to-date, even though the character inventories aren't)
                savedVersion = new Version(element.Document.Root.GetAttributeString("version", "0.0.0.0"));
            }

            float prevCondition = item.condition;
            if (savedVersion != null)
            {
                SerializableProperty.UpgradeGameVersion(item, item.Prefab.ConfigElement, savedVersion);
            }

            if (element.GetAttribute("conditionpercentage") != null)
            {
                item.condition = element.GetAttributeFloat("conditionpercentage", 100.0f) / 100.0f * item.MaxCondition;
            }
            else
            {
                //backwards compatibility
                item.condition = element.GetAttributeFloat("condition", item.condition);
                //if the item was in full condition considering the unmodified health
                //(not taking possible HealthMultipliers added by mods into account),
                //make sure it stays in full condition
                if (item.condition > 0)
                {
                    bool wasFullCondition = prevCondition >= item.Prefab.Health;
                    if (wasFullCondition)
                    {
                        item.condition = item.MaxCondition;
                    }
                    item.condition = MathHelper.Clamp(item.condition, 0, item.MaxCondition);
                }
            }
            item.lastSentCondition = item.prevCondition = item.condition;
            item.RecalculateConditionValues();
            item.SetActiveSprite();

            foreach (ItemComponent component in item.components)
            {
                if (component.Parent != null && component.InheritParentIsActive) { component.IsActive = component.Parent.IsActive; }
                component.OnItemLoaded();
            }

            item.FullyInitialized = true;

            return item;
        }

        /// <summary>
        /// Replaces this item with another one, intended to be called on the client from network data.
        /// Does not swap connected items since it is assumed that the server will send separate entity events for them.
        /// </summary>
        /// <param name="replacement">The item prefab to replace this one with.</param>
        /// <param name="newId">ID to assign the newly created item. Should match the one created on server.</param>
        private void ReplaceFromNetwork(ItemPrefab replacement, ushort newId)
            => Replace(replacement, newId: Option.Some(newId), createEntityEvent: false);

        /// <summary>
        /// Replaces this item with another one, creating a network event in multiplayer and
        /// swapping the connected items if the replacement is a valid swap.
        /// </summary>
        /// <param name="replacement">The item prefab to replace this one with.</param>
        public void ReplaceWithLinkedItems(ItemPrefab replacement)
            => Replace(replacement, newId: Option.None, createEntityEvent: true);

        /// <summary>
        /// Replaces this item with another one, inheriting properties that are meant to be inherited when swapping items like turrets.
        /// Should not be called by itself but by <see cref="ReplaceWithLinkedItems(ItemPrefab)"/> and <see cref="ReplaceFromNetwork(ItemPrefab, ushort)"/>.
        /// </summary>
        /// <param name="replacement">The item prefab to replace this one with.</param>
        /// <param name="newId">
        /// ID of the new item to swap to or Option.None to automatically assign one.
        /// </param>
        /// <param name="createEntityEvent">
        /// Should an entity event be created to notify clients about the item swap.
        /// </param>
        /// <remarks>
        /// When ID is set to Option.None
        /// the linked items will recursively be swapped to the new ones too, but not when the ID is set to a specific value.
        /// this is because it is assumed that the function is run on the client when the ID is known and the server will send
        /// separate entity events for the linked items.
        /// </remarks>
        private void Replace(ItemPrefab replacement, Option<ushort> newId, bool createEntityEvent)
        {
            Vector2 centerPos = Position;

            var newItem = new Item(replacement, Position, Submarine, id: newId.Fallback(NullEntityID))
            {
                SpriteDepth = SpriteDepth,
                SpriteColor = SpriteColor,
                Rotation = Rotation
            };

            float scaleRelativeToPrefab = Scale / Prefab.Scale;
            newItem.Scale *= scaleRelativeToPrefab;

            if (Prefab.SwappableItem != null && replacement.SwappableItem != null)
            {
                Vector2 oldRelativeOrigin = (Prefab.SwappableItem.SwapOrigin - Prefab.Size / 2) * scale;
                oldRelativeOrigin.Y = -oldRelativeOrigin.Y;
                oldRelativeOrigin = MathUtils.RotatePoint(oldRelativeOrigin, -RotationRad);
                Vector2 oldOrigin = centerPos + oldRelativeOrigin;

                Vector2 relativeOrigin = (Prefab.SwappableItem.SwapOrigin - Prefab.Size / 2) * Scale;
                relativeOrigin.Y = -relativeOrigin.Y;
                relativeOrigin = MathUtils.RotatePoint(relativeOrigin, -RotationRad);
                Vector2 origin = new Vector2(rect.X + rect.Width / 2f, rect.Y - rect.Height / 2f) + relativeOrigin;

                newItem.rect.Location -= (origin - oldOrigin).ToPoint();
            }

            if (!string.IsNullOrEmpty(Prefab.SwappableItem?.SpawnWithId))
            {
                var newContainer = newItem.GetComponent<ItemContainer>();
                if (newContainer != null)
                {
                    newContainer.SpawnWithId = Prefab.SwappableItem.SpawnWithId;
                }
            }

            foreach (ItemComponent originalComponent in components)
            {
                var originalComponents = components.Where(c => c.GetType() == originalComponent.GetType()).ToList();
                var newComponents = newItem.components.Where(c => c.GetType() == originalComponent.GetType()).ToList();
                int originalIndex = originalComponents.IndexOf(originalComponent);
                if (originalIndex >= newComponents.Count)
                {
                    //original item has components the new one doesn't -> no need to copy anything from them
                    continue;
                }
                var newComponent = newComponents[originalIndex];

                foreach (var originalProperty in originalComponent.SerializableProperties)
                {
                    if (originalProperty.Value.OverridePrefabValues || originalProperty.Value.GetAttribute<Editable>() is { TransferToSwappedItem: true })
                    {
                        newComponent.SerializableProperties[originalProperty.Key].TrySetValue(newComponent, originalProperty.Value.GetValue(originalComponent));
                    }
                }
            }

            foreach (var linked in linkedTo)
            {
                newItem.linkedTo.Add(linked);
                if (linked.linkedTo.Contains(this))
                {
                    linked.linkedTo.Add(newItem);
                }
            }

            var thisConnectionPanel = GetComponent<ConnectionPanel>();
            var newConnectionPanel = newItem.GetComponent<ConnectionPanel>();
            if (thisConnectionPanel != null && newConnectionPanel != null)
            {
                foreach (var connection in thisConnectionPanel.Connections)
                {
                    var newConnection = newConnectionPanel.Connections.FirstOrDefault(c => c.Name == connection.Name);
                    if (newConnection == null) { continue; }
                    foreach (var wire in connection.Wires)
                    {
                        int connectionIndex = wire.Connections.IndexOf(connection);
                        wire.RemoveConnection(this);
                        wire.Connect(newConnection, connectionIndex, addNode: false);
                        newConnection.ConnectWire(wire);
                    }
                }
            }

            if (newId.IsNone() && replacement.SwappableItem != null)
            {
                var connectedItemsToSwap = newItem.GetConnectedItemsToSwap(replacement.SwappableItem);
                foreach (var kvp in connectedItemsToSwap)
                {
                    Item itemToSwap = kvp.Key;
                    ItemPrefab swapTo = kvp.Value;
                    itemToSwap.Replace(swapTo, newId: Option.None, createEntityEvent);
                }
            }

#if SERVER
            if (createEntityEvent && GameMain.Server is { } server)
            {
                server.CreateEntityEvent(this, new SwapItemEventData(newItem.Prefab, newItem.ID));
            }
#endif
            Remove();
        }

        public Dictionary<Item, ItemPrefab> GetConnectedItemsToSwap(SwappableItem swappingTo)
        {
            Dictionary<Item, ItemPrefab> itemsToSwap = new();
            foreach (var (requiredTag, swapTo) in swappingTo.ConnectedItemsToSwap)
            {
                if (MapEntityPrefab.FindByIdentifier(swapTo) is not ItemPrefab replacement) { continue; }

                foreach (var linked in linkedTo)
                {
                    if (linked is Item linkedItem && linkedItem.HasTag(requiredTag))
                    {
                        itemsToSwap.Add(linkedItem, replacement);
                    }
                }
                if (GetComponent<ConnectionPanel>() is ConnectionPanel connectionPanel)
                {
                    foreach (Connection c in connectionPanel.Connections)
                    {
                        foreach (var connectedComponent in GetConnectedComponentsRecursive<ItemComponent>(c))
                        {
                            if (!itemsToSwap.ContainsKey(connectedComponent.Item) && 
                                connectedComponent.Item.HasTag(requiredTag))
                            {
                                itemsToSwap.Add(connectedComponent.Item, replacement);
                            }
                        }
                    }
                }
            }
            return itemsToSwap;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Item");

            element.Add(
                new XAttribute("name", Prefab.OriginalName),
                new XAttribute("identifier", Prefab.Identifier),
                new XAttribute("ID", ID),
                new XAttribute("markedfordeconstruction", _deconstructItems.Contains(this)));

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

            if (!MathUtils.NearlyEqual(healthMultiplier, 1.0f))
            {
                element.Add(new XAttribute("healthmultiplier", HealthMultiplier.ToString("G", CultureInfo.InvariantCulture)));
            }

            Item rootContainer = RootContainer ?? this;
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

            statManager?.Save(element);

            element.Add(new XAttribute("conditionpercentage", ConditionPercentage.ToString("G", CultureInfo.InvariantCulture)));

            var conditionAttribute = element.GetAttribute("condition");
            if (conditionAttribute != null) { conditionAttribute.Remove(); }            

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
            RemoveFromLists();

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
                if (character.SelectedItem == this) { character.SelectedItem = null; }
                if (character.SelectedSecondaryItem == this) { character.SelectedSecondaryItem = null; }
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

            RemoveFromLists();

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

        private void RemoveFromLists()
        {
            ItemList.Remove(this);
            _dangerousItems.Remove(this);
            _repairableItems.Remove(this);
            _sonarVisibleItems.Remove(this);
            _cleanableItems.Remove(this);
            _deconstructItems.Remove(this);
            _turretTargetItems.Remove(this);
            _chairItems.Remove(this);
            RemoveFromDroppedStack(allowClientExecute: true);
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
        public Limb TargetLimb { get; set; }

        public AbilityApplyTreatment(Character user, Character target, Item item, Limb limb)
        {
            Character = target;
            User = user;
            Item = item;
            TargetLimb = limb;
        }
    }
}
