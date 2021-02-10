using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

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

        public ItemInventory Inventory;

        private readonly List<ActiveContainedItem> activeContainedItems = new List<ActiveContainedItem>();
        
        private List<ushort>[] itemIds;

        //how many items can be contained
        private int capacity;
        [Serialize(5, false, description: "How many items can be contained inside this item.")]
        public int Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1); }
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


        [Serialize(false, false, description: "If set to true, interacting with this item will make the character interact with the contained item(s), automatically picking them up if they can be picked up.")]
        public bool AutoInteractWithContained
        {
            get;
            set;
        }

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

        [Serialize(false, false)]
        public bool RemoveContainedItemsOnDeconstruct { get; set; }

        public bool ShouldBeContained(string[] identifiersOrTags, out bool isRestrictionsDefined)
        {
            isRestrictionsDefined = containableRestrictions.Any();
            if (ContainableItems.None(ri => ri.MatchesItem(item))) { return false; }
            if (!isRestrictionsDefined) { return true; }
            return identifiersOrTags.Any(id => containableRestrictions.Any(r => r == id));
        }

        public bool ShouldBeContained(Item item, out bool isRestrictionsDefined)
        {
            isRestrictionsDefined = containableRestrictions.Any();
            if (ContainableItems.None(ri => ri.MatchesItem(item))) { return false; }
            if (!isRestrictionsDefined) { return true; }
            return containableRestrictions.Any(id => item.Prefab.Identifier == id || item.HasTag(id));
        }

        public List<RelatedItem> ContainableItems { get; private set; } = new List<RelatedItem>();

        public IEnumerable<string> GetContainableItemIdentifiers => ContainableItems.SelectMany(ri => ri.Identifiers);

        public override bool RecreateGUIOnResolutionChange => true;

        public ItemContainer(Item item, XElement element)
            : base (item, element)
        {
            Inventory = new ItemInventory(item, this, capacity, SlotsPerRow);
            
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
                        ContainableItems.Add(containable);
                        break;
                }
            }

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public void OnItemContained(Item containedItem)
        {
            item.SetContainedItemPositions();
            
            RelatedItem ri = ContainableItems.Find(x => x.MatchesItem(containedItem));
            if (ri != null)
            {
                activeContainedItems.RemoveAll(i => i.Item == containedItem);
                foreach (StatusEffect effect in ri.statusEffects)
                {
                    activeContainedItems.Add(new ActiveContainedItem(containedItem, effect, ri.ExcludeBroken));
                }
            }

            //no need to Update() if this item has no statuseffects and no physics body
            IsActive = activeContainedItems.Count > 0 || Inventory.AllItems.Any(it => it.body != null);
        }

        public void OnItemRemoved(Item containedItem)
        {
            activeContainedItems.RemoveAll(i => i.Item == containedItem);

            //deactivate if the inventory is empty
            IsActive = activeContainedItems.Count > 0 || Inventory.AllItems.Any(it => it.body != null);
        }

        public bool CanBeContained(Item item)
        {
            if (ContainableItems.Count == 0) { return true; }
            return ContainableItems.Find(c => c.MatchesItem(item)) != null;
        }
        public bool CanBeContained(ItemPrefab itemPrefab)
        {
            if (ContainableItems.Count == 0) { return true; }
            return ContainableItems.Find(c => c.MatchesItem(itemPrefab)) != null;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.ParentInventory is CharacterInventory)
            {
                item.SetContainedItemPositions();
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
                    var targets = new List<ISerializableEntity>();
                    effect.GetNearbyTargets(item.WorldPosition, targets);
                    effect.Apply(ActionType.OnActive, deltaTime, item, targets);
                }
            }
        }

        public override bool HasRequiredItems(Character character, bool addMessage, string msg = null)
        {
            return (!AccessOnlyWhenBroken || Item.Condition <= 0) && base.HasRequiredItems(character, addMessage, msg);
        }

        public override bool Select(Character character)
        {
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
            return base.Select(character);
        }

        public override bool Pick(Character picker)
        {
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
            if (!ContainableItems.Any(it => it.MatchesItem(item))) { return false; }
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
            Vector2 simPos = item.SimPosition;
            Vector2 displayPos = item.Position;
            float currentRotation = itemRotation;
            if (item.body != null)
            {
                currentRotation += item.body.Rotation;
            }

            foreach (Item contained in Inventory.AllItems)
            {
                if (contained.body != null)
                {
                    try
                    {
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
                        (int)(displayPos.X - contained.Rect.Width / 2.0f),
                        (int)(displayPos.Y + contained.Rect.Height / 2.0f),
                        contained.Rect.Width, contained.Rect.Height);

                contained.Submarine = item.Submarine;
                contained.CurrentHull = item.CurrentHull;

                contained.SetContainedItemPositions();
            }
        }

        public override void OnItemLoaded()
        {
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
                        Inventory.TryPutItem(item, i, false, false, null, false);
                    }
                }
                itemIds = null;
            }
            SpawnAlwaysContainedItems();
        }

        private void SpawnAlwaysContainedItems()
        {
            if (SpawnWithId.Length > 0)
            {
                ItemPrefab prefab = ItemPrefab.Prefabs.Find(m => m.Identifier == SpawnWithId);
                if (prefab != null && Inventory != null && Inventory.CanBePut(prefab))
                {
                    Entity.Spawner?.AddToSpawnQueue(prefab, Inventory, spawnIfInventoryFull: false);                    
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
