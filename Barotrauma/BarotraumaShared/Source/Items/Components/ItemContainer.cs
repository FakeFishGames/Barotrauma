using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemContainer : ItemComponent, IDrawableComponent
    {
        public const int MaxInventoryCount = 4;

        List<RelatedItem> containableItems;
        public ItemInventory Inventory;

        private List<Pair<Item, StatusEffect>> itemsWithStatusEffects;

        //how many items can be contained
        [HasDefaultValue(5, false)]
        public int Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1); }
        }
        private int capacity;

        [HasDefaultValue(true, false)]
        public bool HideItems
        {
            get { return hideItems; }
            set 
            { 
                hideItems = value;
                Drawable = !hideItems;
            }
        }
        private bool hideItems;

        [HasDefaultValue(false, false)]
        public bool DrawInventory
        {
            get { return drawInventory; }
            set { drawInventory = value; }
        }
        private bool drawInventory;

        //the position of the first item in the container
        [HasDefaultValue("0.0,0.0", false)]
        public string ItemPos
        {
            get { return ToolBox.Vector2ToString(itemPos); }
            set { itemPos = ToolBox.ParseToVector2(value); }
        }
        private Vector2 itemPos;

        //item[i].Pos = itemPos + itemInterval*i 
        [HasDefaultValue("0.0,0.0", false)]
        public string ItemInterval
        {
            get { return ToolBox.Vector2ToString(itemInterval); }
            set { itemInterval = ToolBox.ParseToVector2(value); }
        }
        private Vector2 itemInterval;

        [HasDefaultValue(0.0f, false)]
        public float ItemRotation
        {
            get { return MathHelper.ToDegrees(itemRotation); }
            set { itemRotation = MathHelper.ToRadians(value); }
        }
        private float itemRotation;


        [HasDefaultValue("0.5,0.9", false)]
        public string HudPos
        {
            get { return ToolBox.Vector2ToString(hudPos); }
            set 
            { 
                hudPos = ToolBox.ParseToVector2(value);
                //inventory.CenterPos = hudPos;
            }
        }
        private Vector2 hudPos;

        [HasDefaultValue(5, false)]
        public int SlotsPerRow
        {
            get { return slotsPerRow; }
            set { slotsPerRow = value; }
        }
        private int slotsPerRow;

        public List<RelatedItem> ContainableItems
        {
            get { return containableItems; }
        }

        public ItemContainer(Item item, XElement element)
            : base (item, element)
        {
            Inventory = new ItemInventory(item, this, capacity, hudPos, slotsPerRow);            
            containableItems = new List<RelatedItem>();
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "containable":
                        RelatedItem containable = RelatedItem.Load(subElement);
                        if (containable == null) continue;
                        
                        containableItems.Add(containable);

                        break;
                }
            }

            itemsWithStatusEffects = new List<Pair<Item, StatusEffect>>();
        }

        public void OnItemContained(Item containedItem)
        {
            item.SetContainedItemPositions();
            
            RelatedItem ri = containableItems.Find(x => x.MatchesItem(containedItem));
            if (ri != null)
            {
                foreach (StatusEffect effect in ri.statusEffects)
                {
                    itemsWithStatusEffects.Add(Pair<Item, StatusEffect>.Create(containedItem, effect));
                }
            }

            //no need to Update() if this item has no statuseffects and no physics body
            IsActive = itemsWithStatusEffects.Count > 0 || containedItem.body != null;
        }

        public void OnItemRemoved(Item item)
        {
            itemsWithStatusEffects.RemoveAll(i => i.First == item);

            //deactivate if the inventory is empty
            IsActive = itemsWithStatusEffects.Count > 0 || item.body != null;
        }

        public bool CanBeContained(Item item)
        {
            if (containableItems.Count == 0) return true;
            return (containableItems.Find(x => x.MatchesItem(item)) != null);
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
                if (contained.Condition < 0.0f) continue;

                StatusEffect effect = itemAndEffect.Second;

                if (effect.Targets.HasFlag(StatusEffect.TargetType.This))                 
                    effect.Apply(ActionType.OnContaining, deltaTime, item, item.AllPropertyObjects);
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Contained)) 
                    effect.Apply(ActionType.OnContaining, deltaTime, item, contained.AllPropertyObjects);               
            }
        }

        public override bool Pick(Character picker)
        {
            return (picker != null);
        }


        public override bool Combine(Item item)
        {
            if (!containableItems.Any(x => x.MatchesItem(item))) return false;
            
            if (Inventory.TryPutItem(item, null))
            {            
                IsActive = true;
                if (hideItems && item.body != null) item.body.Enabled = false;
                            
                return true;
            }

            return false;            
        }

        public override void OnMapLoaded()
        {
            if (itemIds == null) return;

            for (ushort i = 0; i < itemIds.Length; i++)
            {
                Item item = Entity.FindEntityByID(itemIds[i]) as Item;
                if (item == null) continue;

                Inventory.TryPutItem(item, i, false, null, false);
            }

            itemIds = null;
        }

        protected override void ShallowRemoveComponentSpecific()
        {
        }

        protected override void RemoveComponentSpecific()
        {
            foreach (Item item in Inventory.Items)
            {
                if (item == null) continue;
                item.Remove();
            }
        }

        public override void Load(XElement componentElement)
        {
            base.Load(componentElement);

            string containedString = ToolBox.GetAttributeString(componentElement, "contained", "");

            string[] itemIdStrings = containedString.Split(',');

            itemIds = new ushort[itemIdStrings.Length];
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                ushort id = 0;
                if (!ushort.TryParse(itemIdStrings[i], out id)) continue;

                itemIds[i] = id;
            }
        }

        ushort[] itemIds;
    }
}
