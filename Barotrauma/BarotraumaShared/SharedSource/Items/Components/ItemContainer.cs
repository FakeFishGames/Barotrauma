using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;
using System.Collections.Immutable;
using Barotrauma.Abilities;

namespace Barotrauma.Items.Components
{
    partial class ItemContainer : ItemComponent, IDrawableComponent
    {
        class ActiveContainedItem
        {
            public readonly Item Item;
            public readonly StatusEffect StatusEffect;
            public readonly bool ExcludeBroken;
            public ActiveContainedItem(Item item, StatusEffect statusEffect, bool excludeBroken)
            {
                Item = item;
                StatusEffect = statusEffect;
                ExcludeBroken = excludeBroken;
            }
        }

        class SlotRestrictions
        {
            public readonly int MaxStackSize;
            public readonly List<RelatedItem> ContainableItems;

            public SlotRestrictions(int maxStackSize, List<RelatedItem> containableItems)
            {
                MaxStackSize = maxStackSize;
                ContainableItems = containableItems;
            }

            public bool MatchesItem(Item item)
            {
                return ContainableItems == null || ContainableItems.Count == 0 || ContainableItems.Any(c => c.MatchesItem(item));
            }

            public bool MatchesItem(ItemPrefab itemPrefab)
            {
                return ContainableItems == null || ContainableItems.Count == 0 || ContainableItems.Any(c => c.MatchesItem(itemPrefab));
            }
        }

        private bool alwaysContainedItemsSpawned;

        public ItemInventory Inventory;

        private readonly List<ActiveContainedItem> activeContainedItems = new List<ActiveContainedItem>();
        
        private List<ushort>[] itemIds;

        //how many items can be contained
        private int capacity;
        [Serialize(5, false, description: "How many items can be contained inside this item.")]
        public int Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 0); }
        }

        //how many items can be contained
        private int maxStackSize;
        [Serialize(64, false, description: "How many items can be stacked in one slot. Does not increase the maximum stack size of the items themselves, e.g. a stack of bullets could have a maximum size of 8 but the number of bullets in a specific weapon could be restricted to 6.")]
        public int MaxStackSize
        {
            get { return maxStackSize; }
            set { maxStackSize = Math.Max(value, 1); }
        }

        private bool hideItems;
        [Serialize(true, false, description: "Should the items contained inside this item be hidden."
            + " If set to false, you should use the ItemPos and ItemInterval properties to determine where the items get rendered.")]
        public bool HideItems
        {
            get { return hideItems; }
            set
            {
                hideItems = value;
                Drawable = !hideItems;
            }
        }

        [Serialize("0.0,0.0", false, description: "The position where the contained items get drawn at (offset from the upper left corner of the sprite in pixels).")]
        public Vector2 ItemPos { get; set; }

        [Serialize("0.0,0.0", false, description: "The interval at which the contained items are spaced apart from each other (in pixels).")]
        public Vector2 ItemInterval { get; set; }

        [Serialize(100, false, description: "How many items are placed in a row before starting a new row.")]
        public int ItemsPerRow { get; set; }

        [Serialize(true, false, description: "Should the inventory of this item be visible when the item is selected.")]
        public bool DrawInventory
        {
            get;
            set;
        }

        [Serialize(true, false, "Allow dragging and dropping items to deposit items into this inventory.")]
        public bool AllowDragAndDrop
        {
            get;
            set;
        }

        [Serialize(true, false)]
        public bool AllowSwappingContainedItems
        {
            get;
            set;
        }

        [Serialize(false, false, description: "If set to true, interacting with this item will make the character interact with the contained item(s), automatically picking them up if they can be picked up.")]
        public bool AutoInteractWithContained
        {
            get;
            set;
        }

        [Serialize(true, false)]
        public bool AllowAccess { get; set; }

        [Serialize(false, false)]
        public bool AccessOnlyWhenBroken { get; set; }

        [Serialize(5, false, description: "How many inventory slots the inventory has per row.")]
        public int SlotsPerRow { get; set; }

        private readonly HashSet<string> containableRestrictions = new HashSet<string>();
        [Editable, Serialize("", true, description: "Define items (by identifiers or tags) that bots should place inside this container. If empty, no restrictions are applied.")]
        public string ContainableRestrictions
        {
            get { return string.Join(",", containableRestrictions); }
            set
            {
                StringFormatter.ParseCommaSeparatedStringToCollection(value, containableRestrictions);
            }
        }

        [Editable, Serialize(true, true, description: "Should this container be automatically filled with items?")]
        public bool AutoFill { get; set; }

        private float itemRotation;
        [Serialize(0.0f, false, description: "The rotation in which the contained sprites are drawn (in degrees).")]
        public float ItemRotation
        {
            get { return MathHelper.ToDegrees(itemRotation); }
            set { itemRotation = MathHelper.ToRadians(value); }
        }

        [Serialize("", false, description: "Specify an item for the container to spawn with.")]
        public string SpawnWithId
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the items configured using SpawnWithId spawn if this item is broken.")]
        public bool SpawnWithIdWhenBroken
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the items be injected into the user.")]
        public bool AutoInject
        {
            get;
            set;
        }

        [Serialize(0.5f, false, description: "The health threshold that the user must reach in order to activate the autoinjection.")]
        public float AutoInjectThreshold
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool RemoveContainedItemsOnDeconstruct { get; set; }

        private SlotRestrictions[] slotRestrictions;

        public bool ShouldBeContained(string[] identifiersOrTags, out bool isRestrictionsDefined)
        {
            isRestrictionsDefined = containableRestrictions.Any();
            if (slotRestrictions.None(s => s.MatchesItem(item))) { return false; }
            if (!isRestrictionsDefined) { return true; }
            return identifiersOrTags.Any(id => containableRestrictions.Any(r => r == id));
        }

        public bool ShouldBeContained(Item item, out bool isRestrictionsDefined)
        {
            isRestrictionsDefined = containableRestrictions.Any();
            if (slotRestrictions.None(s => s.MatchesItem(item))) { return false; }
            if (!isRestrictionsDefined) { return true; }
            return containableRestrictions.Any(id => item.Prefab.Identifier == id || item.HasTag(id));
        }

        private ImmutableHashSet<string> containableItemIdentifiers;
        public IEnumerable<string> ContainableItemIdentifiers => containableItemIdentifiers;

        public override bool RecreateGUIOnResolutionChange => true;

        public ItemContainer(Item item, XElement element)
            : base(item, element)
        {
            int totalCapacity = capacity;

            List<RelatedItem> containableItems = null;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "containable":
                        RelatedItem containable = RelatedItem.Load(subElement, returnEmpty: false, parentDebugName: item.Name);
                        if (containable == null)
                        {
                            DebugConsole.ThrowError("Error in item config \"" + item.ConfigFile + "\" - containable with no identifiers.");
                            continue;
                        }
                        containableItems ??= new List<RelatedItem>();
                        containableItems.Add(containable);
                        break;
                    case "subcontainer":
                        totalCapacity += subElement.GetAttributeInt("capacity", 1);
                        break;
                }
            }
            Inventory = new ItemInventory(item, this, totalCapacity, SlotsPerRow);
            slotRestrictions = new SlotRestrictions[totalCapacity];
            for (int i = 0; i < capacity; i++)
            {
                slotRestrictions[i] = new SlotRestrictions(maxStackSize, containableItems);
            }

            int subContainerIndex = capacity;
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "subcontainer") { continue; }
       
                int subCapacity = subElement.GetAttributeInt("capacity", 1);
                int subMaxStackSize = subElement.GetAttributeInt("maxstacksize", maxStackSize);

                List<RelatedItem> subContainableItems = null;
                foreach (XElement subSubElement in subElement.Elements())
                {
                    if (subSubElement.Name.ToString().ToLowerInvariant() != "containable") { continue; }

                    RelatedItem containable = RelatedItem.Load(subSubElement, returnEmpty: false, parentDebugName: item.Name);
                    if (containable == null)
                    {
                        DebugConsole.ThrowError("Error in item config \"" + item.ConfigFile + "\" - containable with no identifiers.");
                        continue;
                    }
                    subContainableItems ??= new List<RelatedItem>();
                    subContainableItems.Add(containable);
                }

                for (int i = subContainerIndex; i < subContainerIndex + subCapacity; i++)
                {
                    slotRestrictions[i] = new SlotRestrictions(subMaxStackSize, subContainableItems);
                }
                subContainerIndex += subCapacity;
            }
            capacity = totalCapacity;
            InitProjSpecific(element);
        }

        public int GetMaxStackSize(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= capacity)
            {
                return 0;
            }
            return slotRestrictions[slotIndex].MaxStackSize;
        }

        partial void InitProjSpecific(XElement element);

        public void OnItemContained(Item containedItem)
        {
            item.SetContainedItemPositions();

            int index = Inventory.FindIndex(containedItem);
            if (index >= 0 && index < slotRestrictions.Length)
            {
                RelatedItem ri = slotRestrictions[index].ContainableItems?.Find(ci => ci.MatchesItem(containedItem));
                if (ri != null)
                {
                    activeContainedItems.RemoveAll(i => i.Item == containedItem);
                    foreach (StatusEffect effect in ri.statusEffects)
                    {
                        activeContainedItems.Add(new ActiveContainedItem(containedItem, effect, ri.ExcludeBroken));
                    }
                }
            }            

            //no need to Update() if this item has no statuseffects and no physics body
            IsActive = activeContainedItems.Count > 0 || Inventory.AllItems.Any(it => it.body != null);
        }

        public override void Move(Vector2 amount)
        {
            SetContainedItemPositions();
        }

        public void OnItemRemoved(Item containedItem)
        {
            activeContainedItems.RemoveAll(i => i.Item == containedItem);

            //deactivate if the inventory is empty
            IsActive = activeContainedItems.Count > 0 || Inventory.AllItems.Any(it => it.body != null);
        }

        public bool CanBeContained(Item item)
        {
            return slotRestrictions.Any(s => s.MatchesItem(item));
        }

        public bool CanBeContained(Item item, int index)
        {
            if (index < 0 || index >= capacity) { return false; }
            return slotRestrictions[index].MatchesItem(item);
        }

        public bool CanBeContained(ItemPrefab itemPrefab)
        {
            return slotRestrictions.Any(s => s.MatchesItem(itemPrefab));
        }

        public bool CanBeContained(ItemPrefab itemPrefab, int index)
        {
            if (index < 0 || index >= capacity) { return false; }
            return slotRestrictions[index].MatchesItem(itemPrefab);
        }

        readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        public override void Update(float deltaTime, Camera cam)
        {
            if (!string.IsNullOrEmpty(SpawnWithId) && !alwaysContainedItemsSpawned)
            {
                SpawnAlwaysContainedItems();
            }

            if (item.ParentInventory is CharacterInventory ownerInventory)
            {
                item.SetContainedItemPositions();

                if (AutoInject)
                {
                    if (ownerInventory?.Owner is Character ownerCharacter && 
                        ownerCharacter.HealthPercentage / 100f <= AutoInjectThreshold &&
                        ownerCharacter.HasEquippedItem(item))
                    {
                        foreach (Item item in Inventory.AllItemsMod)
                        {
                            item.ApplyStatusEffects(ActionType.OnUse, 1.0f, ownerCharacter);
                            item.GetComponent<GeneticMaterial>()?.Equip(ownerCharacter);
                        }
                    }
                }

            }
            else if (item.body != null && 
                item.body.Enabled &&
                item.body.FarseerBody.Awake)
            {
                item.SetContainedItemPositions();
            }
            else if (activeContainedItems.Count == 0)
            {
                IsActive = false;
                return;
            }

            foreach (var activeContainedItem in activeContainedItems)
            {
                Item contained = activeContainedItem.Item;

                if (activeContainedItem.ExcludeBroken && contained.Condition <= 0.0f) { continue; }
                StatusEffect effect = activeContainedItem.StatusEffect;

                if (effect.HasTargetType(StatusEffect.TargetType.This))                 
                    effect.Apply(ActionType.OnContaining, deltaTime, item, item.AllPropertyObjects);
                if (effect.HasTargetType(StatusEffect.TargetType.Contained)) 
                    effect.Apply(ActionType.OnContaining, deltaTime, item, contained.AllPropertyObjects);
                if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                    effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                {
                    targets.Clear();
                    targets.AddRange(effect.GetNearbyTargets(item.WorldPosition, targets));
                    effect.Apply(ActionType.OnActive, deltaTime, item, targets);
                }
            }
        }

        public override bool HasRequiredItems(Character character, bool addMessage, string msg = null)
        {
            return AllowAccess && (!AccessOnlyWhenBroken || Item.Condition <= 0) && base.HasRequiredItems(character, addMessage, msg);
        }

        public override bool Select(Character character)
        {
            if (!AllowAccess) { return false; }
            if (item.Container != null) { return false; }
            if (AccessOnlyWhenBroken)
            {
                if (item.Condition > 0)
                {
                    return false;
                }
            }
            if (AutoInteractWithContained && character.SelectedConstruction == null)
            {
                foreach (Item contained in Inventory.AllItems)
                {
                    if (contained.TryInteract(character))
                    {
                        character.FocusedItem = contained;
                        return false;
                    }
                }
            }
            var abilityItem = new AbilityItem(item);
            character.CheckTalents(AbilityEffectType.OnOpenItemContainer, abilityItem);

            return base.Select(character);
        }

        public override bool Pick(Character picker)
        {
            if (!AllowAccess) { return false; }
            if (AccessOnlyWhenBroken)
            {
                if (item.Condition > 0)
                {
                    return false;
                }
            }
            if (AutoInteractWithContained)
            {
                foreach (Item contained in Inventory.AllItems)
                {
                    if (contained.TryInteract(picker))
                    {
                        picker.FocusedItem = contained;
                        return true;
                    }
                }
            }

            IsActive = true;

            return picker != null;
        }

        public override bool Combine(Item item, Character user)
        {
            if (!AllowDragAndDrop && user != null) { return false; }
            if (!slotRestrictions.Any(s => s.MatchesItem(item))) { return false; }
            if (user != null && !user.CanAccessInventory(Inventory)) { return false; }
            
            if (Inventory.TryPutItem(item, user))
            {            
                IsActive = true;
                if (hideItems && item.body != null) { item.body.Enabled = false; }
                            
                return true;
            }

            return false;
        }

        public override void Drop(Character dropper)
        {
            IsActive = true;
        }

        public override void Equip(Character character)
        {
            IsActive = true;
        }

        public void SetContainedItemPositions()
        {
            Vector2 transformedItemPos = ItemPos * item.Scale;
            Vector2 transformedItemInterval = ItemInterval * item.Scale;
            Vector2 transformedItemIntervalHorizontal = new Vector2(transformedItemInterval.X, 0.0f);
            Vector2 transformedItemIntervalVertical = new Vector2(0.0f, transformedItemInterval.Y);

            if (ItemPos == Vector2.Zero && ItemInterval == Vector2.Zero)
            {
                transformedItemPos = item.Position;
            }
            else
            {
                if (item.body == null)
                {
                    if (item.FlippedX)
                    {
                        transformedItemPos.X = -transformedItemPos.X;
                        transformedItemPos.X += item.Rect.Width;
                        transformedItemInterval.X = -transformedItemInterval.X;
                        transformedItemIntervalHorizontal.X = -transformedItemIntervalHorizontal.X;
                    }
                    if (item.FlippedY)
                    {
                        transformedItemPos.Y = -transformedItemPos.Y;
                        transformedItemPos.Y -= item.Rect.Height;
                        transformedItemInterval.Y = -transformedItemInterval.Y;
                        transformedItemIntervalVertical.Y = -transformedItemIntervalVertical.Y;
                    }
                    transformedItemPos += new Vector2(item.Rect.X, item.Rect.Y);
                    if (Math.Abs(item.Rotation) > 0.01f)
                    {
                        Matrix transform = Matrix.CreateRotationZ(MathHelper.ToRadians(-item.Rotation));
                        transformedItemPos = Vector2.Transform(transformedItemPos, transform);
                        transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                        transformedItemIntervalHorizontal = Vector2.Transform(transformedItemIntervalHorizontal, transform);
                        transformedItemIntervalVertical = Vector2.Transform(transformedItemIntervalVertical, transform);
                    }
                }
                else
                {
                    Matrix transform = Matrix.CreateRotationZ(item.body.Rotation);
                    if (item.body.Dir == -1.0f)
                    {
                        transformedItemPos.X = -transformedItemPos.X;
                        transformedItemInterval.X = -transformedItemInterval.X;
                        transformedItemIntervalHorizontal.X = -transformedItemIntervalHorizontal.X;
                    }
                    transformedItemPos = Vector2.Transform(transformedItemPos, transform);
                    transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                    transformedItemIntervalHorizontal = Vector2.Transform(transformedItemIntervalHorizontal, transform);
                    transformedItemPos += item.Position;
                }
            }            

            float currentRotation = itemRotation;
            if (item.body != null)
            {
                currentRotation *= item.body.Dir;
                currentRotation += item.body.Rotation;
            }

            int i = 0;
            Vector2 currentItemPos = transformedItemPos;
            foreach (Item contained in Inventory.AllItems)
            {
                if (contained.body != null)
                {
                    try
                    {
                        Vector2 simPos = ConvertUnits.ToSimUnits(currentItemPos);
                        contained.body.FarseerBody.SetTransformIgnoreContacts(ref simPos, currentRotation);
                        contained.body.SetPrevTransform(contained.body.SimPosition, contained.body.Rotation);
                        contained.body.UpdateDrawPosition();
                    }
                    catch (Exception e)
                    {
                        DebugConsole.Log("SetTransformIgnoreContacts threw an exception in SetContainedItemPositions (" + e.Message + ")\n" + e.StackTrace.CleanupStackTrace());
                        GameAnalyticsManager.AddErrorEventOnce("ItemContainer.SetContainedItemPositions.InvalidPosition:" + contained.Name,
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            "SetTransformIgnoreContacts threw an exception in SetContainedItemPositions (" + e.Message + ")\n" + e.StackTrace.CleanupStackTrace());
                    }
                    contained.body.Submarine = item.Submarine;
                }

                contained.Rect =
                    new Rectangle(
                        (int)(currentItemPos.X - contained.Rect.Width / 2.0f),
                        (int)(currentItemPos.Y + contained.Rect.Height / 2.0f),
                        contained.Rect.Width, contained.Rect.Height);

                contained.Submarine = item.Submarine;
                contained.CurrentHull = item.CurrentHull;
                contained.SetContainedItemPositions();

                i++;
                if (Math.Abs(ItemInterval.X) > 0.001f && Math.Abs(ItemInterval.Y) > 0.001f)
                {
                    //interval set on both axes -> use a grid layout
                    currentItemPos += transformedItemIntervalHorizontal;
                    if (i % ItemsPerRow == 0)
                    {
                        currentItemPos = transformedItemPos;
                        currentItemPos += transformedItemIntervalVertical * (i / ItemsPerRow);
                    }
                }
                else
                {
                    currentItemPos += transformedItemInterval;
                }
            }
        }

        public override void OnItemLoaded()
        {
            Inventory.AllowSwappingContainedItems = AllowSwappingContainedItems;
            containableItemIdentifiers = slotRestrictions.SelectMany(s => s.ContainableItems?.SelectMany(ri => ri.Identifiers) ?? Enumerable.Empty<string>()).ToImmutableHashSet();
            if (item.Submarine == null || !item.Submarine.Loading)
            {
                SpawnAlwaysContainedItems();
            }
        }

        public override void OnMapLoaded()
        {
            if (itemIds != null)
            {
                for (ushort i = 0; i < itemIds.Length; i++)
                {
                    if (i >= Inventory.Capacity) 
                    {
                        //legacy support: before item stacking was implemented, revolver for example had a separate slot for each bullet
                        //now there's just one, try to put the extra items where they fit (= stack them)
                        Inventory.TryPutItem(item, user: null, createNetworkEvent: false);
                        continue;
                    }
                    foreach (ushort id in itemIds[i])
                    {
                        if (!(Entity.FindEntityByID(id) is Item item)) { continue; }
                        Inventory.TryPutItem(item, i, false, false, null, createNetworkEvent: false, ignoreCondition: true);
                    }
                }
                itemIds = null;
            }

            //outpost and ruins are loaded in multiple stages (each module is loaded separately)
            //spawning items at this point during the generation will cause ID overlaps with the entities in the modules loaded afterwards
            //so let's not spawn them at this point, but in the 1st Update()
            if (item.Submarine?.Info != null && (item.Submarine.Info.IsOutpost || item.Submarine.Info.IsRuin))
            {
                if (SpawnWithId.Length > 0)
                {
                    IsActive = true;
                }
            }
            else
            {
                SpawnAlwaysContainedItems();
            }
        }

        private void SpawnAlwaysContainedItems()
        {
            if (SpawnWithId.Length > 0 && (item.Condition > 0.0f || SpawnWithIdWhenBroken))
            {
                string[] splitIds = SpawnWithId.Split(',');
                foreach (string id in splitIds)
                {
                    ItemPrefab prefab = ItemPrefab.Prefabs.Find(m => m.Identifier == id);
                    if (prefab != null && Inventory != null && Inventory.CanBePut(prefab))
                    {
                        bool isEditor = false;
#if CLIENT
                        isEditor = Screen.Selected == GameMain.SubEditorScreen;
#endif
                        if (!isEditor && (Entity.Spawner == null || Entity.Spawner.Removed) && GameMain.NetworkMember == null)
                        {
                            var spawnedItem = new Item(prefab, Vector2.Zero, null);
                            Inventory.TryPutItem(spawnedItem, null, spawnedItem.AllowedSlots, createNetworkEvent: false); 
                            alwaysContainedItemsSpawned = true;
                        }
                        else
                        {
                            IsActive = true;
                            Entity.Spawner?.AddToSpawnQueue(prefab, Inventory, spawnIfInventoryFull: false, onSpawned: (Item item) => { alwaysContainedItemsSpawned = true; });
                        }
                    }
                }
            }
        }


        protected override void ShallowRemoveComponentSpecific()
        {
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
#if CLIENT
            inventoryTopSprite?.Remove();
            inventoryBackSprite?.Remove();
            inventoryBottomSprite?.Remove();
            ContainedStateIndicator?.Remove();

            if (SubEditorScreen.IsSubEditor())
            {
                Inventory.DeleteAllItems();
                return;
            }
#endif
            Inventory.AllItemsMod.ForEach(it => it.Drop(null));
        }

        public override void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);

            string containedString = componentElement.GetAttributeString("contained", "");
            string[] itemIdStrings = containedString.Split(',');
            itemIds = new List<ushort>[itemIdStrings.Length];
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                itemIds[i] ??= new List<ushort>();
                foreach (string idStr in itemIdStrings[i].Split(';'))
                {
                    if (!int.TryParse(idStr, out int id)) { continue; }
                    itemIds[i].Add(idRemap.GetOffsetId(id));
                }
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);
            string[] itemIdStrings = new string[Inventory.Capacity];
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                var items = Inventory.GetItemsAt(i);
                itemIdStrings[i] = string.Join(';', items.Select(it => it.ID.ToString()));
            }
            componentElement.Add(new XAttribute("contained", string.Join(',', itemIdStrings)));
            return componentElement;
        }
    }
}
