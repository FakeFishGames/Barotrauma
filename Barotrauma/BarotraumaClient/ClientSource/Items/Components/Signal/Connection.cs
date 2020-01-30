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

        private Color flashColor;
        private float flashDuration = 1.5f;
        
        public float FlashTimer { get; private set; }
        public static Wire DraggingConnected { get; private set; }

        public static void DrawConnections(SpriteBatch spriteBatch, ConnectionPanel panel, Character character)
        {
            Rectangle panelRect = panel.GuiFrame.Rect;
            int x = panelRect.X, y = panelRect.Y;
            int width = panelRect.Width, height = panelRect.Height;

            Vector2 scale = new Vector2(GUI.Scale);
            if (panel.GuiFrame.RectTransform.MaxSize.X < int.MaxValue) 
            {
                scale.X = panel.GuiFrame.RectTransform.MaxSize.X / panel.GuiFrame.Rect.Width;
            }
            if (panel.GuiFrame.RectTransform.MaxSize.Y < int.MaxValue)
            {
                scale.Y = panel.GuiFrame.RectTransform.MaxSize.Y / panel.GuiFrame.Rect.Height;
            }

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

                    if (selectedItem == null) { continue; }

                    Wire wireComponent = selectedItem.GetComponent<Wire>();
                    if (wireComponent != null)
                    {
                        equippedWire = wireComponent;
                    }
                }
            }

            //two passes: first the connector, then the wires to get the wires to render in front
            for (int i = 0; i < 2; i++)
            {
                Vector2 rightPos = new Vector2(x + width - 80 * scale.X, y + 60 * scale.Y);
                Vector2 leftPos = new Vector2(x + 80 * scale.X, y + 60 * scale.Y);

                Vector2 rightWirePos = new Vector2(x + width - 5 * scale.X, y + 30 * scale.Y);
                Vector2 leftWirePos = new Vector2(x + 5 * scale.X, y + 30 * scale.Y);

                int wireInterval = (height - (int)(20 * scale.Y)) / Math.Max(totalWireCount, 1);
                int connectorIntervalLeft = (height - (int)(100 * scale.Y)) / Math.Max(panel.Connections.Count(c => c.IsOutput), 1);
                int connectorIntervalRight = (height - (int)(100 * scale.Y)) / Math.Max(panel.Connections.Count(c => !c.IsOutput), 1);

                foreach (Connection c in panel.Connections)
                {
                    //if dragging a wire, let the Inventory know so that the wire can be
                    //dropped or dragged from the panel to the players inventory
                    if (DraggingConnected != null && i == 1)
                    {
                        //the wire can only be dragged out if it's not connected to anything at the other end
                        if (Screen.Selected == GameMain.SubEditorScreen ||
                            (DraggingConnected.Connections[0] == null && DraggingConnected.Connections[1] == null) ||
                            (DraggingConnected.Connections.Contains(c) && DraggingConnected.Connections.Contains(null)))
                        {
                            int linkIndex = c.FindWireIndex(DraggingConnected.Item);
                            if (linkIndex > -1 || panel.DisconnectedWires.Contains(DraggingConnected))
                            {
                                Inventory.draggingItem = DraggingConnected.Item;
                            }
                        }
                    }

                    //outputs are drawn at the right side of the panel, inputs at the left
                    if (c.IsOutput)
                    {
                        if (i == 0)
                        {
                            c.DrawConnection(spriteBatch, panel, rightPos,
                                new Vector2(rightPos.X - GUI.SmallFont.MeasureString(c.DisplayName.ToUpper()).X - 25 * panel.Scale, rightPos.Y + 5 * panel.Scale),
                                scale);
                        }
                        else
                        {
                            c.DrawWires(spriteBatch, panel, rightPos, rightWirePos, mouseInRect, equippedWire, wireInterval);
                        }

                        rightPos.Y += connectorIntervalLeft;
                        rightWirePos.Y += c.Wires.Count(w => w != null) * wireInterval;
                    }
                    else
                    {
                        if (i == 0)
                        {
                            c.DrawConnection(spriteBatch, panel, leftPos,
                                new Vector2(leftPos.X + 25 * panel.Scale, leftPos.Y - 5 * panel.Scale - GUI.SmallFont.MeasureString(c.DisplayName.ToUpper()).Y),
                                scale);
                        }
                        else
                        {
                            c.DrawWires(spriteBatch, panel, leftPos, leftWirePos, mouseInRect, equippedWire, wireInterval);
                        }

                        leftPos.Y += connectorIntervalRight;
                        leftWirePos.Y += c.Wires.Count(w => w != null) * wireInterval;
                    }
                }
            }


            if (DraggingConnected != null)
            {
                if (mouseInRect)
                {
                    DrawWire(spriteBatch, DraggingConnected, PlayerInput.MousePosition, new Vector2(x + width / 2, y + height - 10), null, panel, "");
                }
                panel.TriggerRewiringSound();

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (GameMain.NetworkMember != null || panel.CheckCharacterSuccess(character))
                    {
                        if (DraggingConnected.Connections[0]?.ConnectionPanel == panel ||
                            DraggingConnected.Connections[1]?.ConnectionPanel == panel)
                        {
                            DraggingConnected.RemoveConnection(panel.Item);
                            panel.DisconnectedWires.Add(DraggingConnected);
                        }
                    }

                    if (GameMain.Client != null)
                    {
                        panel.Item.CreateClientEvent(panel);
                    }

                    DraggingConnected = null;
                }
            }

            //if the Character using the panel has a wire item equipped
            //and the wire hasn't been connected yet, draw it on the panel
            if (equippedWire != null && (DraggingConnected != equippedWire || !mouseInRect))
            {
                if (panel.Connections.Find(c => c.Wires.Contains(equippedWire)) == null)
                {
                    DrawWire(spriteBatch, equippedWire, new Vector2(x + width / 2, y + height - 150 * GUI.Scale),
                        new Vector2(x + width / 2, y + height),
                        null, panel, "");

                    if (DraggingConnected == equippedWire) { Inventory.draggingItem = equippedWire.Item; }
                }
            }


            float step = (width * 0.75f) / panel.DisconnectedWires.Count();
            x = (int)(x + width / 2 - step * (panel.DisconnectedWires.Count() - 1) / 2);
            foreach (Wire wire in panel.DisconnectedWires)
            {
                if (wire == DraggingConnected && mouseInRect) { continue; }

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

        private void DrawConnection(SpriteBatch spriteBatch, ConnectionPanel panel, Vector2 position, Vector2 labelPos, Vector2 scale)
        {
            string text = DisplayName.ToUpper();
            Vector2 textSize = GUI.SmallFont.MeasureString(text);

            //nasty
            var labelSprite = GUI.Style.GetComponentStyle("ConnectionPanelLabel")?.Sprites.Values.First().First();
            if (labelSprite != null)
            {
                Rectangle labelArea = new Rectangle(labelPos.ToPoint(), textSize.ToPoint());
                labelArea.Inflate(10 * scale.X, 3 * scale.Y);
                labelSprite.Draw(spriteBatch, labelArea, IsPower ? GUI.Style.Red : Color.SteelBlue);
            }

            GUI.DrawString(spriteBatch, labelPos + Vector2.UnitY, text, Color.Black * 0.8f, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, labelPos, text, GUI.Style.TextColorBright, font: GUI.SmallFont);

            float connectorSpriteScale = (35.0f / connectionSprite.SourceRect.Width) * panel.Scale;
            connectionSprite.Draw(spriteBatch, position, scale: connectorSpriteScale);
        }

        private void DrawWires(SpriteBatch spriteBatch, ConnectionPanel panel, Vector2 position, Vector2 wirePosition, bool mouseIn, Wire equippedWire, float wireInterval)
        {
            float connectorSpriteScale = (35.0f / connectionSprite.SourceRect.Width) * panel.Scale;

            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null || wires[i].Hidden || (DraggingConnected == wires[i] && (mouseIn || Screen.Selected == GameMain.SubEditorScreen))) { continue; }

                Connection recipient = wires[i].OtherConnection(this);
                string label = recipient == null ? "" : recipient.item.Name + $" ({recipient.DisplayName})";
                if (wires[i].Locked) { label += "\n" + TextManager.Get("ConnectionLocked"); }
                DrawWire(spriteBatch, wires[i], position, wirePosition, equippedWire, panel, label);

                wirePosition.Y += wireInterval;
            }

            if (DraggingConnected != null && Vector2.Distance(position, PlayerInput.MousePosition) < (20.0f * GUI.Scale))
            {
                connectionSpriteHighlight.Draw(spriteBatch, position, scale: connectorSpriteScale);

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (GameMain.NetworkMember != null || panel.CheckCharacterSuccess(Character.Controlled))
                    {
                        //find an empty cell for the new connection
                        int index = FindEmptyIndex();
                        if (index > -1 && !Wires.Contains(DraggingConnected))
                        {
                            bool alreadyConnected = DraggingConnected.IsConnectedTo(panel.Item);
                            DraggingConnected.RemoveConnection(panel.Item);
                            if (DraggingConnected.Connect(this, !alreadyConnected, true))
                            {
                                var otherConnection = DraggingConnected.OtherConnection(this);
                                SetWire(index, DraggingConnected);
                            }
                        }
                    }

                    if (GameMain.Client != null)
                    {
                        panel.Item.CreateClientEvent(panel);
                    }
                    DraggingConnected = null;
                }
            }

            if (FlashTimer > 0.0f)
            {
                //the number of flashes depends on the duration, 1 flash per 1 full second
                int flashCycleCount = (int)Math.Max(flashDuration, 1);
                float flashCycleDuration = flashDuration / flashCycleCount;

                //MathHelper.Pi * 0.8f -> the curve goes from 144 deg to 0, 
                //i.e. quickly bumps up from almost full brightness to full and then fades out
                connectionSpriteHighlight.Draw(spriteBatch, position,
                    flashColor * (float)Math.Sin(FlashTimer % flashCycleDuration / flashCycleDuration * MathHelper.Pi * 0.8f), scale: connectorSpriteScale);
            }

            if (Wires.Any(w => w != null && w != DraggingConnected))
            {
                int screwIndex = (int)Math.Floor(position.Y / 30.0f) % screwSprites.Count;
                screwSprites[screwIndex].Draw(spriteBatch, position, scale: connectorSpriteScale);
            }
        }

        public void Flash(Color? color = null, float flashDuration = 1.5f)
        {
            FlashTimer = flashDuration;
            this.flashDuration = flashDuration;
            flashColor = (color == null) ? GUI.Style.Red : (Color)color;
        }

        public void UpdateFlashTimer(float deltaTime)
        {
            if (FlashTimer <= 0) return;
            FlashTimer -= deltaTime;
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
                    GUI.Font.DrawString(spriteBatch, label, start + Vector2.UnitY * 20 * GUI.Scale, Color.White, 45.0f, Vector2.Zero, 1.0f, SpriteEffects.None, 0.0f);
                }
                else
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(start.X < end.X ? textX - GUI.SmallFont.MeasureString(label).X : textX, start.Y - 5.0f),
                        label,
                        wire.Locked ? GUI.Style.TextColorDim : (mouseOn ? Wire.higlightColor : GUI.Style.TextColor), Color.Black * 0.9f,
                        3, GUI.SmallFont);
                }
            }

            var wireEnd = end + Vector2.Normalize(start - end) * 30.0f * panel.Scale;

            float dist = Vector2.Distance(start, wireEnd);

            float wireWidth = 12 * panel.Scale;
            float highlight = 5 * panel.Scale;
            if (mouseOn)
            {
                spriteBatch.Draw(wireVertical.Texture, new Rectangle(wireEnd.ToPoint(), new Point((int)(wireWidth + highlight), (int)dist)), wireVertical.SourceRect,
                    Wire.higlightColor,
                    MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2,
                    new Vector2(wireVertical.size.X / 2, 0), // point in line about which to rotate
                    SpriteEffects.None,
                    0.0f);
            }
            spriteBatch.Draw(wireVertical.Texture, new Rectangle(wireEnd.ToPoint(), new Point((int)wireWidth, (int)dist)), wireVertical.SourceRect,
                wire.Item.Color * alpha,
                MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2,
                new Vector2(wireVertical.size.X / 2, 0), // point in line about which to rotate
                SpriteEffects.None,
                0.0f);

            float connectorScale = wireWidth / (float)wireVertical.SourceRect.Width;

            connector.Draw(spriteBatch, end, Color.White, connector.Origin, MathUtils.VectorToAngle(end - start) + MathHelper.PiOver2, scale: connectorScale);

            if (DraggingConnected == null && canDrag)
            {
                if (mouseOn)
                {
                    ConnectionPanel.HighlightedWire = wire;

                    bool allowRewiring = GameMain.NetworkMember?.ServerSettings == null || GameMain.NetworkMember.ServerSettings.AllowRewiring;
                    if (allowRewiring && !wire.Locked && (!panel.Locked || Screen.Selected == GameMain.SubEditorScreen))
                    {
                        //start dragging the wire
                        if (PlayerInput.PrimaryMouseButtonHeld()) { DraggingConnected = wire; }
                    }
                }
            }
        }
    }
}
