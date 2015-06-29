using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{

    class Connection
    {
        private static Sprite connector;
        private static Sprite wireCorner, wireVertical, wireHorizontal;

        //how many wires can be linked to a single connector
        private const int MaxLinked = 5;

        public readonly string name;

        public Wire[] wires;

        private Item item;   

        public readonly bool isOutput;
        
        private static Item draggingConnected;

        int[] wireId;

        public List<Connection> Recipients
        {
            get
            {
                List<Connection> recipients = new List<Connection>();
                for (int i = 0; i<MaxLinked; i++)
                {
                    if (wires[i] == null) continue;
                    Connection recipient = wires[i].OtherConnection(this);
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
                connector = new Sprite("Content/Items/connector.png", new Vector2(0.5f, 0.5f));
                wireCorner = new Sprite("Content/Items/wireCorner.png", new Vector2(0.5f, 0.1f));
                wireVertical = new Sprite("Content/Items/wireVertical.png", new Vector2(0.5f, 0.5f));
                wireHorizontal = new Sprite("Content/Items/wireHorizontal.png", new Vector2(0.5f, 0.5f));
            }

            this.item = item;

            //recipient = new Connection[MaxLinked];
            wires = new Wire[MaxLinked];

            isOutput = (element.Name.ToString() == "output");
            name = ToolBox.GetAttributeString(element, "name", (isOutput) ? "output" : "input");

            wireId = new int[MaxLinked];

            foreach (XElement subElement in element.Elements())
            {
                int index = -1;

                for (int i = 0; i < MaxLinked; i++)
                {
                    if (wireId[i]<1) index = i;
                }
                if (index == -1) break;

                wireId[index] = ToolBox.GetAttributeInt(subElement, "w", -1);
            }

        }

        public int FindEmptyIndex()
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i]==null) return i;
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
                if (wires[i] == null && wireItem == null) return i;
                if (wires[i] != null && wires[i].Item == wireItem) return i;
            }
            return -1;
        }

        public void AddLink(int index, Wire wire)
        {
            //linked[index] = connectedItem;
            //recipient[index] = otherConnection;
            wires[index] = wire;
        }

        //public bool AddLink(Item connectedItem, Connection otherConnection)
        //{
        //    if (linked.Contains(connectedItem)) return false;

        //    for (int i = 0; i<MaxLinked; i++)
        //    {
        //        if (linked[i]!=null) continue;

        //        linked[i] = connectedItem;
        //        return true;
        //    }

        //    return false;
        //}

        public void SendSignal(string signal, Item sender, float power)
        {
            for (int i = 0; i<MaxLinked; i++)
            {
                if (wires[i]==null) continue;

                Connection recipient = wires[i].OtherConnection(this);
                if (recipient == null) continue;

                foreach (ItemComponent ic in recipient.item.components)
                {
                    ic.ReceiveSignal(signal, recipient, sender, power);
                }
            }
        }

        public void ClearConnections()
        {
            for (int i = 0; i<MaxLinked; i++)
            {
                if (wires[i] == null) continue;

                wires[i].RemoveConnection(this);
                wires[i] = null;
            }
        }


        public static void DrawConnections(SpriteBatch spriteBatch, ConnectionPanel panel, Character character)
        {
            
            int width = 400, height = 200;
            int x = Game1.GraphicsWidth/2 - width/2, y = Game1.GraphicsHeight - height;

            Rectangle panelRect = new Rectangle(x, y, width, height);

            GUI.DrawRectangle(spriteBatch, panelRect, Color.Black, true);

            bool mouseInRect = panelRect.Contains(PlayerInput.MousePosition);

            Wire equippedWire = null;
                        //if the character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            for (int i = 0; i < character.SelectedItems.Length; i++)
            {
                Item selectedItem = character.SelectedItems[i];

                if (selectedItem == null) continue;

                Wire wireComponent = selectedItem.GetComponent<Wire>();
                if (wireComponent != null) equippedWire = wireComponent;
            }

            Vector2 rightPos = new Vector2(x + width - 110, y + 20);
            Vector2 leftPos = new Vector2(x + 110, y + 20);
            
            float wireInterval = 10.0f;

            float rightWireX = x+width / 2 + wireInterval;
            float leftWireX = x + width / 2 - wireInterval;
            foreach (Connection c in panel.connections)
            {
                //if dragging a wire, let the Inventory know so that the wire can be
                //dropped or dragged from the panel to the players inventory
                if (draggingConnected != null)
                {
                    int linkIndex = c.FindWireIndex(draggingConnected);
                    if (linkIndex>-1)
                    {
                        Inventory.draggingItem = c.wires[linkIndex].Item;
                    }
                }

                //outputs are drawn at the right side of the panel, inputs at the left
                if (c.isOutput)
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
                        new Vector2(leftPos.X - 100, leftPos.Y),
                        new Vector2(leftWireX, y + height), 
                        mouseInRect, equippedWire != null);

                    leftPos.Y += 30;
                    leftWireX -= wireInterval;
                }
            }
            
            //if the character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            if (equippedWire!=null)
            {
                if (panel.connections.Find(c => c.wires.Contains(equippedWire)) == null)
                {
                    DrawWire(spriteBatch, equippedWire.Item, equippedWire.Item,
                        new Vector2(x + width / 2, y + height - 100),
                        new Vector2(x + width / 2, y + height), mouseInRect, false);

                    if (draggingConnected == equippedWire.Item) Inventory.draggingItem = equippedWire.Item;

                    //break;
                }
            }

            //for (int i = 0; i < character.SelectedItems.Length; i++ )
            //{
            //    Item selectedItem = character.SelectedItems[i];

            //    if (selectedItem == null) continue;

            //    Wire wireComponent = selectedItem.GetComponent<Wire>();


            //}

            //stop dragging a wire item if cursor is outside the panel
            if (mouseInRect) Inventory.draggingItem = null;

            if (draggingConnected != null)
            {
                if (!PlayerInput.LeftButtonDown())
                {
                    panel.Item.NewComponentEvent(panel, true);
                    draggingConnected = null;
                }
            }
        }

        private void Draw(SpriteBatch spriteBatch, Item item, Vector2 position, Vector2 labelPos, Vector2 wirePosition, bool mouseIn, bool wireEquipped)
        {

            spriteBatch.DrawString(GUI.font, name, new Vector2(labelPos.X, labelPos.Y-10), Color.White);

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)position.X-10, (int)position.Y-10, 20, 20), Color.White);
            
            
            for (int i = 0; i<MaxLinked; i++)
            {
                if (wires[i]==null) continue;

                Connection recipient = wires[i].OtherConnection(this);

                DrawWire(spriteBatch, wires[i].Item, (recipient == null) ? wires[i].Item : recipient.item, position, wirePosition, mouseIn, wireEquipped);
                wirePosition.X += (isOutput) ? -20 : 20;
            }            
            
            //dragging a wire and released the mouse -> see if the wire can be connected to this connection
            if (draggingConnected != null
                && !PlayerInput.LeftButtonDown())
            {
                //close enough to the connector -> make a new connection
                if (Vector2.Distance(position, PlayerInput.MousePosition) < 10.0f)
                {
                    //find an empty cell for the new connection
                    int index = FindWireIndex(null);

                    Wire wireComponent = draggingConnected.GetComponent<Wire>();
                    
                    if (index>-1 && wireComponent!=null && !wires.Contains(wireComponent))
                    {
                        wires[index] = wireComponent;
                        wireComponent.Connect(this);
                    }                    
                }
                //far away -> disconnect if the wire is linked to this connector
                else
                {
                    int index = FindWireIndex(draggingConnected);
                    if (index>-1)
                    {
                        wires[index].RemoveConnection(this);
                        wires[index].Item.SetTransform(item.SimPosition, 0.0f);
                        wires[index].Item.Drop();
                        wires[index].Item.body.Enabled = true;
                        wires[index] = null;
                    }
                }                    
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

            Color color = (wireEquipped) ? Color.Gray : Color.White;

            if (Math.Abs(end.X-start.X)<connLength*6.0f)
            {
                wireVertical.DrawTiled(spriteBatch, 
                    new Vector2(end.X - wireVertical.size.X / 2, end.Y + connLength), 
                    new Vector2(wireVertical.size.X, (float)Math.Abs(end.Y - start.Y)), color);
                textX = (int)end.X;
                connector.Draw(spriteBatch, end, color);                
            }
            else
            {
                Vector2 pos = new Vector2(start.X, end.Y + wireCorner.size.Y) - wireVertical.size / 2;
                Vector2 size = new Vector2(wireVertical.size.X, (float)Math.Abs((end.Y + wireCorner.size.Y) - start.Y));
                wireVertical.DrawTiled(spriteBatch, pos, size, color);

                Rectangle rect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
                if (!wireEquipped && rect.Contains(PlayerInput.MousePosition)) mouseOn = true;

                float dir = (end.X > start.X) ? -1.0f : 1.0f;

                wireCorner.Draw(spriteBatch, 
                    new Vector2(start.X, end.Y), color, 0.0f, 1.0f,
                    (end.X > start.X) ? SpriteEffects.None : SpriteEffects.FlipHorizontally);

                float wireStartX = start.X - wireCorner.size.X / 2 * dir;
                float wireEndX = end.X + connLength * dir;

                pos = new Vector2(Math.Min(wireStartX,wireEndX), end.Y - wireVertical.size.Y / 2);
                size = new Vector2(Math.Abs(wireStartX - wireEndX), wireHorizontal.size.Y);

                wireHorizontal.DrawTiled(spriteBatch, pos, size, color);
                rect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
                if (!wireEquipped && rect.Contains(PlayerInput.MousePosition)) mouseOn = true;

                connector.Draw(spriteBatch, end, color, -MathHelper.PiOver2*dir);
            }

            if (draggingConnected == null && !wireEquipped)
            {
                if (mouseOn || Vector2.Distance(end, PlayerInput.MousePosition)<20.0f)
                {
                    item.IsHighlighted = true;
                    //start dragging the wire
                    if (PlayerInput.LeftButtonDown()) draggingConnected = wireItem;                    
                }
            }
                            
            spriteBatch.DrawString(GUI.font, item.Name, 
                new Vector2(textX, start.Y-30), 
                (mouseOn && !wireEquipped) ? Color.Gold : Color.White, 
                MathHelper.PiOver2, 
                GUI.font.MeasureString(item.Name)*0.5f, 
                1.0f, SpriteEffects.None, 0.0f);
        }

        public void Save(XElement parentElement)
        {
            XElement newElement = new XElement(isOutput ? "output" : "input", new XAttribute("name", name));

            Array.Sort(wires, delegate(Wire wire1, Wire wire2)             
            {
                if (wire1 == null) return 1;
                if (wire2 == null) return -1;
                return wire1.Item.ID.CompareTo(wire2.Item.ID); 
            });

            for (int i = 0; i < MaxLinked; i++ )
            {
                if (wires[i] == null) continue;
                
                //Connection recipient = wires[i].OtherConnection(this);

                //int connectionIndex = recipient.item.Connections.FindIndex(x => x == recipient);
                newElement.Add(new XElement("link", 
                    new XAttribute("w", wires[i].Item.ID.ToString())));                
            }
      
            parentElement.Add(newElement);                
        }



        public void ConnectLinked()
        {
            if (wireId == null) return;

            for (int i = 0; i < MaxLinked; i++)
            {
                if (wireId[i] == -1) continue;

                Item wireItem = MapEntity.FindEntityByID(wireId[i]) as Item;

                if (wireItem == null) continue;
                wires[i] = wireItem.GetComponent<Wire>();

                if (wires[i]!=null)
                {
                    wires[i].Item.body.Enabled = false;
                    wires[i].Connect(this, false);
                }
            }

            wireId = null;
        }

    }
}
