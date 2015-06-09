using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Items.Components
{
    class ItemContainer : ItemComponent
    {
        List<RelatedItem> containableItems;
        public ItemInventory inventory;

        //how many items can be contained
        private int capacity;

        private bool hideItems;
        private bool drawInventory;

        //the position of the first item in the container
        private Vector2 itemPos;

        //item[i].Pos = itemPos + itemInterval*i 
        private Vector2 itemInterval;

        private float itemRotation;

        [HasDefaultValue(5, false)]
        public int Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1); }
        }

        [HasDefaultValue(true, false)]
        public bool HideItems
        {
            get { return hideItems; }
            set { hideItems = value; }
        }

        [HasDefaultValue(false, false)]
        public bool DrawInventory
        {
            get { return drawInventory; }
            set { drawInventory = value; }
        }

        [HasDefaultValue(0.0f, false)]
        public float ItemRotation
        {
            get { return itemRotation; }
            set { itemRotation = value; }
        }

        [HasDefaultValue("0.0,0.0", false)]
        public string ItemPos
        {
            get { return ToolBox.Vector2ToString(itemPos); }
            set { itemPos = ToolBox.ParseToVector2(value); }
        }

        [HasDefaultValue("0.0,0.0", false)]
        public string ItemInterval
        {
            get { return ToolBox.Vector2ToString(itemInterval); }
            set { itemInterval = ToolBox.ParseToVector2(value); }
        }
        
        public ItemContainer(Item item, XElement element)
            : base (item, element)
        {
            inventory = new ItemInventory(this, capacity);            
            containableItems = new List<RelatedItem>();

            //itemPos = ToolBox.GetAttributeVector2(element, "ItemPos", Vector2.Zero);
            //itemPos = ConvertUnits.ToSimUnits(itemPos);

            //itemInterval = ToolBox.GetAttributeVector2(element, "ItemInterval", Vector2.Zero);
            //itemInterval = ConvertUnits.ToSimUnits(itemInterval);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "containable":
                        RelatedItem containable = RelatedItem.Load(subElement);
                        if (containable!=null) containableItems.Add(containable);
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
            return (containableItems.Find(x => x.MatchesItem(item)) != null);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            foreach (Item contained in inventory.items)
            {
                if (contained == null) continue;

                if (contained.body!=null) contained.body.Enabled = false;

                RelatedItem ri = containableItems.Find(x => x.MatchesItem(contained));
                if (ri == null) continue;

                foreach (StatusEffect effect in ri.statusEffects)
                {
                    if (effect.Targets.HasFlag(StatusEffect.Target.This)) effect.Apply(ActionType.OnContaining, deltaTime, item);
                    if (effect.Targets.HasFlag(StatusEffect.Target.Contained)) effect.Apply(ActionType.OnContaining, deltaTime, contained);
                }

                contained.ApplyStatusEffects(ActionType.OnContained, deltaTime);
            }

            //if (hideItems) return;
            
            //Vector2 transformedItemPos;
            //Vector2 transformedItemInterval = itemInterval;
            ////float transformedItemRotation = itemRotation;
            //if (item.body==null)
            //{
            //    transformedItemPos = new Vector2(item.Rect.X, item.Rect.Y);
            //    transformedItemPos = ConvertUnits.ToSimUnits(transformedItemPos) + itemPos;
            //}
            //else
            //{
            //    Matrix transform = Matrix.CreateRotationZ(item.body.Rotation);

            //    transformedItemPos = item.body.Position + Vector2.Transform(itemPos, transform);
            //    transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);                    
            //    //transformedItemRotation += item.body.Rotation;
            //}

            //foreach (Item containedItem in inventory.items)
            //{
            //    if (containedItem == null) continue;

            //    Vector2 itemDist = (transformedItemPos - containedItem.body.Position);
            //    Vector2 force = (itemDist - containedItem.body.LinearVelocity * 0.1f) * containedItem.body.Mass * 60.0f;

            //    containedItem.body.ApplyForce(force);

            //    containedItem.body.SmoothRotate(itemRotation);

            //    transformedItemPos += transformedItemInterval;
            //}
            

        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            if (hideItems || (item.body!=null && !item.body.Enabled)) return;

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
                
                transformedItemPos += ConvertUnits.ToDisplayUnits(item.body.Position);

                currentRotation += item.body.Rotation;
            }

            foreach (Item containedItem in inventory.items)
            {
                if (containedItem == null) continue;

                containedItem.sprite.Draw(
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
            if (picker == null) return false;
            //picker.SelectedConstruction = item;

            return true;
        }


        public override bool Combine(Item item)
        {
            if (containableItems.Find(x => x.MatchesItem(item)) == null) return false;
            
            if (inventory.TryPutItem(item))
            {            
                isActive = true;
                if (hideItems || (item.body!=null && !item.body.Enabled)) item.body.Enabled = false;

                item.container = this.item;
            
                return true;
            }

            return false;            
        }

        public override void OnMapLoaded()
        {
            if (itemIds == null) return;

            for (int i = 0; i < itemIds.Length; i++)
            {
                Item item = MapEntity.FindEntityByID(itemIds[i]) as Item;
                if (item == null) continue;

                inventory.TryPutItem(item, i, false);
            }

            itemIds = null;
        }

        public override void Load(XElement componentElement)
        {
            base.Load(componentElement);

            string containedString = ToolBox.GetAttributeString(componentElement, "contained", "");

            string[] itemIdStrings = containedString.Split(',');

            itemIds = new int[itemIdStrings.Length];
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                int id = -1;
                if (!int.TryParse(itemIdStrings[i], out id)) continue;

                itemIds[i] = id;
            }
        }

        int[] itemIds;

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);
            
            string[] itemIdStrings = new string[inventory.items.Length];
            for (int i = 0; i < inventory.items.Length; i++)
            {
                itemIdStrings[i] = (inventory.items[i]==null) ? "-1" : inventory.items[i].ID.ToString();
            }

            componentElement.Add(new XAttribute("contained",  string.Join(",",itemIdStrings)));

            return componentElement;
        }
    }
}
