using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Connection
    {
        //private static Texture2D panelTexture;
        private static Sprite connector;
        private static Sprite wireVertical;
        private static Sprite connectionSprite;
        private static Sprite connectionSpriteHighlight;
        private static List<Sprite> screwSprites;

        private static Wire draggingConnected;

        private Color flashColor;
        private float flashDuration = 1.5f;
        public float FlashTimer
        {
            get { return flashTimer; }
        }

        public static Wire DraggingConnected
        {
            get => draggingConnected;
        }

        private float flashTimer;

        public static void DrawConnections(SpriteBatch spriteBatch, ConnectionPanel panel, Character character)
        {
            Rectangle panelRect = panel.GuiFrame.Rect;
            int x = panelRect.X, y = panelRect.Y;
            int width = panelRect.Width, height = panelRect.Height;

            bool mouseInRect = panelRect.Contains(PlayerInput.MousePosition);

            int totalWireCount = 0;
            foreach (Connection c in panel.Connections)
            {
                totalWireCount += c.Wires.Count(w => w != null);
            }

            Wire equippedWire = null;
            
            bool allowRewiring = GameMain.NetworkMember?.ServerSettings == null || GameMain.NetworkMember.ServerSettings.AllowRewiring;
            if (allowRewiring && (!panel.Locked || Screen.Selected == GameMain.SubEditorScreen))
            {
                //if the Character using the panel has a wire item equipped
                //and the wire hasn't been connected yet, draw it on the panel
                for (int i = 0; i < character.SelectedItems.Length; i++)
                {
                    Item selectedItem = character.SelectedItems[i];

                    if (selectedItem == null) continue;

                    Wire wireComponent = selectedItem.GetComponent<Wire>();
                    if (wireComponent != null) equippedWire = wireComponent;
                }
            }

            Vector2 rightPos = new Vector2(x + width - 110 * GUI.xScale, y + 80 * GUI.yScale);
            Vector2 leftPos = new Vector2(x + 110 * GUI.xScale, y + 80 * GUI.yScale);

            Vector2 rightWirePos = new Vector2(x + width - 5 * GUI.xScale, y + 30 * GUI.yScale);
            Vector2 leftWirePos = new Vector2(x + 5 * GUI.xScale, y + 30 * GUI.yScale);

            int wireInterval = (height - (int)(20 * GUI.yScale)) / Math.Max(totalWireCount, 1);
            int connectorIntervalLeft = (height - (int)(100 * GUI.yScale)) / Math.Max(panel.Connections.Count(c => c.IsOutput), 1);
            int connectorIntervalRight = (height - (int)(100 * GUI.yScale)) / Math.Max(panel.Connections.Count(c => !c.IsOutput), 1);

            foreach (Connection c in panel.Connections)
            {
                //if dragging a wire, let the Inventory know so that the wire can be
                //dropped or dragged from the panel to the players inventory
                if (draggingConnected != null)
                {
                    //the wire can only be dragged out if it's not connected to anything at the other end
                    if (Screen.Selected == GameMain.SubEditorScreen ||
                        (draggingConnected.Connections[0] == null && draggingConnected.Connections[1] == null) ||
                        (draggingConnected.Connections.Contains(c) && draggingConnected.Connections.Contains(null)))
                    {
                        int linkIndex = c.FindWireIndex(draggingConnected.Item);
                        if (linkIndex > -1 || panel.DisconnectedWires.Contains(draggingConnected))
                        {
                            Inventory.draggingItem = draggingConnected.Item;
                        }
                    }
                }

                //outputs are drawn at the right side of the panel, inputs at the left
                if (c.IsOutput)
                {
                    c.Draw(spriteBatch, panel, rightPos,
                        new Vector2(rightPos.X - GUI.SmallFont.MeasureString(c.DisplayName).X - 20 * GUI.xScale, rightPos.Y + 3 * GUI.yScale),
                        rightWirePos,
                        mouseInRect, equippedWire,
                        wireInterval);

                    rightPos.Y += connectorIntervalLeft;
                    rightWirePos.Y += c.Wires.Count(w => w != null) * wireInterval;
                }
                else
                {
                    c.Draw(spriteBatch, panel, leftPos,
                        new Vector2(leftPos.X + 20 * GUI.xScale, leftPos.Y - 12 * GUI.yScale),
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
                if (mouseInRect)
                {
                    DrawWire(spriteBatch, draggingConnected, PlayerInput.MousePosition, new Vector2(x + width / 2, y + height - 10), null, panel, "");
                }
                panel.TriggerRewiringSound();

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (GameMain.NetworkMember != null || panel.CheckCharacterSuccess(character))
                    {
                        if (draggingConnected.Connections[0]?.ConnectionPanel == panel ||
                            draggingConnected.Connections[1]?.ConnectionPanel == panel)
                        {
                            draggingConnected.RemoveConnection(panel.Item);
                            panel.DisconnectedWires.Add(draggingConnected);
                        }
                    }

                    if (GameMain.Client != null)
                    {
                        panel.Item.CreateClientEvent(panel);
                    }

                    draggingConnected = null;
                }
            }

            //if the Character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            if (equippedWire != null && (draggingConnected != equippedWire || !mouseInRect))
            {
                if (panel.Connections.Find(c => c.Wires.Contains(equippedWire)) == null)
                {
                    DrawWire(spriteBatch, equippedWire, new Vector2(x + width / 2, y + height - 150 * GUI.Scale),
                        new Vector2(x + width / 2, y + height),
                        null, panel, "");

                    if (draggingConnected == equippedWire) { Inventory.draggingItem = equippedWire.Item; }
                }
            }


            float step = (width * 0.75f) / panel.DisconnectedWires.Count();
            x = (int)(x + width / 2 - step * (panel.DisconnectedWires.Count() - 1) / 2);
            foreach (Wire wire in panel.DisconnectedWires)
            {
                if (wire == draggingConnected && mouseInRect) { continue; }

                Connection recipient = wire.OtherConnection(null);
                string label = recipient == null ? "" : recipient.item.Name + $" ({recipient.DisplayName})";
                if (wire.Locked) { label += "\n" + TextManager.Get("ConnectionLocked"); }
                DrawWire(spriteBatch, wire, new Vector2(x, y + height - 100 * GUI.Scale),
                    new Vector2(x, y + height),
                    null, panel, label);
                x += (int)step;
            }

            //stop dragging a wire item if the cursor is within any connection panel
            //(so we don't drop the item when dropping the wire on a connection)
            if (mouseInRect || GUI.MouseOn?.UserData is ConnectionPanel) { Inventory.draggingItem = null; }       
        }

        private void Draw(SpriteBatch spriteBatch, ConnectionPanel panel, Vector2 position, Vector2 labelPos, Vector2 wirePosition, bool mouseIn, Wire equippedWire, float wireInterval)
        {
            GUI.DrawString(spriteBatch, labelPos, DisplayName, IsPower ? Color.Red : Color.White, Color.Black, 0, GUI.SmallFont);

            connectionSprite.Draw(spriteBatch, position);

            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null || wires[i].Hidden || (draggingConnected == wires[i] && (mouseIn || Screen.Selected == GameMain.SubEditorScreen))) { continue; }

                Connection recipient = wires[i].OtherConnection(this);
                string label = recipient == null ? "" : recipient.item.Name + $" ({recipient.DisplayName})";
                if (wires[i].Locked) { label += "\n" + TextManager.Get("ConnectionLocked"); }
                DrawWire(spriteBatch, wires[i], position, wirePosition, equippedWire, panel, label);

                wirePosition.Y += wireInterval;
            }

            if (draggingConnected != null && Vector2.Distance(position, PlayerInput.MousePosition) < (20.0f * GUI.Scale))
            {
                connectionSpriteHighlight.Draw(spriteBatch, position);

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (GameMain.NetworkMember != null || panel.CheckCharacterSuccess(Character.Controlled))
                    {
                        //find an empty cell for the new connection
                        int index = FindEmptyIndex();
                        if (index > -1 && !Wires.Contains(draggingConnected))
                        {
                            bool alreadyConnected = draggingConnected.IsConnectedTo(panel.Item);
                            draggingConnected.RemoveConnection(panel.Item);
                            if (draggingConnected.Connect(this, !alreadyConnected, true))
                            {
                                var otherConnection = draggingConnected.OtherConnection(this);
                                SetWire(index, draggingConnected);
                            }
                        }
                    }

                    if (GameMain.Client != null)
                    {
                        panel.Item.CreateClientEvent(panel);
                    }
                    draggingConnected = null;
                }
            }

            if (flashTimer > 0.0f)
            {
                //the number of flashes depends on the duration, 1 flash per 1 full second
                int flashCycleCount = (int)Math.Max(flashDuration, 1);
                float flashCycleDuration = flashDuration / flashCycleCount;

                //MathHelper.Pi * 0.8f -> the curve goes from 144 deg to 0, 
                //i.e. quickly bumps up from almost full brightness to full and then fades out
                connectionSpriteHighlight.Draw(spriteBatch, position, flashColor * (float)Math.Sin(flashTimer % flashCycleDuration / flashCycleDuration * MathHelper.Pi * 0.8f));
            }

            if (Wires.Any(w => w != null && w != draggingConnected))
            {
                int screwIndex = (int)Math.Floor(position.Y / 30.0f) % screwSprites.Count;
                screwSprites[screwIndex].Draw(spriteBatch, position);
            }
        }

        public void Flash(Color? color = null, float flashDuration = 1.5f)
        {
            flashTimer = flashDuration;
            this.flashDuration = flashDuration;
            flashColor = (color == null) ? Color.Red : (Color)color;
        }

        public void UpdateFlashTimer(float deltaTime)
        {
            if (flashTimer <= 0) return;
            flashTimer -= deltaTime;
        }

        private static void DrawWire(SpriteBatch spriteBatch, Wire wire, Vector2 end, Vector2 start, Wire equippedWire, ConnectionPanel panel, string label)
        {
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

            if (!string.IsNullOrEmpty(label))
            {
                if (start.Y > panel.GuiFrame.Rect.Bottom - 1.0f)
                {
                    //wire at the bottom of the panel -> draw the text below the panel, tilted 45 degrees
                    GUI.SmallFont.DrawString(spriteBatch, label, start + Vector2.UnitY * 20 * GUI.Scale, Color.White, 45.0f, Vector2.Zero, 1.0f, SpriteEffects.None, 0.0f);
                }
                else
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(start.X < end.X ? textX - GUI.SmallFont.MeasureString(label).X : textX, start.Y - 5.0f),
                        label,
                        (mouseOn ? Color.Gold : Color.White) * (wire.Locked ? 0.6f : 1.0f), Color.Black * 0.8f,
                        3, GUI.SmallFont);
                }
            }

            var wireEnd = end + Vector2.Normalize(start - end) * 30.0f;

            float dist = Vector2.Distance(start, wireEnd);

            if (mouseOn)
            {
                spriteBatch.Draw(wireVertical.Texture, new Rectangle(wireEnd.ToPoint(), new Point(18, (int)dist)), wireVertical.SourceRect,
                    Color.Gold,
                    MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2,
                    new Vector2(6, 0), // point in line about which to rotate
                    SpriteEffects.None,
                    0.0f);
            }
            spriteBatch.Draw(wireVertical.Texture, new Rectangle(wireEnd.ToPoint(), new Point(12, (int)dist)), wireVertical.SourceRect,
                wire.Item.Color * alpha,
                MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2,
                new Vector2(6, 0), // point in line about which to rotate
                SpriteEffects.None,
                0.0f);

            connector.Draw(spriteBatch, end, Color.White, new Vector2(10.0f, 10.0f), MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2);

            if (draggingConnected == null && canDrag)
            {
                if (mouseOn)
                {
                    ConnectionPanel.HighlightedWire = wire;

                    bool allowRewiring = GameMain.NetworkMember?.ServerSettings == null || GameMain.NetworkMember.ServerSettings.AllowRewiring;
                    if (allowRewiring && !wire.Locked && (!panel.Locked || Screen.Selected == GameMain.SubEditorScreen))
                    {
                        //start dragging the wire
                        if (PlayerInput.PrimaryMouseButtonHeld()) { draggingConnected = wire; }
                    }
                }
            }
        }
    }
}
