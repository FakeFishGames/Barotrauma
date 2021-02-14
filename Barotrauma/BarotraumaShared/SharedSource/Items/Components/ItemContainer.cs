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
        public ItemInventory Inventory;

        private List<Pair<Item, StatusEffect>> itemsWithStatusEffects;
        
        private ushort[] itemIds;

        private class reloadItems
        {
            public Item WorstAmmoInWeapon;
            public Item ReplacementAmmo;
        }

        //how many items can be contained
        private int capacity;
        [Serialize(5, false, description: "How many items can be contained inside this item.")]
        public int Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1); }
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

        [Serialize(false, false, description: "Can the Reload action of the item be triggered by players.")]
        public bool PlayerReloadable { get; set; }

        [Serialize(0.5f, false, description: "Minimum time it takes to remove magazine/ammo")]
        public float UnloadBaseTime { get; set; }

        [Serialize(1.5f, false, description: "Additional time it takes to remove magazine/ammo if the character's skill is 0")]
        public float UnloadUnskilledExtraTime { get; set; }

        [Serialize(0.5f, false, description: "Minimum time it takes to load magazine/ammo")]
        public float ReloadBaseTime { get; set; }

        [Serialize(1.5f, false, description: "Additional time it takes to load magazine/ammo if the character's skill is 0")]
        public float ReloadUnskilledExtraTime { get; set; }

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

            itemsWithStatusEffects = new List<Pair<Item, StatusEffect>>();
        }

        partial void InitProjSpecific(XElement element);

        public void OnItemContained(Item containedItem)
        {
            item.SetContainedItemPositions();
            
            RelatedItem ri = ContainableItems.Find(x => x.MatchesItem(containedItem));
            if (ri != null)
            {
                itemsWithStatusEffects.RemoveAll(i => i.First == containedItem);
                foreach (StatusEffect effect in ri.statusEffects)
                {
                    itemsWithStatusEffects.Add(new Pair<Item, StatusEffect>(containedItem, effect));
                }
            }

            //no need to Update() if this item has no statuseffects and no physics body
            IsActive = itemsWithStatusEffects.Count > 0 || Inventory.Items.Any(it => it?.body != null);
        }

        public void OnItemRemoved(Item containedItem)
        {
            itemsWithStatusEffects.RemoveAll(i => i.First == containedItem);

            //deactivate if the inventory is empty
            IsActive = itemsWithStatusEffects.Count > 0 || Inventory.Items.Any(it => it?.body != null);
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
            else if (itemsWithStatusEffects.Count == 0)
            {
                IsActive = false;
                return;
            }

            foreach (Pair<Item, StatusEffect> itemAndEffect in itemsWithStatusEffects)
            {
                Item contained = itemAndEffect.First;
                if (contained.Condition <= 0.0f) continue;

                StatusEffect effect = itemAndEffect.Second;

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
                foreach (Item contained in Inventory.Items)
                {
                    if (contained == null) continue;
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
                foreach (Item contained in Inventory.Items)
                {
                    if (contained == null) continue;
                    if (contained.TryInteract(picker))
                    {
                        picker.FocusedItem = contained;
                        return true;
                    }
                }
            }

            IsActive = true;

            return (picker != null);
        }

        public override bool Combine(Item item, Character user)
        {
            if (!AllowDragAndDrop && user != null) { return false; }

            if (!ContainableItems.Any(x => x.MatchesItem(item))) { return false; }
            if (user != null && !user.CanAccessInventory(Inventory)) { return false; }
            
            if (Inventory.TryPutItem(item, null))
            {            
                IsActive = true;
                if (hideItems && item.body != null) item.body.Enabled = false;
                            
                return true;
            }

            return false;
        }

        private reloadItems CheckReload(Character character)
        {
            // The properties of ri are null by default which is interpreted in the code as item can't be reloaded or no ammo to use
            reloadItems ri = new reloadItems();

            if (!this.PlayerReloadable) return ri;

            // Return if item's inventory is full and all itmes in it are in perfect condition.
            if (this.Inventory.IsFull() && this.Inventory.Items.All(i => i.Condition == 100)) return ri;

            // Get the type of items storable in the item
            List<string> containableId = new List<string>();
            foreach (RelatedItem containableItem in this.ContainableItems)
            {
                foreach (string itemId in containableItem.Identifiers)
                {
                    containableId.Add(itemId);
                }
            }
            if (containableId.Count == 0) return ri;

            // If the weapon/item is full then look for ammo that is in better condition
            if (this.Inventory.IsFull())
            {
                // get the item in the worst condition from the weapon's inventory
                ri.WorstAmmoInWeapon = this.Inventory.Items.Aggregate((x, y) => x.Condition < y.Condition ? x : y);
                // get the first suitable item from the inventory
                ri.ReplacementAmmo = character.Inventory.FindItem(i => character.SelectedItems.All(si => si != i.ParentInventory.Owner)
                                                                && character.HeadsetSlotItem != i.ParentInventory.Owner
                                                                && character.HeadSlotItem != i.ParentInventory.Owner
                                                                && ri.WorstAmmoInWeapon.Prefab.Identifier == i.Prefab.Identifier
                                                                && i.Condition > ri.WorstAmmoInWeapon.Condition, true);
            }
            // If the weapon/item is not full then look for any suitable ammo
            else
            {
                // get the first suitable item from the inventory
                ri.ReplacementAmmo = character.Inventory.FindItem(i => character.SelectedItems.All(si => si != i.ParentInventory.Owner)
                                                                && character.HeadsetSlotItem != i.ParentInventory.Owner
                                                                && character.HeadSlotItem != i.ParentInventory.Owner
                                                                && containableId.Any(id => id == i.Prefab.Identifier || i.HasTag(id)) && i.Condition > 0, true);
            }
            return ri;
        }

        public override float StartReload(Character character)
        {
            // -1 means no reloading
            float reloadCooldown = -1f;

            reloadItems ri = CheckReload(character);

            // No reloading if item is not reloadable or there is no ammo to use
            if (ri.ReplacementAmmo == null) return reloadCooldown;
                       

            // Calculate additional time modifier (in percentage) for reloading based on the character's required skill levels
            float skillModifier = 0f;
            if (this.requiredSkills.Count >= 1)
            {
                float charSkillSum = 0f;
                foreach (Skill requiredSkill in this.requiredSkills)
                {
                    charSkillSum += character.GetSkillLevel(requiredSkill.Identifier);
                }
                skillModifier = (100f - charSkillSum / this.requiredSkills.Count) / 100f;
            }

            // If there is ammo in the weapon and no empty slot
            if (ri.WorstAmmoInWeapon != null)
            {
                reloadCooldown = UnloadBaseTime + UnloadUnskilledExtraTime * skillModifier + ReloadBaseTime + ReloadUnskilledExtraTime * skillModifier;
            }
            else
            {
                reloadCooldown = ReloadBaseTime + ReloadUnskilledExtraTime * skillModifier;
            }
#if CLIENT
            PlaySound(ActionType.OnReload, character);
#endif
            return reloadCooldown;
        }

        public override bool FinalizeReload(Character character)
        {
            reloadItems ri = CheckReload(character);

            // If there is ammo in the weapon and no empty slot
            if (ri.WorstAmmoInWeapon != null)
            {
                var worstAmmoInWeaponInventoryPosition = ri.WorstAmmoInWeapon.ParentInventory.FindIndex(ri.WorstAmmoInWeapon);
                if (ri.WorstAmmoInWeapon.ParentInventory.TryPutItem(ri.ReplacementAmmo, worstAmmoInWeaponInventoryPosition, true, false, character, true))
                {
                    return true;
                }
            }
            else
            {
                if (this.Inventory.TryPutItem(ri.ReplacementAmmo, character))
                {
                    return true;
                }
            }
            return false;
        }

        public override void AbortReload(Character character)
        {
            // stop reload sound
            return;
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

            foreach (Item contained in Inventory.Items)
            {
                if (contained == null) { continue; }
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
                    if (!(Entity.FindEntityByID(itemIds[i]) is Item item)) { continue; }
                    if (i >= Inventory.Capacity) { continue; }
                    Inventory.TryPutItem(item, i, false, false, null, false);
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
                if (prefab != null && Inventory != null && Inventory.Items.Any(it => it == null))
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

            foreach (Item item in Inventory.Items)
            {
                if (item == null) continue;
                item.Drop(null);
            }               
        }        

        public override void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);

            string containedString = componentElement.GetAttributeString("contained", "");
            string[] itemIdStrings = containedString.Split(',');
            itemIds = new ushort[itemIdStrings.Length];
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                if (!int.TryParse(itemIdStrings[i], out int id)) { continue; }
                itemIds[i] = idRemap.GetOffsetId(id);
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            string[] itemIdStrings = new string[Inventory.Items.Length];
            for (int i = 0; i < Inventory.Items.Length; i++)
            {
                itemIdStrings[i] = (Inventory.Items[i] == null) ? "0" : Inventory.Items[i].ID.ToString();
            }

            componentElement.Add(new XAttribute("contained", string.Join(",", itemIdStrings)));

            return componentElement;
        }
    }
}
