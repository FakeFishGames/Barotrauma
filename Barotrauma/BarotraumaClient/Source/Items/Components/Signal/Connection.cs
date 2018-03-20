using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Connection
    {
        private static Texture2D panelTexture;
        private static Sprite connector;
        private static Sprite wireVertical;

        public static void DrawConnections(SpriteBatch spriteBatch, ConnectionPanel panel, Character character)
        {
            int width = 400, height = 350;
            int x = (GameMain.GraphicsWidth - width) / 2, y = (GameMain.GraphicsHeight - height) / 2;

            Rectangle panelRect = new Rectangle(x, y, width, height);

            spriteBatch.Draw(panelTexture, panelRect, new Rectangle(0, 312, 400, 200), Color.White);

            //GUI.DrawRectangle(spriteBatch, panelRect, Color.Black, true);

            bool mouseInRect = panelRect.Contains(PlayerInput.MousePosition);

            int totalWireCount = 0;
            foreach (Connection c in panel.Connections)
            {
                totalWireCount += c.Wires.Count(w => w != null);
            }

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

            Vector2 rightPos = new Vector2(x + width - 130, y + 80);
            Vector2 leftPos = new Vector2(x + 130, y + 80);

            Vector2 rightWirePos = new Vector2(x + width - 5, y + 30);
            Vector2 leftWirePos = new Vector2(x + 5, y + 30);

            int wireInterval = (height - 20) / Math.Max(totalWireCount, 1);
            int connectorIntervalLeft = (height - 20) / Math.Max(panel.Connections.Count(c => c.IsOutput), 1);
            int connectorIntervalRight = (height - 20) / Math.Max(panel.Connections.Count(c => !c.IsOutput), 1);

            foreach (Connection c in panel.Connections)
            {
                //if dragging a wire, let the Inventory know so that the wire can be
                //dropped or dragged from the panel to the players inventory
                if (draggingConnected != null)
                {
                    int linkIndex = c.FindWireIndex(draggingConnected.Item);
                    if (linkIndex > -1)
                    {
                        Inventory.draggingItem = c.Wires[linkIndex].Item;
                    }
                }

                //outputs are drawn at the right side of the panel, inputs at the left
                if (c.IsOutput)
                {
                    c.Draw(spriteBatch, panel.Item, rightPos,
                        new Vector2(rightPos.X - GUI.SmallFont.MeasureString(c.Name).X - 20, rightPos.Y + 3),
                        rightWirePos,
                        mouseInRect, equippedWire,
                        wireInterval);

                    rightPos.Y += connectorIntervalLeft;
                    rightWirePos.Y += c.Wires.Count(w => w != null) * wireInterval;
                }
                else
                {
                    c.Draw(spriteBatch, panel.Item, leftPos,
                        new Vector2(leftPos.X + 20, leftPos.Y - 12),
                        leftWirePos,
                        mouseInRect, equippedWire,
                        wireInterval);

                    leftPos.Y += connectorIntervalRight;
                    leftWirePos.Y += c.Wires.Count(w => w != null) * wireInterval;
                    //leftWireX -= wireInterval;
                }
            }

            if (draggingConnected != null)
            {
                DrawWire(spriteBatch, draggingConnected, draggingConnected.Item, PlayerInput.MousePosition, new Vector2(x + width / 2, y + height), mouseInRect, null);

                if (!PlayerInput.LeftButtonHeld())
                {
                    if (GameMain.Client != null)
                    {
                        panel.Item.CreateClientEvent(panel);
                    }
                    else if (GameMain.Server != null)
                    {
                        panel.Item.CreateServerEvent(panel);
                    }

                    draggingConnected = null;
                }
            }

            //if the Character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            if (equippedWire != null)
            {
                if (panel.Connections.Find(c => c.Wires.Contains(equippedWire)) == null)
                {
                    DrawWire(spriteBatch, equippedWire, equippedWire.Item,
                        new Vector2(x + width / 2, y + height - 100),
                        new Vector2(x + width / 2, y + height), mouseInRect, null);

                    if (draggingConnected == equippedWire) Inventory.draggingItem = equippedWire.Item;
                }
            }

            //stop dragging a wire item if cursor is outside the panel
            if (mouseInRect) Inventory.draggingItem = null;


            spriteBatch.Draw(panelTexture, panelRect, new Rectangle(0, 0, 400, 200), Color.White);

        }

        private void Draw(SpriteBatch spriteBatch, Item item, Vector2 position, Vector2 labelPos, Vector2 wirePosition, bool mouseIn, Wire equippedWire, float wireInterval)
        {
            //spriteBatch.DrawString(GUI.SmallFont, Name, new Vector2(labelPos.X, labelPos.Y-10), Color.White);
            GUI.DrawString(spriteBatch, labelPos, Name, IsPower ? Color.Red : Color.White, Color.Black, 0, GUI.SmallFont);

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)position.X - 10, (int)position.Y - 10, 20, 20), Color.White);
            spriteBatch.Draw(panelTexture, position - new Vector2(16.0f, 16.0f), new Rectangle(64, 256, 32, 32), Color.White);

            for (int i = 0; i < MaxLinked; i++)
            {
                if (Wires[i] == null || Wires[i].Hidden || draggingConnected == Wires[i]) continue;

                Connection recipient = Wires[i].OtherConnection(this);

                DrawWire(spriteBatch, Wires[i], (recipient == null) ? Wires[i].Item : recipient.item, position, wirePosition, mouseIn, equippedWire);

                wirePosition.Y += wireInterval;
            }

            if (draggingConnected != null && Vector2.Distance(position, PlayerInput.MousePosition) < 13.0f)
            {
                spriteBatch.Draw(panelTexture, position - new Vector2(21.5f, 21.5f), new Rectangle(106, 250, 43, 43), Color.White);

                if (!PlayerInput.LeftButtonHeld())
                {
                    //find an empty cell for the new connection
                    int index = FindWireIndex(null);

                    if (index > -1 && !Wires.Contains(draggingConnected))
                    {
                        bool alreadyConnected = draggingConnected.IsConnectedTo(item);

                        draggingConnected.RemoveConnection(item);

                        if (draggingConnected.Connect(this, !alreadyConnected, true))
                        {
                            var otherConnection = draggingConnected.OtherConnection(this);
                            if (otherConnection == null)
                            {
                                GameServer.Log(Character.Controlled.LogName + " connected a wire to " +
                                    Item.Name + " (" + Name + ")", ServerLog.MessageType.ItemInteraction);
                            }
                            else
                            {
                                GameServer.Log(Character.Controlled.LogName + " connected a wire from " +
                                    Item.Name + " (" + Name + ") to " + otherConnection.item.Name + " (" + otherConnection.Name + ")", ServerLog.MessageType.ItemInteraction);
                            }

                            AddLink(index, draggingConnected);
                        }
                    }
                }
            }

            int screwIndex = (position.Y % 60 < 30) ? 0 : 1;

            if (Wires.Any(w => w != null && w != draggingConnected))
            {
                spriteBatch.Draw(panelTexture, position - new Vector2(16.0f, 16.0f), new Rectangle(screwIndex * 32, 256, 32, 32), Color.White);
            }

        }

        private static void DrawWire(SpriteBatch spriteBatch, Wire wire, Item item, Vector2 end, Vector2 start, bool mouseIn, Wire equippedWire)
        {
            if (draggingConnected == wire)
            {
                if (!mouseIn) return;
                end = PlayerInput.MousePosition;
                start.X = (start.X + end.X) / 2.0f;
            }

            int textX = (int)start.X;
            if (start.X < end.X)
                textX -= 10;
            else
                textX += 10;

            bool canDrag = equippedWire == null || equippedWire == wire;

            float alpha = canDrag ? 1.0f : 0.5f;

            bool mouseOn =
                canDrag &&
                ((PlayerInput.MousePosition.X > Math.Min(start.X, end.X) &&
                PlayerInput.MousePosition.X < Math.Max(start.X, end.X) &&
                MathUtils.LineToPointDistance(start, end, PlayerInput.MousePosition) < 6) ||
                Vector2.Distance(end, PlayerInput.MousePosition) < 20.0f ||
                new Rectangle((start.X < end.X) ? textX - 100 : textX, (int)start.Y - 5, 100, 14).Contains(PlayerInput.MousePosition));

            string label = wire.Locked ? item.Name + "\n" + TextManager.Get("ConnectionLocked") : item.Name;

            GUI.DrawString(spriteBatch,
                new Vector2(start.X < end.X ? textX - GUI.SmallFont.MeasureString(label).X : textX, start.Y - 5.0f),
                label,
                (mouseOn ? Color.Gold : Color.White) * (wire.Locked ? 0.6f : 1.0f), Color.Black * 0.8f,
                3, GUI.SmallFont);

            var wireEnd = end + Vector2.Normalize(start - end) * 30.0f;

            float dist = Vector2.Distance(start, wireEnd);

            if (mouseOn)
            {
                spriteBatch.Draw(wireVertical.Texture, new Rectangle(wireEnd.ToPoint(), new Point(18, (int)dist)), wireVertical.SourceRect,
                    Color.Gold,
                    MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2,     //angle of line (calulated above)
                    new Vector2(6, 0), // point in line about which to rotate
                    SpriteEffects.None,
                    0.0f);
            }
            spriteBatch.Draw(wireVertical.Texture, new Rectangle(wireEnd.ToPoint(), new Point(12, (int)dist)), wireVertical.SourceRect,
                wire.Item.Color * alpha,
                MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2,     //angle of line (calulated above)
                new Vector2(6, 0), // point in line about which to rotate
                SpriteEffects.None,
                0.0f);

            connector.Draw(spriteBatch, end, Color.White, new Vector2(10.0f, 10.0f), MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2);

            if (draggingConnected == null && canDrag)
            {
                if (mouseOn)
                {
                    ConnectionPanel.HighlightedWire = wire;

                    if (!wire.Locked)
                    {
                        //start dragging the wire
                        if (PlayerInput.LeftButtonHeld()) draggingConnected = wire;
                    }
                }
            }
        }
    }
}
