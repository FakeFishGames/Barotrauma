using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    class ItemContainer : ItemComponent
    {
        List<RelatedItem> containableItems;
        public ItemInventory inventory;

        private bool hasStatusEffects;

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
            set { hideItems = value; }
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

        public ItemContainer(Item item, XElement element)
            : base (item, element)
        {
            inventory = new ItemInventory(item, this, capacity, hudPos, slotsPerRow);            
            containableItems = new List<RelatedItem>();
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "containable":
                        RelatedItem containable = RelatedItem.Load(subElement);
                        if (containable == null) continue;

                        foreach (StatusEffect effect in containable.statusEffects)
                        {
                            if (effect.type == ActionType.OnContaining) hasStatusEffects = true;
                        }

                        containableItems.Add(containable);

                        break;
                }
            }
        }

        public void RemoveContained(Item item)
        {
            inventory.RemoveItem(item);
        }

        public bool CanBeContained(Item item)
        {
            if (containableItems.Count == 0) return true;
            return (containableItems.Find(x => x.MatchesItem(item)) != null);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (!hasStatusEffects) return;

            foreach (Item contained in inventory.Items)
            {
                if (contained == null || contained.Condition <= 0.0f) continue;
                //if (contained.body != null) contained.body.Enabled = false;

                RelatedItem ri = containableItems.Find(x => x.MatchesItem(contained));
                if (ri == null) continue;

                foreach (StatusEffect effect in ri.statusEffects)
                {
                    if (effect.Targets.HasFlag(StatusEffect.TargetType.This)) effect.Apply(ActionType.OnContaining, deltaTime, item, item.AllPropertyObjects);
                    if (effect.Targets.HasFlag(StatusEffect.TargetType.Contained)) effect.Apply(ActionType.OnContaining, deltaTime, item, contained.AllPropertyObjects);
                }

                //contained.ApplyStatusEffects(ActionType.OnContained, deltaTime);
            }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            base.Draw(spriteBatch);

            if (hideItems || (item.body != null && !item.body.Enabled)) return;

            Vector2 transformedItemPos = itemPos;
            Vector2 transformedItemInterval = itemInterval;
            float currentRotation = itemRotation;
            //float transformedItemRotation = itemRotation;
            if (item.body == null)
            {
                transformedItemPos = new Vector2(item.Rect.X, item.Rect.Y);
                transformedItemPos = transformedItemPos + itemPos;
            }
            else
            {
                //item.body.Enabled = true;

                Matrix transform = Matrix.CreateRotationZ(item.body.Rotation);

                if (item.body.Dir==-1.0f)
                {
                    transformedItemPos.X = -transformedItemPos.X;
                    transformedItemInterval.X = -transformedItemInterval.X;
                }
                transformedItemPos = Vector2.Transform(transformedItemPos, transform);
                transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                
                transformedItemPos += ConvertUnits.ToDisplayUnits(item.body.SimPosition);

                currentRotation += item.body.Rotation;
            }

            foreach (Item containedItem in inventory.Items)
            {
                if (containedItem == null) continue;

                containedItem.Sprite.Draw(
                    spriteBatch, 
                    new Vector2(transformedItemPos.X, -transformedItemPos.Y), 
                    -currentRotation, 
                    1.0f, 
                    (item.body != null && item.body.Dir == -1) ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
                
                transformedItemPos += transformedItemInterval;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (!drawInventory && false) return;

            inventory.Draw(spriteBatch);
        }

        public override bool Pick(Character picker)
        {
            return (picker != null);
        }


        public override bool Combine(Item item)
        {
            if (containableItems.Find(x => x.MatchesItem(item)) == null) return false;
            
            if (inventory.TryPutItem(item))
            {            
                IsActive = true;
                if (hideItems || (item.body!=null && !item.body.Enabled)) item.body.Enabled = false;

                item.container = this.item;
            
                return true;
            }

            return false;            
        }

        public override void OnMapLoaded()
        {
            if (itemIds == null) return;

            for (ushort i = 0; i < itemIds.Length; i++)
            {
                Item item = MapEntity.FindEntityByID(itemIds[i]) as Item;
                if (item == null) continue;

                inventory.TryPutItem(item, i, false);
            }

            itemIds = null;
        }

        public override void Remove()
        {
            base.Remove();

            foreach (Item item in inventory.Items)
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

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);
            
            string[] itemIdStrings = new string[inventory.Items.Length];
            for (int i = 0; i < inventory.Items.Length; i++)
            {
                itemIdStrings[i] = (inventory.Items[i]==null) ? "0" : inventory.Items[i].ID.ToString();
            }

            componentElement.Add(new XAttribute("contained",  string.Join(",",itemIdStrings)));

            return componentElement;
        }
    }
}
