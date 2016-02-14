using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{

    class Connection
    {
        private static Texture2D panelTexture;
        private static Sprite connector;
        private static Sprite wireCorner, wireVertical, wireHorizontal;

        //how many wires can be linked to a single connector
        public const int MaxLinked = 5;

        public readonly string Name;

        public Wire[] Wires;

        private Item item;   

        public readonly bool IsOutput;
        
        private static Item draggingConnected;

        private List<StatusEffect> effects;

        public readonly ushort[] wireId;
        
        public bool IsPower
        {
            get;
            private set;
        }

        public List<Connection> Recipients
        {
            get
            {
                List<Connection> recipients = new List<Connection>();
                for (int i = 0; i < MaxLinked; i++)
                {
                    if (Wires[i] == null) continue;
                    Connection recipient = Wires[i].OtherConnection(this);
                    if (recipient != null) recipients.Add(recipient);
                }
                return recipients;
            }
        }

        public Item Item
        {
            get { return item; }
        }

        public Connection(XElement element, Item item)
        {

            if (connector == null)
            {
                panelTexture = Sprite.LoadTexture("Content/Items/connectionpanel.png");

                connector = new Sprite(panelTexture, new Rectangle(448, 80, 64, 64), Vector2.Zero, 0.0f);
                connector.Origin = new Vector2(32.0f, 32.0f);
                wireCorner = new Sprite(panelTexture, new Rectangle(448, 0, 64, 64), Vector2.Zero, 0.0f);
                wireCorner.Origin = new Vector2(32.0f, 32.0f);
                wireVertical = new Sprite(panelTexture, new Rectangle(480, 64, 16, 16), new Vector2(-8.0f, -8.0f), 0.0f);
                wireHorizontal = new Sprite(panelTexture, new Rectangle(496, 64, 16, 16), new Vector2(-8.0f, -8.0f), 0.0f);
            }

            this.item = item;

            //recipient = new Connection[MaxLinked];
            Wires = new Wire[MaxLinked];
            
            IsOutput = (element.Name.ToString() == "output");
            Name = ToolBox.GetAttributeString(element, "name", (IsOutput) ? "output" : "input");

            IsPower = Name == "power_in" || Name == "power" || Name == "power_out";

            effects = new List<StatusEffect>();

            wireId = new ushort[MaxLinked];

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "link":
                        int index = -1;
                        for (int i = 0; i < MaxLinked; i++)
                        {
                            if (wireId[i]<1) index = i;
                        }
                        if (index == -1) break;

                        int id = ToolBox.GetAttributeInt(subElement, "w", 0);
                        if (id<0) id = 0;
                        wireId[index] = (ushort)id;

                        break;

                    case "statuseffect":
                        effects.Add(StatusEffect.Load(subElement));
                        break;
                }
            }
        }

        public int FindEmptyIndex()
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (Wires[i]==null) return i;
            }
            return -1;
        }

        //public int FindLinkIndex(Item item)
        //{
        //    for (int i = 0; i < MaxLinked; i++)
        //    {
        //        if (item == null && recipient[i] == null) return i;
        //        if (recipient[i]!=null && recipient[i].item == item) return i;
        //    }
        //    return -1;
        //}

        public int FindWireIndex(Item wireItem)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (Wires[i] == null && wireItem == null) return i;
                if (Wires[i] != null && Wires[i].Item == wireItem) return i;
            }
            return -1;
        }

        public void AddLink(int index, Wire wire)
        {
            //linked[index] = connectedItem;
            //recipient[index] = otherConnection;
            Wires[index] = wire;
            UpdateRecipients();
        }

        public void UpdateRecipients()
        {

        }

        public void SendSignal(string signal, Item sender, float power)
        {
            for (int i = 0; i<MaxLinked; i++)
            {
                if (Wires[i]==null) continue;

                Connection recipient = Wires[i].OtherConnection(this);
                if (recipient == null) continue;
                if (recipient.item == this.item || recipient.item == sender) continue;

                foreach (ItemComponent ic in recipient.item.components)
                {
                    ic.ReceiveSignal(signal, recipient, this.item, power);
                }

                foreach (StatusEffect effect in recipient.effects)
                {

                    //effect.Apply(ActionType.OnUse, 1.0f, recipient.item, recipient.item);
                    recipient.item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f);
                }
            }
        }

        public void ClearConnections()
        {
            for (int i = 0; i<MaxLinked; i++)
            {
                if (Wires[i] == null) continue;

                Wires[i].RemoveConnection(this);
                Wires[i] = null;
            }            
        }


        public static void DrawConnections(SpriteBatch spriteBatch, ConnectionPanel panel, Character character)
        {

            int width = 400, height = 200;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = GameMain.GraphicsHeight - height;

            Rectangle panelRect = new Rectangle(x, y, width, height);

            spriteBatch.Draw(panelTexture, panelRect, new Rectangle(0, 512 - height, width, height), Color.White);

            //GUI.DrawRectangle(spriteBatch, panelRect, Color.Black, true);

            bool mouseInRect = panelRect.Contains(PlayerInput.MousePosition);

            Wire equippedWire = null;
                        //if the Character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            for (int i = 0; i < character.SelectedItems.Length; i++)
            {
                Item selectedItem = character.SelectedItems[i];

                if (selectedItem == null) continue;

                Wire wireComponent = selectedItem.GetComponent<Wire>();
                if (wireComponent != null) equippedWire = wireComponent;
            }

            Vector2 rightPos = new Vector2(x + width - 110, y + 50);
            Vector2 leftPos = new Vector2(x + 110, y + 50);
            
            float wireInterval = 10.0f;

            float rightWireX = x + width / 2 + wireInterval;
            float leftWireX = x + width / 2 - wireInterval;
            foreach (Connection c in panel.Connections)
            {
                //if dragging a wire, let the Inventory know so that the wire can be
                //dropped or dragged from the panel to the players inventory
                if (draggingConnected != null)
                {
                    int linkIndex = c.FindWireIndex(draggingConnected);
                    if (linkIndex>-1)
                    {
                        Inventory.draggingItem = c.Wires[linkIndex].Item;
                    }
                }

                //outputs are drawn at the right side of the panel, inputs at the left
                if (c.IsOutput)
                {
                    c.Draw(spriteBatch, panel.Item, rightPos, 
                        new Vector2(rightPos.X + 20, rightPos.Y),
                        new Vector2(rightWireX, y + height), 
                        mouseInRect, equippedWire != null);

                    rightPos.Y += 30;
                    rightWireX += wireInterval;
                }
                else
                {
                    c.Draw(spriteBatch, panel.Item, leftPos,
                        new Vector2(leftPos.X - GUI.SmallFont.MeasureString(c.Name).X - 20, leftPos.Y),
                        new Vector2(leftWireX, y + height), 
                        mouseInRect, equippedWire != null);

                    leftPos.Y += 30;
                    leftWireX -= wireInterval;
                }
            }
            
            //if the Character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            if (equippedWire!=null)
            {
                if (panel.Connections.Find(c => c.Wires.Contains(equippedWire)) == null)
                {
                    DrawWire(spriteBatch, equippedWire.Item, equippedWire.Item,
                        new Vector2(x + width / 2, y + height - 100),
                        new Vector2(x + width / 2, y + height), mouseInRect, false);

                    if (draggingConnected == equippedWire.Item) Inventory.draggingItem = equippedWire.Item;

                    //break;
                }
            }

            //for (int i = 0; i < Character.SelectedItems.Length; i++ )
            //{
            //    Item selectedItem = Character.SelectedItems[i];

            //    if (selectedItem == null) continue;

            //    Wire wireComponent = selectedItem.GetComponent<Wire>();


            //}

            //stop dragging a wire item if cursor is outside the panel
            if (mouseInRect) Inventory.draggingItem = null;

            if (draggingConnected != null)
            {
                DrawWire(spriteBatch, draggingConnected, draggingConnected, PlayerInput.MousePosition, new Vector2(x + width / 2, y + height), mouseInRect, false);

                if (!PlayerInput.LeftButtonHeld())
                {
                    panel.Item.NewComponentEvent(panel, true, true);
                    //draggingConnected.Drop(Character);
                    draggingConnected = null;
                }
            }

            spriteBatch.Draw(panelTexture, panelRect, new Rectangle(0, 0, width, height), Color.White);

        }

        private void Draw(SpriteBatch spriteBatch, Item item, Vector2 position, Vector2 labelPos, Vector2 wirePosition, bool mouseIn, bool wireEquipped)
        {

            spriteBatch.DrawString(GUI.SmallFont, Name, new Vector2(labelPos.X, labelPos.Y-10), Color.White);

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)position.X-10, (int)position.Y-10, 20, 20), Color.White);
            spriteBatch.Draw(panelTexture, position - new Vector2(16.0f, 16.0f), new Rectangle(64, 256, 32, 32), Color.White);
            
            for (int i = 0; i<MaxLinked; i++)
            {
                if (Wires[i]==null || draggingConnected == Wires[i].Item) continue;

                Connection recipient = Wires[i].OtherConnection(this);

                DrawWire(spriteBatch, Wires[i].Item, (recipient == null) ? Wires[i].Item : recipient.item, position, wirePosition, mouseIn, wireEquipped);
                wirePosition.X += (IsOutput) ? -20 : 20;
            }            
            
            //dragging a wire and released the mouse -> see if the wire can be connected to this connection
            if (draggingConnected != null
                && !PlayerInput.LeftButtonHeld())
            {
                //close enough to the connector -> make a new connection
                if (Vector2.Distance(position, PlayerInput.MousePosition) < 10.0f)
                {
                    //find an empty cell for the new connection
                    int index = FindWireIndex(null);

                    Wire wireComponent = draggingConnected.GetComponent<Wire>();
                    
                    if (index>-1 && wireComponent!=null && !Wires.Contains(wireComponent))
                    {
                        bool alreadyConnected = wireComponent.IsConnectedTo(item);

                        wireComponent.RemoveConnection(item);

                        Wires[index] = wireComponent;
                        wireComponent.Connect(this, !alreadyConnected);
                    }                    
                }
                //far away -> disconnect if the wire is linked to this connector
                else
                {
                    //int index = FindWireIndex(draggingConnected);
                    //if (index>-1)
                    //{
                    //    Wires[index].RemoveConnection(this);
                    //    //Wires[index].Item.SetTransform(item.SimPosition, 0.0f);
                    //    //Wires[index].Item.Drop();
                    //    //Wires[index].Item.body.Enabled = true;
                    //    Wires[index] = null;
                    //}
                }                    
            }

            int screwIndex = (position.Y % 60 < 30) ? 0 : 1;

            if (Wires.Any(w => w != null && w.Item != draggingConnected))
            {
                spriteBatch.Draw(panelTexture, position - new Vector2(16.0f, 16.0f), new Rectangle(screwIndex*32, 256, 32, 32), Color.White);
            }
            
        }

        private static void DrawWire(SpriteBatch spriteBatch, Item wireItem, Item item, Vector2 end, Vector2 start, bool mouseIn, bool wireEquipped)
        {
            if (draggingConnected == wireItem)
            {
                if (!mouseIn) return;
                end = PlayerInput.MousePosition;
            }

            bool mouseOn = false;

            int textX = (int)start.X;
            float connLength = 10.0f;

            float alpha = wireEquipped ? 0.5f : 1.0f;

            //Color color = (wireEquipped) ? wireItem.Color * 0.5f : wireItem.Color;

            if (Math.Abs(end.X-start.X)<connLength*6.0f)
            {
                wireVertical.DrawTiled(spriteBatch, 
                    new Vector2(end.X - wireVertical.size.X / 2, end.Y + connLength),
                    new Vector2(wireVertical.size.X, (float)Math.Abs(end.Y - start.Y)), wireItem.Color * alpha);
                textX = (int)end.X;
                connector.Draw(spriteBatch, end, Color.White*alpha);

                //spriteBatch.Draw(panelTexture, end, new Rectangle(32, 256, 32, 32), Color.White);
            }
            else
            {
                Vector2 pos = new Vector2(start.X, end.Y + wireCorner.size.Y+1) - wireVertical.size / 2;
                Vector2 size = new Vector2(wireVertical.size.X, (float)Math.Abs((end.Y + wireCorner.size.Y) - start.Y));
                wireVertical.DrawTiled(spriteBatch, pos, size, wireItem.Color * alpha);

                Rectangle rect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
                if (!wireEquipped && rect.Contains(PlayerInput.MousePosition)) mouseOn = true;

                float dir = (end.X > start.X) ? -1.0f : 1.0f;

                wireCorner.Draw(spriteBatch,
                    new Vector2(start.X, end.Y+25), wireItem.Color * alpha, 0.0f, 1.0f,
                    (end.X > start.X) ? SpriteEffects.None : SpriteEffects.FlipHorizontally);

                float wireStartX = start.X - wireCorner.size.X / 2 * dir;
                float wireEndX = end.X + connLength * dir;

                pos = new Vector2(Math.Min(wireStartX,wireEndX), end.Y - wireVertical.size.Y / 2);
                size = new Vector2(Math.Abs(wireStartX - wireEndX), wireHorizontal.size.Y);

                wireHorizontal.DrawTiled(spriteBatch, pos, size, wireItem.Color * alpha);
                rect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
                if (!wireEquipped && rect.Contains(PlayerInput.MousePosition)) mouseOn = true;

                connector.Draw(spriteBatch, end, Color.White*alpha, -MathHelper.PiOver2*dir);
            }

            if (draggingConnected == null && !wireEquipped)
            {
                if (mouseOn || Vector2.Distance(end, PlayerInput.MousePosition)<20.0f)
                {
                    item.IsHighlighted = true;
                    //start dragging the wire
                    if (PlayerInput.LeftButtonHeld()) draggingConnected = wireItem;                    
                }
            }
                            
            spriteBatch.DrawString(GUI.Font, item.Name, 
                new Vector2(textX, start.Y-30), 
                (mouseOn && !wireEquipped) ? Color.Gold : Color.White, 
                MathHelper.PiOver2, 
                GUI.Font.MeasureString(item.Name)*0.5f, 
                1.0f, SpriteEffects.None, 0.0f);
        }

        public void Save(XElement parentElement)
        {
            XElement newElement = new XElement(IsOutput ? "output" : "input", new XAttribute("name", Name));

            Array.Sort(Wires, delegate(Wire wire1, Wire wire2)             
            {
                if (wire1 == null) return 1;
                if (wire2 == null) return -1;
                return wire1.Item.ID.CompareTo(wire2.Item.ID); 
            });

            for (int i = 0; i < MaxLinked; i++ )
            {
                if (Wires[i] == null) continue;
                
                //Connection recipient = wires[i].OtherConnection(this);

                //int connectionIndex = recipient.item.Connections.FindIndex(x => x == recipient);
                newElement.Add(new XElement("link", 
                    new XAttribute("w", Wires[i].Item.ID.ToString())));                
            }
      
            parentElement.Add(newElement);                
        }



        public void ConnectLinked()
        {
            if (wireId == null) return;

            for (int i = 0; i < MaxLinked; i++)
            {
                if (wireId[i] == 0) continue;

                Item wireItem = MapEntity.FindEntityByID(wireId[i]) as Item;

                if (wireItem == null) continue;
                Wires[i] = wireItem.GetComponent<Wire>();

                if (Wires[i]!=null)
                {
                    Wires[i].Item.body.Enabled = false;
                    Wires[i].Connect(this, false, true);
                }
            }

            UpdateRecipients();

            //wireId = null;
        }

    }
}
