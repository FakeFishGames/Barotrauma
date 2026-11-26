#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    internal enum PlantItemType
    {
        Seed,
        Fertilizer
    }

    internal enum PickerAction
    {
        UprootOrPlant,
        EmptyPlanter
    }

    internal readonly struct SuitablePlantItem
    {
        public readonly Item? Item;
        public readonly PlantItemType? Type;
        public readonly string? ProgressBarMessage;
        public readonly string? ErrorMessage;

        public SuitablePlantItem(Item item, PlantItemType type, string progressBarMessage, string? errorMessage = null)
        {
            Item = item;
            Type = type;
            ProgressBarMessage = progressBarMessage;
            ErrorMessage = errorMessage;
        }

        public SuitablePlantItem(string? errorMessage = null)
        {
            ErrorMessage = errorMessage;
        }

        public bool IsNull() => Item == null;
    }

    public struct PlantSlot
    {
        public Vector2 Offset;
        public int Size;

        public PlantSlot(ContentXElement element)
        {
            Offset = element.GetAttributeVector2("offset", Vector2.Zero);
            Size = element.GetAttributeInt("size", 1);
        }

        public PlantSlot(Vector2 offset, int size)
        {
            Offset = offset;
            Size = size;
        }
    }

    public partial class Planter : Pickable, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        public static readonly PlantSlot NullSlot = new PlantSlot();
        public readonly Dictionary<int, PlantSlot> PlantSlots = new Dictionary<int, PlantSlot>();

        private static readonly SuitablePlantItem NullItem = new SuitablePlantItem();
        private const string MsgInspectPlant = "ItemMsgInspectPlant";
        private const string MsgPlanterTooSmall = "ItemMsgPlanterTooSmall";
        private const string MsgUprooting = "progressbar.uprooting";
        private const string MsgPlanting = "progressbar.planting";
        private const string MsgEmptying = "progressbar.emptying";

        private PickerAction pendingAction = PickerAction.UprootOrPlant;
        public static float GrowthTickDelay = 1f; // 1 second
        public static float? DebugGrowthMultiplier;

        public enum PlanterSyncEvent : byte
        {
            Uproot = 0,
            Plant = 1,
            Settings = 2,
            Empty = 3,
        }

        public readonly record struct EventData(PlanterSyncEvent SyncEvent, Item? PlantedSeed = null) : IEventData;

        /// <summary>
        /// Workaround to the Planter component being Pickable despite all of the vanilla planter items also having a Holdable component.
        /// That means that even if we'd make the Holdable only go to the hand slots, it would still go to the "Any" slots too because of this Planter component that would by default allow the item go to "Any".
        /// We could also make Planter not be a subclass of Pickable, but it could cause backwards compatibility issues, and the "picking" action is used for uprooting.
        /// </summary>
        protected override InvSlotType DefaultAllowedSlot => InvSlotType.None;
        
        // Temporary storage for an item dropped into this planter, as it needs to be re-validated at the end of the planting process
        private Item? dragDroppedItem = null;

        public bool HasBeenPlaced => Item.GetComponent<Holdable>() is { Attachable: true, Attached: true };

        private float water;

        [Serialize(0f, IsPropertySaveable.Yes, "Current water amount in the planter."), Editable]
        public float CurrentWater
        {
            get => water;
            set => water = Math.Clamp(value, 0, MaxWater);
        }
        
        [Serialize(100f, IsPropertySaveable.No, "Maximum water the planter can hold.")]
        public float MaxWater { get; set; }

        [Serialize(33f, IsPropertySaveable.No, "How much water per second seeps into the planter when it's immersed in seawater, in the same units as the planter's water capacity is defined. If this is the same as MaxWater, it takes one second of immersion to completely fill the planter with water.")]
        public float WateringWhenFlooded { get; set; }

        [Serialize(false, IsPropertySaveable.No, "When true, the planter's UI panel shows a slider for TargetWaterLevel. Note that it's up to XML to act on that. Defaults to false.")]
        public bool ShowTargetWaterLevel { get; set; }

        private float targetWaterLevel;
        [Serialize(50f, IsPropertySaveable.Yes, "Target water level for the planter, as a percentage of MaxWater (0-100). Note that it's up to XML to act on this, this property only makes sure the value gets stored and synced.")]
        public float TargetWaterLevel
        {
            get => targetWaterLevel;
            set => targetWaterLevel = Math.Clamp(value, 0f, 100f);
        }

        [Serialize(false, IsPropertySaveable.No, "True while the planter is over its TargetWaterLevel. Can be used in XML to trigger water level regulation.")]
        public bool WaterAboveTarget
        {
            // TargetWaterLevel is a percentage (0-100), so let's scale it to an absolute value for comparison
            get => CurrentWater > TargetWaterLevel * 0.01f * MaxWater;
            set { /* the value is computed and only meant to be read */ }
        }

        [Serialize(0.025f, IsPropertySaveable.No, "How much salt seeps into the planter per second when it's immersed in seawater.")]
        public float SaltAbsorptionRate { get; set; }
        
        public const float SeawaterSalinity = 3.5f;
        
        private float salinity;

        [Serialize(0f, IsPropertySaveable.Yes, "Salinity percentage in the planter.")]
        public float Salinity
        {
            get => salinity;
            set
            {
                float newSalinity = Math.Clamp(value, 0, SeawaterSalinity);
                if (salinity >= 0.05f && newSalinity < 0.05f) // when the rounded value decreases from 0.1% to 0.0%
                {
                    salinity = 0;
                }
                else
                {
                    salinity = newSalinity;
                }
            }
        }

        /// Randomized to avoid all planters checking at the same time
        private float PlantLightRecalculationInterval => Rand.Range(1.5f, 2.5f);

        private float cachedPlantLightAmount;
        private float plantLightRecalculationTimer;
        private Hull? lastKnownHull;

        /// <summary>
        /// Cached average midpoint of all vines across all growables in this planter,
        /// as a local offset from the planter's position. Updated when vines are added.
        /// </summary>
        private Vector2 growablesAverageMidpoint;

#if CLIENT && DEBUG
        /// <summary>
        /// Debug lines for visualizing plant light raycasts.
        /// </summary>
        public List<(Vector2 From, Vector2 To, bool Blocked)> DebugRaycastLines = [];
#endif

        /// <summary>
        /// Amount of plant light available for this planter's growables.
        /// Uses raycast-based line-of-sight checks from average growable position to sources of plant light.
        /// Clamped to get a 0-100% value for simplicity, though actually the light amounts are absolute and arbitrary.
        /// </summary>
        public float PlantLightAmount
        {
            get
            {
                // If the timer hasn't expired, return cached value
                if (plantLightRecalculationTimer > 0 && Item.CurrentHull == lastKnownHull)
                {
                    return cachedPlantLightAmount;
                }

                // Recalculate
                cachedPlantLightAmount = CalculatePlantLight();
                plantLightRecalculationTimer = PlantLightRecalculationInterval;
                lastKnownHull = Item.CurrentHull;

                return cachedPlantLightAmount;
            }
        }

        /// <summary>
        /// Calculates the total plant light reaching this planter using LoS raycast checks.
        /// </summary>
        private float CalculatePlantLight()
        {
            if (Item.CurrentHull == null) { return 0f; }

#if CLIENT && DEBUG
            DebugRaycastLines.Clear();
#endif

            Vector2 growablesWorldPosition = Item.WorldPosition + growablesAverageMidpoint;
            Vector2 growablesSimPosition = Item.SimPosition + ConvertUnits.ToSimUnits(growablesAverageMidpoint);

            float totalLight = 0f;
            foreach (var lightComponent in LightComponent.ActivePlantLightSources)
            {
                if (!lightComponent.IsEmittingPlantLight ||
                    lightComponent.Item.Submarine != Item.Submarine)
                {
                    continue;
                }

                float distance = Vector2.Distance(growablesWorldPosition, lightComponent.LightWorldPosition);
                if (distance > lightComponent.Range)
                {
                    continue;
                }

                Vector2 lightSimOffset = ConvertUnits.ToSimUnits(lightComponent.LightWorldPosition - growablesWorldPosition);
                bool hasLoS = Submarine.CheckVisibility(growablesSimPosition, growablesSimPosition + lightSimOffset) == null;

#if CLIENT && DEBUG
                DebugRaycastLines.Add((growablesWorldPosition, lightComponent.LightWorldPosition, !hasLoS));
#endif

                if (!hasLoS)
                {
                    continue;
                }

                // Calculate light contribution with linear distance falloff (full at 0 distance, zero at max range)
                totalLight += lightComponent.PlantLight * (1f - (distance / lightComponent.Range));

#if !DEBUG
                // Already at 100%, no point checking more lights (except in debug mode, to draw them)
                if (totalLight >= 100f) { return 100f; }
#endif
            }

            return Math.Clamp(totalLight, 0f, 100f);
        }

        /// <summary>
        /// Recalculates the average midpoint of all vines across all growables, to get
        /// the approximate midpoint of what's actually growing in the planter.
        /// </summary>
        public void UpdatePlantMidpoint()
        {
            Vector2 sum = Vector2.Zero;
            int count = 0;

            for (int i = 0; i < GrowableSeeds.Length; i++)
            {
                if (GrowableSeeds[i] is not Growable growable) { continue; }

                Vector2 slotOffset = PlantSlots.GetValueOrDefault(i, NullSlot).Offset;

                if (growable.Vines.Count == 0)
                {
                    // No vines yet, just use the slot offset
                    sum += slotOffset;
                    count++;
                }
                else
                {
                    foreach (var vine in growable.Vines)
                    {
                        sum += vine.Position + slotOffset;
                        count++;
                    }
                }
            }

            growablesAverageMidpoint = count > 0 ? sum / count : Vector2.Zero;
        }

        private float fertilizer;

        [Serialize(0f, IsPropertySaveable.Yes, "How much fertilizer the planter has."), Editable]
        public float Fertilizer
        {
            get => fertilizer;
            set => fertilizer = Math.Clamp(value, 0, FertilizerCapacity);
        }

        [Serialize(100f, IsPropertySaveable.No, "How much fertilizer can the planter hold.")]
        public float FertilizerCapacity { get; set; }
        
        /// <summary>
        /// Current water level as a normalized value (0-1).
        /// </summary>
        public float NormalizedWaterLevel => CurrentWater / MaxWater;
        
        /// <summary>
        /// Current fertilizer level as a normalized value (0-1).
        /// </summary>
        public float NormalizedFertilizerLevel => Fertilizer / FertilizerCapacity;

        public Growable?[] GrowableSeeds = [];
        
        private readonly List<RelatedItem> SuitableSeeds = new List<RelatedItem>();
        private ItemContainer? container;
        private float growthTickTimer;

        private Powered? poweredComponent;

        /// <summary>
        /// Whether the planter has sufficient input voltage to operate (wire output, interfacing with planter goggles, indicator light).
        /// For backwards compatibility, falls back to true if the planter has no "Powered" component.
        /// </summary>
        public bool HasPower => poweredComponent?.HasPower ?? true;

        // Caches for planter wiring
        private bool hasConnectionPanel;
        private int prevSentWaterPercent;
        private string? waterPercentSignal;
        private int prevSentFertilizerPercent;
        private string? fertilizerPercentSignal;
        private int prevSentSalinityFixed;
        private string? salinitySignal;
        private int prevSentLight;
        private string? lightSignal;
        private int prevSentHealthPercent;
        private string? healthPercentSignal;

        // We don't want the seeds to be transferred to a new submarine as seeds are not supposed to leave the container after they have been planted.
        public override bool DontTransferInventoryBetweenSubs => true;

        public Planter(Item item, ContentXElement element) : base(item, element)
        {
            canBePicked = true;
            canBeSelected = true;
            SerializableProperty.DeserializeProperties(this, element);
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "plantslot":
                        PlantSlots.Add(subElement.GetAttributeInt("slot", 0), new PlantSlot(subElement));
                        break;
                    case "suitableseed":
                        SuitableSeeds.Add(RelatedItem.Load(subElement, true, item.Name));
                        break;
                }
            }
            
            InitProjSpecific(element);
        }
        
        partial void InitProjSpecific(ContentXElement element);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            IsActive = true;
            container = item.GetComponent<ItemContainer>();
            hasConnectionPanel = item.GetComponent<ConnectionPanel>() != null;
            poweredComponent = item.GetComponents<Powered>().FirstOrDefault(p => p.GetType() == typeof(Powered));
            GrowableSeeds = new Growable[container.Capacity];
            if (container.Capacity != PlantSlots.Count)
            {
                DebugConsole.ThrowError($"Planter {Item.Prefab.Name}'s ItemContainer capacity doesn't match its PlantSlot count");
            }
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            for (int i = 0; i < container?.Inventory.Capacity; i++)
            {
                if (i < 0 || GrowableSeeds.Length <= i) { continue; }

                Item containedItem = container.Inventory.GetItemAt(i);
                Growable? growable = containedItem?.GetComponent<Growable>();

                if (growable != null)
                {
                    GrowableSeeds[i] = growable;
                }
            }

            UpdatePlantMidpoint();
        }

        public override bool HasRequiredItems(Character character, bool addMessage, LocalizedString? msg = null)
        {
            if (container?.Inventory == null) { return false; }

            // For placed planters, Select (click) always opens the inspection panel
            if (HasBeenPlaced)
            {
                canBeSelected = true;
                canBePicked = false;
                Msg = MsgInspectPlant;
                ParseMsg();
                return true;
            }

            // Non-placed planters behave as a regular pickable items
            canBeSelected = false;
            canBePicked = true;
            Msg = string.Empty;
            ParseMsg();
            return false;
        }

        public override bool Pick(Character character)
        {
            if (canBeSelected) // Defaults to picking, but if selecting then don't do both
            {
                return false;
            }
            return StartUprootingOrPlanting(character);
        }

        /// <summary>
        /// Attempts to plant a seed dragged and dropped into the planter.
        /// Validates that the seed matches the planter's conditions, so we can show correct feedback in the UI.
        /// </summary>
        public bool TryPlantItem(Item? potentialGrowable, Character? user)
        {
            if (potentialGrowable == null || user == null) { return false; }
            if (!HasBeenPlaced) { return false; }

            // If a planting attempt is already in progress, it should be the one to finish, skip this one
            if (IsPickInProgress) { return false; }

            // Normally the validation expects the item to be in the character's hands, but now we give it as an override
            SuitablePlantItem plantItem = GetSuitableItem(user, overridingItem: potentialGrowable);
            if (plantItem.IsNull())
            {
                // Show error message if there is one (eg. planter too small)
                if (plantItem.ErrorMessage is string errorMessage)
                {
#if CLIENT
                    user.AddMessage(
                        TextManager.Get(errorMessage).Value,
                        GUIStyle.Red,
                        playSound: false);
#endif
                }
                return false;
            }

            // Stash the dragdropped seed so OnPicked can re-validate the same item when the planting progress bar finishes
            dragDroppedItem = potentialGrowable;

#if CLIENT
            // If this is the client, tell the server which seed to plant
            if (GameMain.NetworkMember is { IsClient: true })
            {
                item.CreateClientEvent(this, new EventData(PlanterSyncEvent.Plant, plantItem.Item));
            }
#endif

            // If there is a nonzero pick time, Pick returns false even when it started fine
            bool pickSucceeded = StartUprootingOrPlanting(user, knownSuitableSeed: plantItem);
            bool isTimedPick = PickingTime > 0f;
            return pickSucceeded || isTimedPick;
        }

        public bool CanPlantItem(Item? potentialGrowable, Character? user)
        {
            if (potentialGrowable == null || user == null) { return false; }
            if (!HasBeenPlaced) { return false; }
            return !(GetSuitableItem(user, overridingItem: potentialGrowable).IsNull());
        }
        
        /// <summary>
        /// OnPicked (the picking action) is implemented as either uprooting the plant or planting a seed, depending on whether the character has a seed in hand.
        /// This method will trigger that method using the usual ItemComponent picking logic, either directly or with the picking delay.
        /// </summary>
        private bool StartUprootingOrPlanting(Character? character, SuitablePlantItem? knownSuitableSeed = null)
        {
            if (character == null) { return false; }

            pendingAction = PickerAction.UprootOrPlant;

            SuitablePlantItem plantItem = knownSuitableSeed ?? GetSuitableItem(character);

            PickingMsg = plantItem.IsNull() ? MsgUprooting : plantItem.ProgressBarMessage;
            return base.Pick(character);
        }

        private bool StartEmptying(Character? character)
        {
            if (character == null) { return false; }

            pendingAction = PickerAction.EmptyPlanter;
            PickingMsg = MsgEmptying;
            return base.Pick(character);
        }
        
        /// <summary>
        /// OnPicked (the picking action) is implemented as either uprooting the plant or planting a seed, depending on whether the character has a seed in hand.
        /// </summary>
        public override bool OnPicked(Character character)
        {
            if (container?.Inventory == null) { return false; }

            if (pendingAction == PickerAction.EmptyPlanter)
            {
                pendingAction = PickerAction.UprootOrPlant;
                return EmptyPlanter(character);
            }

            SuitablePlantItem plantItem = GetSuitableItem(character);
            
            bool wasDragDropping = dragDroppedItem != null;
            // Clear temporary storage of dragdropped item in any case at this point
            dragDroppedItem = null;
            
            if (plantItem.IsNull())
            {
                // If the dragdropped item is now unsuitable, it means something changed during planting so let's just end here
                if (wasDragDropping)
                {
                    return false;
                }
                
                // Otherwise, uproot as usual
                return TryUpRoot(character);
            }

            switch (plantItem.Type)
            {
                case PlantItemType.Seed:
                    ApplyStatusEffects(ActionType.OnPicked, 1.0f, character);
                    if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                    {
                        return container.Inventory.TryPutItem(plantItem.Item, character);
                    }
                    else
                    {
                        //let the server handle moving the item
                        return false;
                    }
            }

            return false;
        }

        private bool HasGrowables()
        {
            return GrowableSeeds.Any(s => s != null);
        }

        private bool HasAnythingToEmpty()
        {
            return HasGrowables() || CurrentWater > 0f || Fertilizer > 0f || Salinity > 0f;
        }

        /// <summary>
        /// Attempts to remove a plant from the planter.
        /// </summary>
        /// <param name="character">The character who gets the produce or null if they should drop on the floor.</param>
        private bool TryUpRoot(Character? character)
        {
            Debug.Assert(container != null, "Tried to harvest a planter without an item container.");

            bool anyDecayed = GrowableSeeds.Any(s => s is { } seed && (seed.Decayed || seed.FullyGrown));
            for (var i = 0; i < GrowableSeeds.Length; i++)
            {
                Growable? seed = GrowableSeeds[i];
                if (seed == null) { continue; }

                if (!anyDecayed || seed.Decayed || seed.FullyGrown)
                {
                    ApplyStatusEffects(ActionType.OnPicked, 1.0f, character);
                    if (GameMain.NetworkMember is not { IsClient: true })
                    {
                        container?.Inventory.RemoveItem(seed.Item);
                        Entity.Spawner?.AddItemToRemoveQueue(seed.Item);
                        GrowableSeeds[i] = null;
                        HumanAIController.PlantUprooted(planter: item, offender: character);
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Empties the planter by removing all growables (destroying them, not harvesting) and resetting water, fertilizer,
        /// and salinity to the prefab values.
        /// </summary>
        private bool EmptyPlanter(Character? character)
        {
            bool hadAnyGrowable = GrowableSeeds.Any(s => s != null);
            if (hadAnyGrowable)
            {
                ApplyStatusEffects(ActionType.OnPicked, 1.0f, character);
            }
            if (GameMain.NetworkMember is { IsClient: true })
            {
                return hadAnyGrowable;
            }

            for (int i = 0; i < GrowableSeeds.Length; i++)
            {
                if (GrowableSeeds[i] is not Growable seed) { continue; }
                container?.Inventory.RemoveItem(seed.Item);
                Entity.Spawner?.AddItemToRemoveQueue(seed.Item);
                GrowableSeeds[i] = null;
            }

            // Reset the live planter properties to the prefab starter values
            SerializableProperty.DeserializeProperty(this, nameof(CurrentWater), originalElement);
            SerializableProperty.DeserializeProperty(this, nameof(Fertilizer),   originalElement);
            SerializableProperty.DeserializeProperty(this, nameof(Salinity),     originalElement);

            if (hadAnyGrowable)
            {
                // Treat as an uprooting for outpost reaction purposes, since the plants are destroyed either way
                HumanAIController.PlantUprooted(planter: item, offender: character);
            }

#if SERVER
            // Broadcast the new water/fertilizer/salinity values to clients right away
            ServerUpdateNow();
#endif

            return hadAnyGrowable;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (plantLightRecalculationTimer > 0)
            {
                plantLightRecalculationTimer -= deltaTime;
            }

            // Selecting (clicking) a planter opens its inspection panel 
            canBeSelected = HasBeenPlaced;
            canBePicked = !HasBeenPlaced;

#if SERVER
            serverUpdateTimer += deltaTime;
#endif

            bool recreateHudTexts = false;
            for (var i = 0; i < container?.Inventory.Capacity; i++)
            {
                if (i < 0 || GrowableSeeds.Length <= i) { continue; }

                Item containedItem = container.Inventory.GetItemAt(i);
                Growable? growable = containedItem?.GetComponent<Growable>();

                if (growable != null)
                {
                    recreateHudTexts |= GrowableSeeds[i] != growable;
                    GrowableSeeds[i] = growable;
                    growable.IsActive = true;
                }
                else
                {
                    if (GrowableSeeds[i] is { } oldGrowable)
                    {
                        // Kill the plant if it's somehow removed
                        oldGrowable.Decayed = true;
                        oldGrowable.IsActive = false;
                        recreateHudTexts = true;
                    }
                    GrowableSeeds[i] = null;
                }
            }
#if CLIENT
            CharacterHUD.RecreateHudTexts |= recreateHudTexts;
#endif

            if (hasConnectionPanel && HasPower)
            {
                int waterPercent = (int)Math.Round(NormalizedWaterLevel * 100);
                if (waterPercent != prevSentWaterPercent || waterPercentSignal == null)
                {
                    prevSentWaterPercent = waterPercent;
                    waterPercentSignal = waterPercent.ToString();
                }
                item.SendSignal(waterPercentSignal, "water_%");

                int fertilizerPercent = (int)Math.Round(NormalizedFertilizerLevel * 100);
                if (fertilizerPercent != prevSentFertilizerPercent || fertilizerPercentSignal == null)
                {
                    prevSentFertilizerPercent = fertilizerPercent;
                    fertilizerPercentSignal = fertilizerPercent.ToString();
                }
                item.SendSignal(fertilizerPercentSignal, "fertilizer_%");
                
                // Salinity is sent as an absolute float, so we cache the rounded x100 representation to cleanly detect changes without comparing floats
                int salinityFixed = (int)Math.Round(Salinity * 100);
                if (salinityFixed != prevSentSalinityFixed || salinitySignal == null)
                {
                    prevSentSalinityFixed = salinityFixed;
                    salinitySignal = (salinityFixed / 100f).ToString("0.00", CultureInfo.InvariantCulture);
                }
                item.SendSignal(salinitySignal, "salinity");

                int light = (int)Math.Round(PlantLightAmount);
                if (light != prevSentLight || lightSignal == null)
                {
                    prevSentLight = light;
                    lightSignal = light.ToString();
                }
                item.SendSignal(lightSignal, "light");

                float minHealthRatio = 1f;
                bool hasPlants = false;
                foreach (Growable? g in GrowableSeeds) // if there are many Growables in the planter, use the lowest health value
                {
                    if (g is null || g.MaxHealth <= 0) { continue; }
                    hasPlants = true;
                    float healthRatio = g.Health / g.MaxHealth;
                    if (healthRatio < minHealthRatio) { minHealthRatio = healthRatio; }
                }
                // Health defaults to 100% when the planter is empty, so any alarms wired to it don't have to be disconnected then
                int healthPercent = hasPlants ? (int)Math.Round(minHealthRatio * 100) : 100;
                if (healthPercent != prevSentHealthPercent || healthPercentSignal == null)
                {
                    prevSentHealthPercent = healthPercent;
                    healthPercentSignal = healthPercent.ToString();
                }
                item.SendSignal(healthPercentSignal, "health_%");
            }

            // Only continue from here if this is SP, or if this is the server in MP
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            // Increment timer when planter is placed and not in anyone's inventory
            if (HasBeenPlaced && Item.ParentInventory == null)
            {
                growthTickTimer += deltaTime;
            }

            float growthTickDelay = DebugGrowthMultiplier is float debugMultiplier ? GrowthTickDelay / debugMultiplier : GrowthTickDelay;

            // Cap ticks to 50 to prevent excessive accumulation (for any reason)
            growthTickTimer = Math.Min(growthTickTimer, growthTickDelay * 50);

            // Process accumulated ticks (may fire multiple times per frame, for example
            // with growfaster 1000 at 60fps => ~16 ticks per frame to keep up)
            while (growthTickTimer > growthTickDelay)
            {
                for (int i = 0; i < GrowableSeeds.Length; i++)
                {
                    PlantSlot slot = PlantSlots.GetValueOrDefault(i, NullSlot);
                    if (GrowableSeeds[i] is Growable seed)
                    {
                        seed.OnGrowthTick(this, slot, growthTickDelay);
                    }
                }

                growthTickTimer -= growthTickDelay;
            }

            // Process submersion after the growth tick (which consumes water/fertilizer), otherwise plants requiring ~100% water would never reach it
            if (Item.InWater && HasBeenPlaced)
            {
                CurrentWater += WateringWhenFlooded * deltaTime;
                Salinity += SaltAbsorptionRate * deltaTime;
            }

#if SERVER
            if (serverUpdateTimer > ServerUpdateDelay && NeedsUpdate)
            {
                ServerUpdateNow();
            }
#endif
        }

        private SuitablePlantItem GetSuitableItem(Character character, Item? overridingItem = null)
        {
            if (container?.Inventory == null || GrowableSeeds.Length == 0)
            {
                return NullItem;
            }

            IEnumerable<Item> itemsToCheck =
                overridingItem != null ? overridingItem.ToEnumerable() : // an explicitly provided item (a seed being dragged, not in hands)
                dragDroppedItem != null ? dragDroppedItem.ToEnumerable() : // the previously stashed dragged item (re-validated when the planting finishes)
                character.HeldItems;

            foreach (Item itemToCheck in itemsToCheck)
            {
                if (itemToCheck.GetComponent<Growable>() is not Growable growable) { continue; }
                if (!container.Inventory.CanBePut(itemToCheck)) { continue; }
                if (!SuitableSeeds.Any(relatedItem => relatedItem.MatchesItem(itemToCheck))) { continue; }

                // Find the first suitable slot that's available
                int suitableSlot = -1;
                bool hadEmptySlots = false;
                for (int i = 0; i < PlantSlots.Count && i < GrowableSeeds.Length; i++)
                {
                    // If the slot is occupied, skip
                    if (GrowableSeeds[i] != null)
                    {
                        continue;
                    }
                    // This slot was free at least
                    hadEmptySlots = true;
                    // If the slot is also big enough, let's end the search
                    if (growable.MinimumPlanterSize <= PlantSlots[i].Size)
                    {
                        suitableSlot = i;
                        break;
                    }
                }
                // Found a suitable slot
                if (suitableSlot >= 0)
                {
                    return new SuitablePlantItem(itemToCheck, PlantItemType.Seed, MsgPlanting);
                }
                // No suitable slots for this particular seed, but if there are empty slots in the planter, it just means
                // no slots were big enough for this particular seed
                if (hadEmptySlots)
                {
#if CLIENT
                    return new SuitablePlantItem(errorMessage: MsgPlanterTooSmall);
#endif
                }
                // Otherwise it just means there's no space at all
            }

            return NullItem;
        }

        private bool HasAnyFinishedGrowing() => GrowableSeeds.Any(seed => seed != null && (seed.FullyGrown || seed.Decayed));

        public void AddWater(float waterAmount, Character? user, float deltaTime)
        {
            float prevWater = CurrentWater;
            float remainingSalt = Salinity * CurrentWater;
            
            CurrentWater += waterAmount * deltaTime;
            CurrentWater = Math.Clamp(CurrentWater, 0, MaxWater);

            if (Salinity > 0 && !Item.InWater && CurrentWater - prevWater > 0) // Fresh water was actually added
            {
                // Dilute the salinity
                Salinity = remainingSalt / CurrentWater;
            }
            
#if CLIENT
            UpdateProgressBars(user, CurrentWater / MaxWater, GUIStyle.Blue, GUIStyle.Blue, "progressbar.watering");
#endif
        }
        
        public void AddFertilizer(float fertilizerAmount, Character? user, float deltaTime)
        {
            Fertilizer += fertilizerAmount * deltaTime;
            Fertilizer = Math.Clamp(Fertilizer, 0, FertilizerCapacity);
        
#if CLIENT
            UpdateProgressBars(user, Fertilizer / FertilizerCapacity, Color.SaddleBrown, Color.SaddleBrown, "progressbar.fertilizing");
#endif
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (connection.IsPower) { return; }
            
            // In MP, only servers handle signals
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            switch (connection.Name)
            {
                case "set_target_water":
                    if (float.TryParse(signal.value, NumberStyles.Any, CultureInfo.InvariantCulture, out float newTarget) && MathUtils.IsValid(newTarget))
                    {
                        TargetWaterLevel = newTarget;
                    }
                    break;
            }
        }

#if CLIENT
        private void UpdateProgressBars(Character? user, float progress, Color emptyColor, Color fullColor, string textTag)
        {
            if (user == null) { return; }

            for (var i = 0; i < PlantSlots.Count; i++)
            {
                float barOffset = 10f * GUI.Scale;
                Vector2 offset = PlantSlots.TryGetValue(i, out PlantSlot slot) ? slot.Offset : Vector2.Zero;
                user.UpdateHUDProgressBar(
                    linkedObject: this,
                    worldPosition: Item.DrawPosition + new Vector2(barOffset, 0) + offset,
                    progress: progress,
                    emptyColor: emptyColor,
                    fullColor: fullColor,
                    textTag: textTag);
            }
        }
#endif
    }
}
