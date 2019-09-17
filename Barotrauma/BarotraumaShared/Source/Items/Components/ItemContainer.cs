using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemContainer : ItemComponent, IDrawableComponent
    {
        public ItemInventory Inventory;

        private List<Pair<Item, StatusEffect>> itemsWithStatusEffects;
        
        private ushort[] itemIds;

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


        [Serialize(false, false, description: "If set to true, interacting with this item will make the character interact with the contained item(s), automatically picking them up if they can be picked up.")]
        public bool AutoInteractWithContained
        {
            get;
            set;
        }

        [Serialize(5, false, description: "How many inventory slots the inventory has per row.")]
        public int SlotsPerRow { get; set; }

        public List<RelatedItem> ContainableItems { get; private set; }

        public ItemContainer(Item item, XElement element)
            : base (item, element)
        {
            Inventory = new ItemInventory(item, this, capacity, SlotsPerRow);            
            ContainableItems = new List<RelatedItem>();
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "containable":
                        RelatedItem containable = RelatedItem.Load(subElement, item.Name);
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
            IsActive = itemsWithStatusEffects.Count > 0 || containedItem.body != null;
        }

        public void OnItemRemoved(Item containedItem)
        {
            itemsWithStatusEffects.RemoveAll(i => i.First == containedItem);

            //deactivate if the inventory is empty
            IsActive = itemsWithStatusEffects.Count > 0 || containedItem.body != null;
        }

        public bool CanBeContained(Item item)
        {
            if (ContainableItems.Count == 0) return true;
            return (ContainableItems.Find(x => x.MatchesItem(item)) != null);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.body != null && 
                item.body.Enabled &&
                item.body.FarseerBody.Awake)
            {
                item.SetContainedItemPositions();
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

        public override bool Select(Character character)
        {
            if (item.Container != null) { return false; }

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

            return (picker != null);
        }

        public override bool Combine(Item item, Character user)
        {
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

        public void SetContainedItemPositions()
        {
            Vector2 simPos = item.SimPosition;
            Vector2 displayPos = item.Position;

            foreach (Item contained in Inventory.Items)
            {
                if (contained == null) continue;
                if (contained.body != null)
                {
                    try
                    {
                        contained.body.FarseerBody.SetTransformIgnoreContacts(ref simPos, 0.0f);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.Log("SetTransformIgnoreContacts threw an exception in SetContainedItemPositions ("+e.Message+")\n"+e.StackTrace);
                        GameAnalyticsManager.AddErrorEventOnce("ItemContainer.SetContainedItemPositions.InvalidPosition:"+contained.Name,
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            "SetTransformIgnoreContacts threw an exception in SetContainedItemPositions (" + e.Message + ")\n" + e.StackTrace);
                    }
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

        public override void OnMapLoaded()
        {
            if (itemIds == null) return;

            for (ushort i = 0; i < itemIds.Length; i++)
            {
                Item item = Entity.FindEntityByID(itemIds[i]) as Item;
                if (item == null) continue;

                if (i >= Inventory.Capacity)
                {
                    continue;
                }

                Inventory.TryPutItem(item, i, false, false, null, false);
            }

            itemIds = null;
        }

        protected override void ShallowRemoveComponentSpecific()
        {
        }

        protected override void RemoveComponentSpecific()
        {
#if CLIENT
            inventoryTopSprite?.Remove();
            inventoryBackSprite?.Remove();
            inventoryBottomSprite?.Remove();
            ContainedStateIndicator?.Remove();

            if (Screen.Selected == GameMain.SubEditorScreen && !Submarine.Unloading)
            {
                GameMain.SubEditorScreen.HandleContainerContentsDeletion(Item, Inventory);
                return;
            }
#endif

            foreach (Item item in Inventory.Items)
            {
                if (item == null) continue;
                item.Drop(null);
            }               
        }        

        public override void Load(XElement componentElement)
        {
            base.Load(componentElement);

            string containedString = componentElement.GetAttributeString("contained", "");

            string[] itemIdStrings = containedString.Split(',');

            itemIds = new ushort[itemIdStrings.Length];
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                ushort id = 0;
                if (!ushort.TryParse(itemIdStrings[i], out id)) continue;

                itemIds[i] = id;
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
