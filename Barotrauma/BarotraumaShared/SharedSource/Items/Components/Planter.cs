#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    internal enum PlantItemType
    {
        Seed,
        Fertilizer
    }

    internal readonly struct SuitablePlantItem
    {
        public readonly Item? Item;
        public readonly PlantItemType Type;
        public readonly string ProgressBarMessage;

        public SuitablePlantItem(Item item, PlantItemType type, string progressBarMessage)
        {
            Item = item;
            Type = type;
            ProgressBarMessage = progressBarMessage;
        }

        public bool IsNull() => Item == null;
    }

    internal struct PlantSlot
    {
        public Vector2 Offset;
        public float Size;

        public PlantSlot(ContentXElement element)
        {
            Offset = element.GetAttributeVector2("offset", Vector2.Zero);
            Size = element.GetAttributeFloat("size", 0.5f);
        }

        public PlantSlot(Vector2 offset, float size)
        {
            Offset = offset;
            Size = size;
        }
    }

    internal partial class Planter : Pickable, IDrawableComponent
    {
        public static readonly PlantSlot NullSlot = new PlantSlot();
        public readonly Dictionary<int, PlantSlot> PlantSlots = new Dictionary<int, PlantSlot>();

        private static readonly SuitablePlantItem NullItem = new SuitablePlantItem();
        private const string MsgFertilizer = "ItemMsgAddFertilizer";
        private const string MsgSeed = "ItemMsgPlantSeed";
        private const string MsgHarvest = "ItemMsgHarvest";
        private const string MsgUprooting = "progressbar.uprooting";
        private const string MsgFertilizing = "progressbar.fertilizing";
        private const string MsgPlanting = "progressbar.planting";
        public static float GrowthTickDelay = 1f; // 1 second

        private float fertilizer;

        [Serialize(0f, IsPropertySaveable.Yes, "How much fertilizer the planter has.")]
        public float Fertilizer
        {
            get => fertilizer;
            set => fertilizer = Math.Clamp(value, 0, FertilizerCapacity);
        }

        [Serialize(100f, IsPropertySaveable.Yes, "How much fertilizer can the planter hold.")]
        public float FertilizerCapacity { get; set; }

        public Growable?[] GrowableSeeds = new Growable?[0];

        private readonly List<RelatedItem> SuitableFertilizer = new List<RelatedItem>();
        private readonly List<RelatedItem> SuitableSeeds = new List<RelatedItem>();
        private ItemContainer? container;
        private float growthTickTimer;

        private List<LightComponent>? lightComponents;

        public Planter(Item item, ContentXElement element) : base(item, element)
        {
            canBePicked = true;
            SerializableProperty.DeserializeProperties(this, element);
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "plantslot":
                        PlantSlots.Add(subElement.GetAttributeInt("slot", 0), new PlantSlot(subElement));
                        break;
                    case "suitablefertilizer":
                        SuitableFertilizer.Add(RelatedItem.Load(subElement, true, item.Name));
                        break;
                    case "suitableseed":
                        SuitableSeeds.Add(RelatedItem.Load(subElement, true, item.Name));
                        break;
                }
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            IsActive = true;
#if CLIENT
            var lights = item.GetComponents<LightComponent>();
            if (lights.Any())
            {
                lightComponents = lights.ToList();
                foreach (var light in lightComponents)
                {
                    light.Light.Enabled = false;
                }
            }
#endif
            container = item.GetComponent<ItemContainer>();
            GrowableSeeds = new Growable[container.Capacity];
        }

        public override bool HasRequiredItems(Character character, bool addMessage, LocalizedString? msg = null)
        {
            if (container?.Inventory == null) { return false; }

            SuitablePlantItem plantItem = GetSuitableItem(character);

            if (!plantItem.IsNull())
            {
                Msg = plantItem.Type switch
                {
                    PlantItemType.Seed => MsgSeed,
                    PlantItemType.Fertilizer => MsgFertilizer,
                    _ => throw new ArgumentOutOfRangeException()
                };
                ParseMsg();
                return true;
            }

            if (GrowableSeeds.Any(s => s != null))
            {
                Msg = MsgHarvest;
                ParseMsg();
                return true;
            }

            Msg = string.Empty;
            ParseMsg();
            return false;
        }

        public override bool Pick(Character character)
        {
            SuitablePlantItem plantItem = GetSuitableItem(character);
            PickingMsg = plantItem.IsNull() ? MsgUprooting : plantItem.ProgressBarMessage;

            return base.Pick(character);
        }

        public override bool OnPicked(Character character)
        {
            if (container?.Inventory == null) { return false; }

            SuitablePlantItem plantItem = GetSuitableItem(character);
            if (plantItem.IsNull())
            {
                return TryHarvest(character);
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
                case PlantItemType.Fertilizer when plantItem.Item != null:
                    float canAdd = FertilizerCapacity - Fertilizer;
                    float maxAvailable = plantItem.Item.Condition;
                    float toAdd = Math.Min(canAdd, maxAvailable);
                    plantItem.Item.Condition -= toAdd;
                    fertilizer += toAdd;
#if CLIENT
                    character.UpdateHUDProgressBar(this, Item.DrawPosition, Fertilizer / FertilizerCapacity, Color.SaddleBrown, Color.SaddleBrown, "entityname.fertilizer");
#endif
                    ApplyStatusEffects(ActionType.OnPicked, 1.0f, character);
                    return false;
            }

            return false;
        }

        /// <summary>
        /// Attempts to harvest a fully grown plant or removes a decayed plant if any
        /// </summary>
        /// <param name="character">The character who gets the produce or null if they should drop on the floor.</param>
        /// <returns></returns>
        private bool TryHarvest(Character? character)
        {
            Debug.Assert(container != null, "Tried to harvest a planter without an item container.");

            bool anyDecayed = GrowableSeeds.Any(s => s is { } seed && (seed.Decayed || seed.FullyGrown));
            for (var i = 0; i < GrowableSeeds.Length; i++)
            {
                Growable? seed = GrowableSeeds[i];
                if (seed == null) { continue; }

                if (!anyDecayed || seed.Decayed || seed.FullyGrown)
                {
                    container?.Inventory.RemoveItem(seed.Item);
                    Entity.Spawner?.AddItemToRemoveQueue(seed.Item);
                    GrowableSeeds[i] = null;
                    ApplyStatusEffects(ActionType.OnPicked, 1.0f, character);
                    return true;
                }
            }

            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
            
#if CLIENT
            if (lightComponents != null && lightComponents.Count > 0)
            {
                bool hasSeed = false;
                foreach (Growable? seed in GrowableSeeds)
                {
                    hasSeed |= seed != null;
                }
                foreach (var light in lightComponents)
                {
                    light.Light.Enabled = hasSeed;
                }
            }
#endif

            if (container?.Inventory == null) { return; }

            bool recreateHudTexts = false;
            for (var i = 0; i < container.Inventory.Capacity; i++)
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

            // server handles this
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            float delay = GrowthTickDelay;
            if (Fertilizer > 0)
            {
                delay /= 2f;
                Fertilizer -= deltaTime / 10f;
            }

            if (growthTickTimer > delay)
            {
                for (var i = 0; i < GrowableSeeds.Length; i++)
                {
                    PlantSlot slot = PlantSlots.ContainsKey(i) ? PlantSlots[i] : NullSlot;
                    Growable? seed = GrowableSeeds[i];
                    seed?.OnGrowthTick(this, slot);
                }

                growthTickTimer = 0;
            }
            else if (Item.ParentInventory == null)
            {
                if (item.GetComponent<Holdable>() is { } holdable)
                {
                    if (holdable.Attachable && !holdable.Attached)
                    {
                        return;
                    }
                }

                growthTickTimer += deltaTime;
            }
        }

        private SuitablePlantItem GetSuitableItem(Character character)
        {
            foreach (Item heldItem in character.HeldItems)
            {
                if (container?.Inventory != null && container.Inventory.CanBePut(heldItem))
                {
                    if (heldItem.GetComponent<Growable>() != null && SuitableSeeds.Any(ri => ri.MatchesItem(heldItem)))
                    {
                        return new SuitablePlantItem(heldItem, PlantItemType.Seed, MsgPlanting);
                    }
                }

                if (SuitableFertilizer.Any(ri => ri.MatchesItem(heldItem)))
                {
                    return new SuitablePlantItem(heldItem, PlantItemType.Fertilizer, MsgFertilizing);
                }
            }

            return NullItem;
        }

        private bool HasAnyFinishedGrowing() => GrowableSeeds.Any(seed => seed != null && (seed.FullyGrown || seed.Decayed));
    }
}