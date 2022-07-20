using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        //how long the rewiring sound plays after doing changes to the wiring
        const float RewireSoundDuration = 5.0f;

        public static Wire HighlightedWire;

        private SoundChannel rewireSoundChannel;
        private float rewireSoundTimer;

        public float Scale
        {
            get { return GuiFrame.Rect.Width / 400.0f; }
        }

        private Point originalMaxSize;
        private Vector2 originalRelativeSize;

        partial void InitProjSpecific()
        {
            if (GuiFrame == null) { return; }
            originalMaxSize = GuiFrame.RectTransform.MaxSize;
            originalRelativeSize = GuiFrame.RectTransform.RelativeSize;
            CheckForLabelOverlap();
            var content = new GUICustomComponent(new RectTransform(Vector2.One, GuiFrame.RectTransform), DrawConnections, null)
            {
                UserData = this
            };
            content.RectTransform.SetAsFirstChild();

            //prevents inputs from going through the GUICustomComponent to the drag handle
            var blocker = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) 
            { AbsoluteOffset = GUIStyle.ItemFrameOffset },
            style: null);
        }

        public void TriggerRewiringSound()
        {
            rewireSoundTimer = RewireSoundDuration;
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (var _ in DisconnectedWires)
            {
                if (Rand.Range(0.0f, 500.0f) < 1.0f)
                {
                    SoundPlayer.PlaySound("zap", item.WorldPosition, hullGuess: item.CurrentHull);
                    Vector2 baseVel = new Vector2(0.0f, -100.0f);
                    for (int i = 0; i < 5; i++)
                    {
                        var particle = GameMain.ParticleManager.CreateParticle("spark", item.WorldPosition,
                            baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);
                        if (particle != null) { particle.Size *= Rand.Range(0.5f, 1.0f); }
                    }
                }
            }

            rewireSoundTimer -= deltaTime;
            if (user != null && user.SelectedItem == item && rewireSoundTimer > 0.0f)
            {
                if (rewireSoundChannel == null || !rewireSoundChannel.IsPlaying)
                {
                    rewireSoundChannel = SoundPlayer.PlaySound("rewire", item.WorldPosition, hullGuess: item.CurrentHull);
                }
            }
            else
            {
                rewireSoundChannel?.FadeOutAndDispose();
                rewireSoundChannel = null;
                rewireSoundTimer = 0.0f;
            }
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (item.Submarine == null || item.Submarine.Loading || Screen.Selected != GameMain.SubEditorScreen) { return; }
            MoveConnectedWires(amount);
        }
        
        public override bool ShouldDrawHUD(Character character)
        {
            return character == Character.Controlled && character == user && character.SelectedItem == item;
        }
        
        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (character != Character.Controlled || character != user || character.SelectedItem != item) { return; }
            
            if (HighlightedWire != null)
            {
                HighlightedWire.Item.IsHighlighted = true;
                if (HighlightedWire.Connections[0] != null && HighlightedWire.Connections[0].Item != null) HighlightedWire.Connections[0].Item.IsHighlighted = true;
                if (HighlightedWire.Connections[1] != null && HighlightedWire.Connections[1].Item != null) HighlightedWire.Connections[1].Item.IsHighlighted = true;
            }
        }

        private void DrawConnections(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (user != Character.Controlled || user == null) { return; }

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, user);

            foreach (UISprite sprite in GUIStyle.GetComponentStyle("ConnectionPanelFront").Sprites[GUIComponent.ComponentState.None])
            {
                sprite.Draw(spriteBatch, GuiFrame.Rect, Color.White, SpriteEffects.None);
            }
        }

        protected override void OnResolutionChanged()
        {
            base.OnResolutionChanged();
            if (GuiFrame == null) { return; }
            CheckForLabelOverlap();
        }

        private void CheckForLabelOverlap()
        {
            GuiFrame.RectTransform.MaxSize = originalMaxSize;
            GuiFrame.RectTransform.Resize(originalRelativeSize);
            if (Connection.CheckConnectionLabelOverlap(this, out Point newRectSize))
            {
                int xCenter = (int)(GameMain.GraphicsWidth / 2.0f);
                int maxNewWidth = 2 * Math.Min(xCenter - HUDLayoutSettings.CrewArea.Right, xCenter - HUDLayoutSettings.ChatBoxArea.Right);
                int yCenter = (int)(GameMain.GraphicsHeight / 2.0f);
                int maxNewHeight = 2 * Math.Min(yCenter - HUDLayoutSettings.MessageAreaTop.Bottom, HUDLayoutSettings.InventoryTopY - yCenter);
                // Make sure we don't expand the panel interface too much
                newRectSize = new Point(Math.Min(newRectSize.X, maxNewWidth), Math.Min(newRectSize.Y, maxNewHeight));
                GuiFrame.RectTransform.MaxSize = new Point(
                    Math.Max(GuiFrame.RectTransform.MaxSize.X, newRectSize.X),
                    Math.Max(GuiFrame.RectTransform.MaxSize.Y, newRectSize.Y));
                GuiFrame.RectTransform.Resize(newRectSize);
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            if (GameMain.Client.MidRoundSyncing)
            {
                //delay reading the state until midround syncing is done
                //because some of the wires connected to the panel may not exist yet
                long msgStartPos = msg.BitPosition;
                msg.ReadUInt16(); //user ID
                foreach (Connection _ in Connections)
                {
                    uint wireCount = msg.ReadVariableUInt32();
                    for (int i = 0; i < wireCount; i++)
                    {
                        msg.ReadUInt16();
                    }
                }
                ushort disconnectedWireCount = msg.ReadUInt16();
                for (int i = 0; i < disconnectedWireCount; i++)
                {
                    msg.ReadUInt16();
                }
                int msgLength = (int)(msg.BitPosition - msgStartPos);
                msg.BitPosition = (int)msgStartPos;
                StartDelayedCorrection(msg.ExtractBits(msgLength), sendingTime, waitForMidRoundSync: true);
            }
            else
            {
                //don't trigger rewiring sounds if the rewiring is being done by the local user (in that case we'll trigger it locally)
                if (Character.Controlled == null || user != Character.Controlled) { TriggerRewiringSound(); }
                ApplyRemoteState(msg);
            }
        }

        private void ApplyRemoteState(IReadMessage msg)
        {
            List<Wire> prevWires = Connections.SelectMany(c => c.Wires).ToList();
            
            ushort userID = msg.ReadUInt16();

            if (userID == 0)
            {
                user = null;
            }
            else
            {
                user = Entity.FindEntityByID(userID) as Character;
                base.IsActive = true;
            }

            foreach (Connection connection in Connections)
            {
                connection.ClearConnections();
            }

            foreach (Connection connection in Connections)
            {
                HashSet<Wire> newWires = new HashSet<Wire>();
                uint wireCount = msg.ReadVariableUInt32();
                for (int i = 0; i < wireCount; i++)
                {
                    ushort wireId = msg.ReadUInt16();

                    if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) { continue; }

                    newWires.Add(wireComponent);
                }

                Wire[] oldWires = connection.Wires.Where(w => !newWires.Contains(w)).ToArray();
                foreach (var wire in oldWires)
                {
                    connection.DisconnectWire(wire);
                }

                foreach (var wire in newWires.Where(w => !connection.Wires.Contains(w)).ToArray())
                {
                    connection.ConnectWire(wire);
                    wire.Connect(connection, false);
                }
            }

            List<Wire> previousDisconnectedWires = new List<Wire>(DisconnectedWires);
            DisconnectedWires.Clear();
            ushort disconnectedWireCount = msg.ReadUInt16();
            for (int i = 0; i < disconnectedWireCount; i++)
            {
                ushort wireId = msg.ReadUInt16();
                if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                Wire wireComponent = wireItem.GetComponent<Wire>();
                if (wireComponent == null) { continue; }
                DisconnectedWires.Add(wireComponent);
                base.IsActive = true;
            }

            foreach (Wire wire in prevWires)
            {
                bool connected = wire.Connections[0] != null || wire.Connections[1] != null;
                if (!connected)
                {
                    foreach (Item item in Item.ItemList)
                    {
                        var connectionPanel = item.GetComponent<ConnectionPanel>();
                        if (connectionPanel != null && connectionPanel.DisconnectedWires.Contains(wire))
                        {
                            connected = true;
                            break;
                        }
                    }
                }
                if (wire.Item.ParentInventory == null && !connected)
                {
                    wire.Item.Drop(null);
                }
            }

            foreach (Wire disconnectedWire in previousDisconnectedWires)
            {
                if (disconnectedWire.Connections[0] == null &&
                    disconnectedWire.Connections[1] == null &&
                    !DisconnectedWires.Contains(disconnectedWire))
                {
                    disconnectedWire.Item.Drop(dropper: null);
                }
            }
        }
    }
}
